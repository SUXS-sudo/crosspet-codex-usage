import Carbon
import Cocoa
import ServiceManagement
import WebKit

extension App {
    // MARK: GPT 额度（可选，默认关）：什么时候拿、从哪拿（来源见文件末尾 CodexQuotaSource）
    enum CodexWhy { case now, turnEnd, tick }

    /// 打开额度显示 / 页面就绪：马上问；Codex 回完一轮：隔 20 秒以上才问；定时（每分钟）：见 codexTickDue。
    /// 藏着、屏幕休眠时定时的不查（和 Windows 版 Pet.Quota.cs 一致）
    func refreshCodexQuota(_ why: CodexWhy) {
        guard ready, defaults.bool(forKey: "gptQuota"), !codexInFlight else { return }
        if why == .tick && (screensAsleep || userHidden) { return }
        let since = Date().timeIntervalSince(codexLiveAt)
        let askLive: Bool
        switch why {
        case .now: askLive = true
        case .turnEnd: askLive = since >= 20
        case .tick: askLive = Self.codexTickDue(since: since, busyAgo: codexBusyAt.map { Date().timeIntervalSince($0) })
        }
        if !askLive { return }
        codexLiveAt = Date()
        codexInFlight = true
        // 找程序可能要起一次登录 shell，放到后台，别卡住桌宠。程序被更新挪走的话 findBinary 下次会发现路径不在、重新找
        DispatchQueue.global(qos: .utility).async {
            let finish = { (rl: [String: Any]?, err: String?, ms: Int) in
                DispatchQueue.main.async {
                    self.codexInFlight = false
                    self.applyCodex(rateLimits: rl, error: err, ms: ms)
                }
            }
            guard let bin = CodexQuotaSource.findBinary() else { finish(nil, "没找到 codex 程序", 0); return }
            CodexQuotaSource.queryLive(binary: bin, done: finish)
        }
    }

    /// 定时到点时要不要问：Codex 在干活（10 分钟内有不是 Stop 的事件，长命令中途可能好几分钟没事件）1 分钟一次，
    /// 闲着 2 分钟一次（别处用了 Codex 也能较快跟上）。实测一次约 0.15 秒 CPU，每分钟一次平均约 0.25% 一个核心。
    /// 定时器每 60 秒一跳会有一两秒误差，门槛各留 5 秒（tests/codex_quota_probe.sh 测）
    static func codexTickDue(since: TimeInterval, busyAgo: TimeInterval?) -> Bool {
        let busy = busyAgo.map { $0 < 600 } ?? false
        return since >= (busy ? 55 : 115)
    }

    /// 交给网页：问到了就是 live；没问到、但以前问到过，就接着显示最后一次的数（stale，网页标上是几点的数据）；从没问到过就不显示
    func applyCodex(rateLimits: [String: Any]?, error: String?, ms: Int) {
        guard defaults.bool(forKey: "gptQuota") else { return }   // 查的途中关掉了额度显示
        if let rl = rateLimits { codexLast = (rl, Date()); codexNote = "" }
        else if codexLast == nil { codexNote = "问不到 Codex：\(error ?? "原因不明")" }
        let tag = rateLimits != nil ? "Codex 服务，\(String(format: "%.1f", Double(ms) / 1000)) 秒" : "没问到：\(error ?? "原因不明")"
        lastGptQuota = "\(Self.clock()) （\(tag)）" + (codexLast.map { (rateLimits == nil ? "沿用 \(Self.hm($0.at)) 的数：" : "") + CodexQuotaSource.brief($0.rateLimits) } ?? "")
        pushSettings()
        guard let last = codexLast else { js("setCodexQuota(null)"); return }
        var p = CodexQuotaSource.payload(source: rateLimits != nil ? "live" : "stale", rateLimits: last.rateLimits, error: error, ms: ms)
        p["at"] = last.at.timeIntervalSince1970
        guard let d = try? JSONSerialization.data(withJSONObject: p), let arg = String(data: d, encoding: .utf8) else { return }
        js("setCodexQuota(\(arg))")
    }

    // MARK: Gemini 额度（可选，默认关）
    // Antigravity 在本机跑着一个后台服务（language_server），它自己的界面就是向它要额度的。
    // 这里做同样的事：从它的启动参数里取本机接口的防伪令牌（每次启动随机生成、只在本机有效，不是账号凭据），
    // 只调「用户状态」接口、只取额度数字；令牌只在内存里用，不存盘、不外传。Antigravity 没开就不显示。

