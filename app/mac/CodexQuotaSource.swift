import Carbon
import Cocoa
import ServiceManagement
import WebKit

// MARK: GPT（Codex）额度来源
// 起 `codex app-server`，问 account/rateLimits/read——Codex 用它自己的登录状态向 OpenAI 查实时额度
// （等同于在 Codex 里看使用情况，不对话、不耗额度；CrossPet 不碰凭据）。
// 不再读本机会话记录：Codex 回复了也不一定往里写额度，读出来的常常是旧数还看着像新的（1.3.3 测试里实测）。
// 排版在网页 codex-quota.js 里做，这里只交原始数据。规则见 docs/superpowers/specs/2026-10-12-codex-quota-design.md
enum CodexQuotaSource {
    static var cachedBinary: String?

    /// 找 codex 程序：测试用的覆盖 → ChatGPT 桌面版自带的 → 登录 shell 里的 → Homebrew 常见位置
    static func findBinary() -> String? {
        let fm = FileManager.default
        if let o = ProcessInfo.processInfo.environment["CROSSPET_CODEX_BIN"] { return fm.isExecutableFile(atPath: o) ? o : nil }
        if let c = cachedBinary, fm.isExecutableFile(atPath: c) { return c }
        cachedBinary = nil
        // 按顺序找，找到就停：大多数人有 ChatGPT 桌面版，就不用起登录 shell（.zprofile 里有 nvm、conda 的会慢）
        let bundled = "/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex"
        if fm.isExecutableFile(atPath: bundled) { cachedBinary = bundled; return bundled }
        if let p = loginShellWhich("codex"), fm.isExecutableFile(atPath: p) { cachedBinary = p; return p }
        cachedBinary = ["/opt/homebrew/bin/codex", "/usr/local/bin/codex"].first { fm.isExecutableFile(atPath: $0) }
        return cachedBinary
    }

    /// GUI 程序的 PATH 不全（npm / nvm 装的找不到），问一次登录 shell
    static func loginShellWhich(_ name: String) -> String? {
        let p = Process(); p.executableURL = URL(fileURLWithPath: "/bin/zsh"); p.arguments = ["-lc", "command -v \(name)"]
        let out = Pipe(); p.standardOutput = out; p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return nil }
        let deadline = Date().addingTimeInterval(5)
        while p.isRunning && Date() < deadline { usleep(50_000) }
        if p.isRunning { p.terminate(); return nil }
        // .zprofile 可能往输出里打别的东西：取最后一行
        let s = String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
            .split(separator: "\n").last.map { $0.trimmingCharacters(in: .whitespaces) } ?? ""
        return s.hasPrefix("/") ? s : nil
    }

    /// 问一次额度。done 在后台线程调用且只调一次：(rateLimits, error, 毫秒)
    static func queryLive(binary: String, timeout: TimeInterval = 15, done: @escaping ([String: Any]?, String?, Int) -> Void) {
        let started = Date()
        let p = Process(); p.executableURL = URL(fileURLWithPath: binary); p.arguments = ["app-server"]
        let inPipe = Pipe(), outPipe = Pipe()
        p.standardInput = inPipe; p.standardOutput = outPipe; p.standardError = FileHandle.nullDevice
        let lock = NSLock(); var finished = false; var buffer = Data()
        func finish(_ rl: [String: Any]?, _ err: String?) {
            lock.lock(); defer { lock.unlock() }
            guard !finished else { return }
            finished = true
            outPipe.fileHandleForReading.readabilityHandler = nil
            if p.isRunning { p.terminate() }
            done(rl, err, Int(Date().timeIntervalSince(started) * 1000))
        }
        outPipe.fileHandleForReading.readabilityHandler = { h in
            let chunk = h.availableData
            if chunk.isEmpty { finish(nil, "Codex 服务提前退出"); return }
            buffer.append(chunk)
            while let nl = buffer.firstIndex(of: 0x0A) {
                let line = buffer.subdata(in: buffer.startIndex..<nl); buffer.removeSubrange(buffer.startIndex...nl)
                guard let msg = try? JSONSerialization.jsonObject(with: line) as? [String: Any], (msg["id"] as? Int) == 2 else { continue }
                if let e = msg["error"] as? [String: Any] { finish(nil, e["message"] as? String ?? "Codex 服务返回错误"); return }
                let r = msg["result"] as? [String: Any]
                let rl = (r?["rateLimitsByLimitId"] as? [String: Any])?["codex"] as? [String: Any] ?? r?["rateLimits"] as? [String: Any]
                finish(rl, rl == nil ? "Codex 服务没给额度" : nil); return
            }
        }
        do { try p.run() } catch { finish(nil, "起不来 Codex：\(error.localizedDescription)"); return }
        let send = { (o: [String: Any]) in
            if let d = try? JSONSerialization.data(withJSONObject: o) { try? inPipe.fileHandleForWriting.write(contentsOf: d + Data([0x0A])) }
        }
        send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "crosspet", "version": "1"]]])
        send(["method": "initialized"])
        send(["id": 2, "method": "account/rateLimits/read"])
        DispatchQueue.global().asyncAfter(deadline: .now() + timeout) { finish(nil, "超时（\(Int(timeout)) 秒没回）") }
    }

    /// 运行状态里的简写：5小时已用 3%，本周已用 40%
    static func brief(_ rl: [String: Any]) -> String {
        ["primary", "secondary"].compactMap { k -> String? in
            guard let w = rl[k] as? [String: Any], let u = w["usedPercent"] as? NSNumber else { return nil }
            let mins = (w["windowDurationMins"] as? NSNumber)?.intValue ?? 0
            return "\(mins >= 10080 ? "本周" : mins >= 1440 ? "今日" : "\(max(1, mins / 60))小时")已用 \(Int(u.doubleValue.rounded()))%"
        }.joined(separator: "，")
    }

    /// 给网页的 payload（rateLimits 原样，不含账号标识——accountId 在 result 里，不在 rateLimits 里）
    static func payload(source: String, rateLimits: [String: Any]?, error: String?, ms: Int) -> [String: Any] {
        var p: [String: Any] = ["source": source, "ms": ms, "at": Date().timeIntervalSince1970]
        if let rl = rateLimits { p["rateLimits"] = rl }
        if let e = error { p["error"] = e }
        return p
    }
}