    func refreshAntigravityQuota() {
        guard ready, defaults.bool(forKey: "agyQuota") else { return }
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self else { return }
            guard let (pid, token) = self.antigravityServer() else {
                DispatchQueue.main.async { self.js("setQuota('gemini', null)") }
                return
            }
            let ports = self.agyPort?.pid == pid ? [self.agyPort!.port] : self.listeningPorts(pid)
            self.askAntigravity(pid: pid, token: token, ports: ports)
        }
    }

    /// 找到 Antigravity 的 language_server 进程，返回 (pid, 令牌)
    func antigravityServer() -> (Int32, String)? {
        guard let out = run("/bin/ps", ["-axo", "pid=,command="]) else { return nil }
        for line in out.split(separator: "\n") where line.contains("Antigravity.app/Contents/Resources/bin/language_server") {
            let parts = line.split(separator: " ").map(String.init)
            guard let pid = Int32(parts.first ?? "") else { continue }
            for (i, a) in parts.enumerated() {
                if a.hasPrefix("--csrf_token=") { return (pid, String(a.dropFirst("--csrf_token=".count))) }
                if a == "--csrf_token", i + 1 < parts.count { return (pid, parts[i + 1]) }
            }
        }
        return nil
    }

    func listeningPorts(_ pid: Int32) -> [Int] {
        guard let out = run("/usr/sbin/lsof", ["-nP", "-a", "-p", "\(pid)", "-iTCP", "-sTCP:LISTEN"]) else { return [] }
        return out.split(separator: "\n").dropFirst().compactMap { line in
            line.split(separator: " ").first { $0.contains("127.0.0.1:") }.flatMap { Int($0.split(separator: ":").last ?? "") }
        }
    }

    func run(_ path: String, _ args: [String]) -> String? {
        let p = Process(), pipe = Pipe()
        p.executableURL = URL(fileURLWithPath: path); p.arguments = args
        p.standardOutput = pipe; p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(data: data, encoding: .utf8)
    }

    /// 每个端口先问「额度汇总」（RetrieveUserQuotaSummary：和 Antigravity 的「View Usage」同一份数据，刚对话完就更新），
    /// 旧版 Antigravity 没有这个接口时退回「用户状态」（GetUserStatus：里面的额度是模型列表顺带的一份缓存，会慢几分钟）。
    /// 2026-10-14 实测：View Usage 已经 98%，用户状态里还是 100%。和 Windows 版 AntigravityQuota.Read 一致
    static let agyMethods = ["RetrieveUserQuotaSummary", "GetUserStatus"]
    func askAntigravity(pid: Int32, token: String, ports: [Int], method: Int = 0) {
        guard let port = ports.first else { return }
        var req = URLRequest(url: URL(string: "https://127.0.0.1:\(port)/exa.language_server_pb.LanguageServerService/\(Self.agyMethods[method])")!)
        req.httpMethod = "POST"
        req.timeoutInterval = 5
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue(token, forHTTPHeaderField: "X-Codeium-Csrf-Token")
        req.httpBody = #"{"metadata":{"ideName":"antigravity","extensionName":"antigravity","locale":"en"}}"#.data(using: .utf8)
        if agySession == nil { agySession = URLSession(configuration: .ephemeral, delegate: LocalhostTrust(), delegateQueue: nil) }
        agySession!.dataTask(with: req) { [weak self] data, resp, _ in
            guard let self else { return }
            guard (resp as? HTTPURLResponse)?.statusCode == 200, let data,
                  let obj = try? JSONSerialization.jsonObject(with: data), let q = Self.antigravityParse(obj) else {
                if method + 1 < Self.agyMethods.count {   // 这个端口换旧接口再问一次
                    self.askAntigravity(pid: pid, token: token, ports: ports, method: method + 1); return
                }
                self.agyPort = nil
                self.askAntigravity(pid: pid, token: token, ports: Array(ports.dropFirst()))  // 换下一个端口试
                return
            }
            self.agyPort = (pid, port)
            guard let json = try? JSONSerialization.data(withJSONObject: q), let arg = String(data: json, encoding: .utf8) else { return }
            DispatchQueue.main.async { self.js("setQuota('gemini', \(arg))") }
        }.resume()
    }

    /// 额度汇总或用户状态，哪种都认（汇总优先）
    static func antigravityParse(_ obj: Any) -> [String: Any]? { antigravitySummary(obj) ?? antigravityQuota(obj) }

    /// 额度汇总（RetrieveUserQuotaSummary）：Antigravity 自己分好组（「Gemini Models」「Claude and GPT models」），
    /// 每组一个或几个窗口（本周 / 今日……）。每个窗口一行；一组只有一个窗口时不写窗口名，和用户状态那条路显示得一样
    static func antigravitySummary(_ obj: Any) -> [String: Any]? {
        guard let groups = ((obj as? [String: Any])?["response"] as? [String: Any])?["groups"] as? [[String: Any]] else { return nil }
        let iso = ISO8601DateFormatter()
        var rows: [(name: String, left: Double, reset: Date?)] = []
        let sorted = groups.enumerated().sorted { a, b in
            let ga = (a.element["displayName"] as? String ?? "").contains("Gemini") ? 0 : 1, gb = (b.element["displayName"] as? String ?? "").contains("Gemini") ? 0 : 1
            return ga != gb ? ga < gb : a.offset < b.offset
        }.map { $0.element }
        for g in sorted {
            let title = g["displayName"] as? String ?? ""
            let name = title.contains("Gemini") ? "Gemini" : title.contains("Claude") || title.contains("GPT") ? "Claude/GPT" : title
            let buckets = g["buckets"] as? [[String: Any]] ?? []
            for b in buckets {
                // 比例为 0 时会被省略（protobuf 转 JSON 不写默认值）：窗口在、比例不在 = 用完了
                let left = (b["remainingFraction"] as? NSNumber)?.doubleValue ?? 0
                let window = ["weekly": "本周", "daily": "今日"][b["window"] as? String ?? ""] ?? (b["displayName"] as? String ?? "")
                rows.append((buckets.count > 1 ? "\(name) \(window)" : name, left, (b["resetTime"] as? String).flatMap { iso.date(from: $0) }))
            }
        }
        guard !rows.isEmpty else { return nil }
        let fmt = DateFormatter()
        let parts = rows.enumerated().map { i, r -> String in
            var piece = "\(r.name) 剩余 \(Int((r.left * 100).rounded()))%"
            if i == 0 || r.left <= 0.1, let d = r.reset {   // 重置时间标在第一行和快用完的行上
                fmt.dateFormat = d.timeIntervalSinceNow < 86400 ? "HH:mm" : "M/d"
                piece += " · \(fmt.string(from: d)) 重置"
            }
            return piece
        }
        return ["text": parts.joined(separator: " ｜ "), "low": (rows.map { $0.left }.min() ?? 1) <= 0.1]
    }

    /// 用户状态（GetUserStatus，旧版 Antigravity 的退路）：额度按「额度池」共享：Gemini 模型一个池，Claude 和 GPT 模型一个池（Antigravity 的「View Usage」也是这两组）。
    /// 固定显示这两行，Gemini 在前。
    /// 不写死「5 小时 / 每周」：24 小时内重置显示时刻，否则显示日期，免费版和会员都适用。
    static func antigravityQuota(_ obj: Any) -> [String: Any]? {
        guard let st = (obj as? [String: Any])?["userStatus"] as? [String: Any],
              let cfg = st["cascadeModelConfigData"] as? [String: Any],
              let models = cfg["clientModelConfigs"] as? [[String: Any]] else { return nil }
        // 固定两行，和 Antigravity 自己「View Usage」的分组一样：Gemini 模型一行，Claude 和 GPT 模型一行。
        // 数字相同也不合并（以前按「剩余 + 重置时间」合并，行数会变来变去，和 Antigravity 对不上）。和 Windows 版 AntigravityQuota.Parse 一致
        var groups: [(name: String, left: Double, reset: Date?)] = []
        let iso = ISO8601DateFormatter()
        for m in models {
            guard let qi = m["quotaInfo"] as? [String: Any] else { continue }
            // 不可用的模型不算：比如只给 Ultra 套餐的「Gemini 4 Argon」，别的账号里标着 disabled、额度信息是空的，
            // 以前被当成「剩 0%」排在第一行（2026-10-14 两位用户反馈）
            if m["disabled"] as? Bool == true || (qi["remainingFraction"] == nil && qi["resetTime"] == nil) { continue }
            let left = (qi["remainingFraction"] as? NSNumber)?.doubleValue ?? 0  // 用完时这个字段会被省略（但还有重置时间）
            let reset = (qi["resetTime"] as? String).flatMap { iso.date(from: $0) }
            let name = (m["label"] as? String ?? "").hasPrefix("Gemini") ? "Gemini" : "Claude/GPT"
            if let i = groups.firstIndex(where: { $0.name == name }) {
                if left < groups[i].left { groups[i].left = left; groups[i].reset = reset ?? groups[i].reset }
            } else {
                groups.append((name, left, reset))
            }
        }
        guard !groups.isEmpty else { return nil }
        groups.sort { ($0.name == "Gemini" ? 0 : 1) < ($1.name == "Gemini" ? 0 : 1) }
        let minLeft = groups.map { $0.left }.min() ?? 1
        let fmt = DateFormatter()
        let parts = groups.enumerated().map { i, g -> String in
            var piece = "\(g.name) 剩余 \(Int((g.left * 100).rounded()))%"
            // 重置时间标在第一行（Gemini）上；别的那行快用完了也标
            if i == 0 || g.left <= 0.1, let d = g.reset {
                fmt.dateFormat = d.timeIntervalSinceNow < 86400 ? "HH:mm" : "M/d"
                piece += " · \(fmt.string(from: d)) 重置"
            }
            return piece
        }
        return ["text": parts.joined(separator: " ｜ "), "low": minLeft <= 0.1]
    }

}
