# GPT（Codex）额度改成直接问 Codex — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GPT 额度的主来源改成调 `codex app-server` 的 `account/rateLimits/read` 拿实时额度，本机会话记录（含 Windows 上的 WSL）只当备用；排版统一放进网页。

**Architecture:** 外壳（Swift / C#）只做两件事：拿原始额度（问 Codex 或读记录）、按规则安排什么时候拿；拿到后调网页 `setCodexQuota(payload)`。网页新文件 `codex-quota.js` 把两种原始格式（服务返回的 camelCase、记录里的 snake_case）统一成现在 `setQuota` 收的 `{text, low, windows}`。用一个假的 `codex` 程序（Python 脚本）测外壳的调用、超时、出错路径。

**Tech Stack:** Swift（AppKit，单文件 `app/CrossPet.swift`）、C# .NET Framework 4.8（`app/windows/src`）、浏览器 JS（`app/web`）、node 24 自带 `node:test`、Python 3 `unittest`。

**Spec:** `docs/superpowers/specs/2026-10-12-codex-quota-design.md`

## Global Constraints

- 主来源：`codex app-server`（stdio），`initialize`（clientInfo name `crosspet`）→ `initialized` → `account/rateLimits/read`；整个过程 15 秒超时，超时杀进程
- 取 `result.rateLimitsByLimitId.codex`，没有就取 `result.rateLimits`
- 查询时机：打开额度显示立刻查；Codex 回完一轮 1.5 秒后查、两次之间至少 20 秒；平时每 5 分钟；藏着（快捷键）、锁屏、屏幕休眠时定时查询跳过
- 主来源失败：退回会话记录，每分钟读一次记录；每 5 分钟再试一次主来源
- 显示格式不变：`5小时 剩余 97% ｜ 本周 剩余 99% · 21:40 重置`；**不显示重置卡张数**；来自记录时末尾加 ` · 本机记录`
- 记录：按 `CODEX_HOME`（没设就是 `~/.codex`）找；最近 4 天的会话里按 max(修改时间, 创建时间) 取前 12 个文件，每个读末尾 400 KB，按每行的 `timestamp` 取最新一条 `rate_limits`
- WSL（仅 Windows）：WSL 虚拟机进程（`vmmemWSL` / `vmmem`）不在就跳过；在的话 `wsl.exe -l --running -q` 拿运行中的发行版，只访问 `\\wsl.localhost\<发行版>\home\*\.codex\sessions`；发行版和目录缓存 10 分钟；出错当没有
- 不写进日志 / 反馈：`accountId`、重置卡 id
- 开发用的覆盖：环境变量 `CROSSPET_CODEX_BIN` 指定 `codex` 程序路径（测试用假程序）
- 只 `git add` 自己改的文件；不推送（攒批发布，推送前查 PR / issue）

## Review Focus

1. **Codex 桌面版更新后程序路径变了**：缓存的路径不存在 → 当次退回记录，并清掉缓存、下次重新找（Task 3 / Task 5 测「缓存路径不存在」）
2. **查询卡住（断网、服务不回）**：15 秒后杀进程、退回记录；进行中不发起第二次查询，不会堆积进程（Task 2 用假程序 `hang` 模式测）
3. **WSL 用户：Windows 这边没登录 Codex**：主来源每 5 分钟才重试一次（不是每分钟起进程），中间每分钟读记录，显示带「本机记录」（Task 3 测调度表）
4. **查询途中关掉了额度显示**：结果回来时不再显示（Task 3 / Task 5 的 `apply` 先检查开关）
5. **服务返回只有一个窗口 / 窗口为 null / 百分比是小数**：网页照样排版，不报错（Task 1 测）

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `app/web/codex-quota.js`（新） | 纯函数：原始额度 → `{text, low, windows, source}`；浏览器里挂到 `window.CodexQuota`，node 里 `module.exports` |
| `app/web/index.html` | 引入 `codex-quota.js`；新增 `setCodexQuota(payload)` |
| `tests/codex_quota_view.test.js`（新） | node 测 `codex-quota.js` |
| `tests/fake_codex.py`（新） | 假 `codex`：按 `FAKE_CODEX_MODE` 模拟 app-server 的各种返回 |
| `tests/codex_quota_probe.sh`（新） | 用假程序跑 Mac 版的 `--codex-quota-probe`，逐个模式检查输出 |
| `app/CrossPet.swift` | 新 `CodexQuotaSource`（找程序、问服务、读记录、组 payload）；调度改成 `refreshCodexQuota(_:)`；删掉旧的 `codexQuota()` / `quotaIn()` 排版 |
| `app/windows/src/CodexQuota.cs` | 改写：找程序（含运行中的进程）、问服务、读记录（含 WSL）、组 payload |
| `app/windows/src/Pet.cs` | 调度改成 `RefreshCodexQuota(why)`；记锁屏状态；运行状态那一行 |
| `tools/integrate.py`、`tests/integrate_test.py` | 诊断列出找到的 `codex` 程序和版本 |
| `README.md`、`docs/常见问题.md`、`CHANGELOG.md` | 隐私说明、常见问题、未发布条目 |

---

### Task 1: 网页统一排版（`codex-quota.js`）

**Files:**
- Create: `app/web/codex-quota.js`
- Create: `tests/codex_quota_view.test.js`
- Modify: `app/web/index.html`（第 144 行 `<script src="effects.js">` 后面；`setQuota` 函数前面）

**Interfaces:**
- Produces: `CodexQuota.view(payload, nowSec) -> {text, low, windows:[{label, used, reset}], source} | null`
- Produces: 网页全局函数 `setCodexQuota(payload)`，payload 形如 `{source: "live"|"records", rateLimits: {...}}`；`payload` 为 `null` 时清掉显示

- [ ] **Step 1: 写失败的测试**

`tests/codex_quota_view.test.js`：

```js
const test = require('node:test')
const assert = require('node:assert')
const { view } = require('../app/web/codex-quota.js')

const NOW = Date.UTC(2026, 9, 12, 4, 0, 0) / 1000   // 2026-10-12 12:00 北京时间
process.env.TZ = 'Asia/Shanghai'

test('服务返回（camelCase）两个窗口', () => {
  const v = view({ source: 'live', rateLimits: {
    primary: { usedPercent: 3, windowDurationMins: 300, resetsAt: NOW + 3600 },
    secondary: { usedPercent: 40, windowDurationMins: 10080, resetsAt: NOW + 3 * 86400 } } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 97% ｜ 本周 剩余 60% · 10/15 重置')
  assert.strictEqual(v.low, false)
  assert.deepStrictEqual(v.windows, [{ label: '5小时', used: 3, reset: NOW + 3600 }, { label: '本周', used: 40, reset: NOW + 3 * 86400 }])
  assert.strictEqual(v.source, 'live')
})

test('记录（snake_case）带「本机记录」', () => {
  const v = view({ source: 'records', rateLimits: {
    primary: { used_percent: 91, window_minutes: 300, resets_at: NOW + 1800 }, secondary: null } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 9% · 12:30 重置 · 本机记录')
  assert.strictEqual(v.low, true)
})

test('过了重置时间的窗口显示 100%，不参与 low', () => {
  const v = view({ source: 'records', rateLimits: {
    primary: { used_percent: 95, window_minutes: 300, resets_at: NOW - 10 },
    secondary: { used_percent: 20, window_minutes: 10080, resets_at: NOW + 86400 * 2 } } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 100% ｜ 本周 剩余 80% · 10/14 重置 · 本机记录')
  assert.strictEqual(v.low, false)
})

test('小数百分比四舍五入；只有一个窗口', () => {
  const v = view({ source: 'live', rateLimits: { primary: { usedPercent: 12.6, windowDurationMins: 10080, resetsAt: NOW + 86400 * 5 } } }, NOW)
  assert.strictEqual(v.text, '本周 剩余 87% · 10/17 重置')
})

test('没有可用窗口 / 空 payload 返回 null', () => {
  assert.strictEqual(view(null, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: null, secondary: null } }, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: { windowDurationMins: 300 } } }, NOW), null)
})

test('「今日」窗口标签', () => {
  const v = view({ source: 'live', rateLimits: { primary: { usedPercent: 0, windowDurationMins: 1440, resetsAt: 0 } } }, NOW)
  assert.strictEqual(v.text, '今日 剩余 100%')
})
```

- [ ] **Step 2: 跑测试，确认失败**

Run: `node --test tests/codex_quota_view.test.js`
Expected: FAIL，`Cannot find module '../app/web/codex-quota.js'`

- [ ] **Step 3: 写实现**

`app/web/codex-quota.js`：

```js
// GPT（Codex）额度排版：外壳交来原始额度（问 Codex 服务得到的 camelCase，或会话记录里的 snake_case），
// 这里统一成名牌下面那一行。两个平台共用这一份，规则只写一遍
;(function (root) {
  const pad = n => String(n).padStart(2, '0')
  const label = mins => mins >= 7 * 24 * 60 ? '本周' : mins >= 24 * 60 ? '今日' : `${Math.max(1, Math.floor(mins / 60))}小时`
  function windowsOf(rl) {
    const out = []
    for (const key of ['primary', 'secondary']) {
      const w = rl && rl[key]
      if (!w) continue
      const used = w.usedPercent ?? w.used_percent
      if (typeof used !== 'number') continue
      const mins = w.windowDurationMins ?? w.window_minutes ?? 0
      const reset = w.resetsAt ?? w.resets_at
      out.push({ label: label(mins), used, reset: typeof reset === 'number' ? reset : 0 })
    }
    return out
  }
  function when(sec, nowSec) {
    const d = new Date(sec * 1000)
    return sec - nowSec < 86400 ? `${pad(d.getHours())}:${pad(d.getMinutes())}` : `${d.getMonth() + 1}/${d.getDate()}`
  }
  function view(p, nowSec) {
    if (!p || !p.rateLimits) return null
    const windows = windowsOf(p.rateLimits)
    if (!windows.length) return null
    // 过了重置时间、又没有新数据的窗口现在是满的，不算「快用完」
    const live = windows.filter(w => !w.reset || w.reset > nowSec)
    const maxUsed = live.length ? Math.max(...live.map(w => w.used)) : 0
    let marked = false
    const parts = windows.map(w => {
      if (w.reset > 0 && w.reset <= nowSec) return `${w.label} 剩余 100%`
      let s = `${w.label} 剩余 ${Math.max(0, 100 - Math.round(w.used))}%`
      if (!marked && w.used === maxUsed && w.reset > 0) { marked = true; s += ` · ${when(w.reset, nowSec)} 重置` }
      return s
    })
    let text = parts.join(' ｜ ')
    if (p.source === 'records') text += ' · 本机记录'   // 没问到 Codex，用的是本机会话记录，可能不是最新
    return { text, low: maxUsed >= 90, windows, source: p.source || 'live' }
  }
  root.CodexQuota = { view }
  if (typeof module !== 'undefined') module.exports = { view }
})(typeof window !== 'undefined' ? window : globalThis)
```

`app/web/index.html`：第 144 行后加一行

```html
<script src="codex-quota.js"></script>
```

在 `function setQuota(id, q) {` 前面加：

```js
// GPT 额度：外壳交来原始额度（见 codex-quota.js），排好版再走 setQuota
function setCodexQuota(p) { setQuota('gpt', p ? CodexQuota.view(p, Date.now() / 1000) : null) }
```

- [ ] **Step 4: 跑测试，确认通过**

Run: `node --test tests/codex_quota_view.test.js`
Expected: 6 个测试全部 PASS

- [ ] **Step 5: 在浏览器里冒烟**

用 `crosspet-web-8777` 预览打开 `app/web/index.html`，按 1.3.2 时的方法 `init` 一个 gpt 角色后执行：

```js
setCodexQuota({ source: 'records', rateLimits: { primary: { used_percent: 10, window_minutes: 300, resets_at: Date.now()/1000 + 3600 } } }); document.getElementById('quota').textContent
```

Expected: 返回 `5小时 剩余 90% · HH:MM 重置 · 本机记录`，控制台无报错

- [ ] **Step 6: Commit**

```bash
git add app/web/codex-quota.js app/web/index.html tests/codex_quota_view.test.js
git commit -m "GPT 额度排版挪进网页（codex-quota.js），两个平台共用；新增 setCodexQuota"
```

---

### Task 2: 假 codex 程序 + Mac 版问服务（含探针命令）

**Files:**
- Create: `tests/fake_codex.py`
- Create: `tests/codex_quota_probe.sh`
- Modify: `app/CrossPet.swift`（新增 `enum CodexQuotaSource`，放在 `// MARK: Gemini 额度` 前；App 启动入口处加探针分支）

**Interfaces:**
- Consumes: 无
- Produces（Swift）:
  - `CodexQuotaSource.findBinary() -> String?`
  - `CodexQuotaSource.queryLive(binary: String, timeout: TimeInterval = 15, done: @escaping (_ rateLimits: [String: Any]?, _ error: String?, _ ms: Int) -> Void)`（`done` 在后台线程调用，只调一次）
  - 命令行 `CrossPet --codex-quota-probe`：打印一行 JSON `{"source":"live"|"records"|"none","ms":…,"error":…,"rateLimits":…}` 后退出（Task 3 完成后才会有 records）

- [ ] **Step 1: 写假 codex**

`tests/fake_codex.py`：

```python
#!/usr/bin/env python3
"""假的 codex 命令：只模拟 `--version` 和 `app-server` 的 account/rateLimits/read，给 CrossPet 的测试用。
FAKE_CODEX_MODE：ok（两个窗口）/ single（只有一个窗口）/ auth（没登录）/ nomethod（旧版没有这个接口）/ hang（一直不回）/ garbage（乱回）
"""
import json, os, sys, time

mode = os.environ.get("FAKE_CODEX_MODE", "ok")
if sys.argv[1:] == ["--version"]:
    print("codex-cli 0.0.0-fake"); sys.exit(0)
if sys.argv[1:2] != ["app-server"]:
    sys.exit(2)
now = int(time.time())
LIMITS = {
    "ok": {"limitId": "codex", "primary": {"usedPercent": 3, "windowDurationMins": 300, "resetsAt": now + 3600},
           "secondary": {"usedPercent": 40, "windowDurationMins": 10080, "resetsAt": now + 3 * 86400}, "planType": "plus"},
    "single": {"limitId": "codex", "primary": {"usedPercent": 12, "windowDurationMins": 10080, "resetsAt": now + 5 * 86400}, "secondary": None},
}
for line in sys.stdin:
    msg = json.loads(line)
    if msg.get("method") == "initialize":
        print(json.dumps({"id": msg["id"], "result": {"userAgent": "fake"}}), flush=True)
    elif msg.get("method") == "account/rateLimits/read":
        if mode == "hang":
            time.sleep(3600)
        elif mode == "garbage":
            print("not json", flush=True)
        elif mode == "auth":
            print(json.dumps({"id": msg["id"], "error": {"code": -32600, "message": "codex account authentication required to read rate limits"}}), flush=True)
        elif mode == "nomethod":
            print(json.dumps({"id": msg["id"], "error": {"code": -32601, "message": "method not found"}}), flush=True)
        else:
            rl = LIMITS[mode]
            print(json.dumps({"id": msg["id"], "result": {"rateLimits": rl, "rateLimitsByLimitId": {"codex": rl},
                              "rateLimitResetCredits": {"availableCount": 1}, "accountId": "acct-secret"}}), flush=True)
```

`chmod +x tests/fake_codex.py`

- [ ] **Step 2: 写探针测试脚本（先失败）**

`tests/codex_quota_probe.sh`：

```sh
#!/bin/sh
# 用假 codex 测 Mac 版问服务的各条路径：先 app/build.sh 编译，再跑本脚本
set -eu
cd "$(dirname "$0")/.."
BIN=build/CrossPet.app/Contents/MacOS/CrossPet
FAKE="$(pwd)/tests/fake_codex.py"
EMPTY=$(mktemp -d)   # 空的 CODEX_HOME：没有会话记录，失败时应该是 none
fail=0
check() {   # 模式 期望的 source 期望 error 里包含的字（可空）
  out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE="$1" "$BIN" --codex-quota-probe)
  python3 - "$1" "$2" "$3" "$out" <<'EOF' || fail=1
import json, sys
mode, src, err, out = sys.argv[1:5]
p = json.loads(out)
ok = p["source"] == src and (err in (p.get("error") or "")) and "acct-secret" not in out
print(("PASS" if ok else "FAIL"), mode, out[:160])
sys.exit(0 if ok else 1)
EOF
}
check ok live ""
check single live ""
check auth none "authentication"
check nomethod none "method not found"
check garbage none "超时"
start=$(date +%s); check hang none "超时"; [ $(( $(date +%s) - start )) -le 8 ] || { echo "FAIL hang 超过 8 秒（超时没生效）"; fail=1; }
out=$(CODEX_HOME="$EMPTY" CROSSPET_CODEX_BIN=/nonexistent/codex "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"none"' && echo "PASS missing-binary" || { echo "FAIL missing-binary $out"; fail=1; }
rm -rf "$EMPTY"
exit $fail
```

`chmod +x tests/codex_quota_probe.sh`

（这一步只写脚本，先不跑：还没有 `--codex-quota-probe` 时程序会正常启动成桌宠、不会退出。Step 4 实现后再跑，作为本任务的验收测试。）

- [ ] **Step 3: 实现 `CodexQuotaSource`（问服务部分）**

`app/CrossPet.swift`，在 `// MARK: Gemini 额度` 之前加：

```swift
// MARK: GPT（Codex）额度来源
// 主来源：起 `codex app-server`，问 account/rateLimits/read——Codex 用它自己的登录状态向 OpenAI 查实时额度
// （等同于在 Codex 里看使用情况，不对话、不耗额度；CrossPet 不碰凭据）。备用：本机会话记录（见 records）。
// 排版在网页 codex-quota.js 里做，这里只交原始数据
enum CodexQuotaSource {
    static var cachedBinary: String?

    /// 找 codex 程序：测试用的覆盖 → ChatGPT 桌面版自带的 → 登录 shell 里的 → Homebrew 常见位置
    static func findBinary() -> String? {
        let fm = FileManager.default
        if let o = ProcessInfo.processInfo.environment["CROSSPET_CODEX_BIN"] { return fm.isExecutableFile(atPath: o) ? o : nil }
        if let c = cachedBinary, fm.isExecutableFile(atPath: c) { return c }
        cachedBinary = nil
        var list = ["/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex"]
        if let p = loginShellWhich("codex") { list.append(p) }
        list += ["/opt/homebrew/bin/codex", "/usr/local/bin/codex"]
        cachedBinary = list.first { fm.isExecutableFile(atPath: $0) }
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
        let s = String(decoding: out.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
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

    /// 给网页的 payload（rateLimits 原样，不含账号标识——accountId 在 result 里，不在 rateLimits 里）
    static func payload(source: String, rateLimits: [String: Any]?, error: String?, ms: Int) -> [String: Any] {
        var p: [String: Any] = ["source": source, "ms": ms, "at": Date().timeIntervalSince1970]
        if let rl = rateLimits { p["rateLimits"] = rl }
        if let e = error { p["error"] = e }
        return p
    }
}
```

在 App 启动入口（`NSApplication.shared` / `app.run()` 之前的顶层代码）加探针分支：

```swift
// 测试用：只问一次 GPT 额度，打印 JSON 就退出（tests/codex_quota_probe.sh）
if CommandLine.arguments.contains("--codex-quota-probe") {
    let sem = DispatchSemaphore(value: 0); var out: [String: Any] = [:]
    if let bin = CodexQuotaSource.findBinary() {
        // CROSSPET_CODEX_TIMEOUT：测试时把 15 秒超时缩短
        let timeout = Double(ProcessInfo.processInfo.environment["CROSSPET_CODEX_TIMEOUT"] ?? "") ?? 15
        CodexQuotaSource.queryLive(binary: bin, timeout: timeout) { rl, err, ms in
            out = CodexQuotaSource.payload(source: rl != nil ? "live" : "none", rateLimits: rl, error: err, ms: ms); sem.signal()
        }
        sem.wait()
    } else { out = CodexQuotaSource.payload(source: "none", rateLimits: nil, error: "没找到 codex 程序", ms: 0) }
    let d = try! JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
    print(String(decoding: d, as: UTF8.self)); exit(0)
}
```

（`garbage` 模式下没有 id 2 的合法行，会走超时，所以期望 error 含「超时」。）

- [ ] **Step 4: 编译、跑测试**

Run: `app/build.sh >/dev/null && CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh`
Expected: 7 行 PASS（ok、single、auth、nomethod、garbage、hang、missing-binary），退出码 0，输出里没有 `acct-secret`

- [ ] **Step 5: 真 Codex 冒烟**

Run: `build/CrossPet.app/Contents/MacOS/CrossPet --codex-quota-probe`
Expected: `"source":"live"`，`rateLimits` 里有 `primary` / `secondary`，耗时几秒

- [ ] **Step 6: Commit**

```bash
git add tests/fake_codex.py tests/codex_quota_probe.sh app/CrossPet.swift
git commit -m "Mac：问 Codex 本地服务读实时额度（CodexQuotaSource.queryLive），假 codex 和探针测试"
```

---

### Task 3: Mac 版备用来源 + 调度 + 接到网页

**Files:**
- Modify: `app/CrossPet.swift`：
  - `CodexQuotaSource` 里加 `records()`
  - 删掉旧的 `codexQuota()`、`quotaIn(_:)`（约 598–655 行）和 `refreshGPTQuota()`
  - 新增 `refreshCodexQuota(_:)`；改调用处：162 行定时器、392 行页面就绪、567 行 Stop 后、998 行开关
  - 1783 行运行状态那一行
- Modify: `tests/codex_quota_probe.sh`（加 records 用例）

**Interfaces:**
- Consumes: `CodexQuotaSource.findBinary()`、`queryLive(...)`、`payload(...)`（Task 2），网页 `setCodexQuota(payload)`（Task 1）
- Produces:
  - `CodexQuotaSource.records(home: URL) -> (rateLimits: [String: Any], ts: String)?`
  - `enum CodexWhy { case now, turnEnd, tick }`；`func refreshCodexQuota(_ why: CodexWhy)`

- [ ] **Step 1: 先加 records 的测试用例（失败）**

`tests/codex_quota_probe.sh` 在 `rm -rf "$EMPTY"` 前加：

```sh
# 备用来源：服务失败时读会话记录，挑时间戳最新的一条（不是文件最新的那个）
REC=$(mktemp -d); D="$REC/sessions/$(date +%Y/%m/%d)"; mkdir -p "$D"
now=$(date +%s)
printf '{"timestamp":"2026-01-01T00:00:09Z","payload":{"rate_limits":{"primary":{"used_percent":70,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-a.jsonl"
printf '{"timestamp":"2026-01-01T00:00:01Z","payload":{"rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_at":%s}}}}\n' $((now+600)) > "$D/rollout-b.jsonl"
touch "$D/rollout-a.jsonl"; sleep 1; touch "$D/rollout-b.jsonl"   # b 文件更新，但记录更旧
out=$(CODEX_HOME="$REC" CROSSPET_CODEX_BIN="$FAKE" FAKE_CODEX_MODE=auth CROSSPET_CODEX_TIMEOUT=3 "$BIN" --codex-quota-probe)
echo "$out" | grep -q '"source":"records"' && echo "$out" | grep -q '"used_percent":70' && echo "PASS records-newest-by-timestamp" || { echo "FAIL records $out"; fail=1; }
rm -rf "$REC"
```

Run: `app/build.sh >/dev/null && CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh`
Expected: 最后一行 FAIL（探针还没退回记录）

- [ ] **Step 2: 实现 `records(home:)`，探针失败时退回记录**

`CodexQuotaSource` 里加：

```swift
    static var home: URL {
        if let h = ProcessInfo.processInfo.environment["CODEX_HOME"], !h.isEmpty { return URL(fileURLWithPath: h) }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".codex")
    }

    /// 备用：最近 4 天的会话记录里，按每行自带的 timestamp 取最新的一条 rate_limits
    static func records(home: URL = home) -> (rateLimits: [String: Any], ts: String)? {
        let fm = FileManager.default, cal = Calendar.current
        var files: [(URL, Date)] = []
        for back in 0..<4 {
            guard let day = cal.date(byAdding: .day, value: -back, to: Date()) else { continue }
            let c = cal.dateComponents([.year, .month, .day], from: day)
            let dir = home.appendingPathComponent(String(format: "sessions/%04d/%02d/%02d", c.year!, c.month!, c.day!))
            for f in (try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey, .creationDateKey])) ?? [] where f.pathExtension == "jsonl" {
                let v = try? f.resourceValues(forKeys: [.contentModificationDateKey, .creationDateKey])
                files.append((f, max(v?.contentModificationDate ?? .distantPast, v?.creationDate ?? .distantPast)))
            }
        }
        var best: (rateLimits: [String: Any], ts: String)?
        for (file, _) in files.sorted(by: { $0.1 > $1.1 }).prefix(12) {
            guard let h = try? FileHandle(forReadingFrom: file) else { continue }
            defer { try? h.close() }
            let size = (try? h.seekToEnd()) ?? 0
            try? h.seek(toOffset: size > 400_000 ? size - 400_000 : 0)
            guard let data = try? h.readToEnd() else { continue }
            for line in String(decoding: data, as: UTF8.self).split(separator: "\n").reversed() where line.contains("\"rate_limits\"") {
                guard let obj = try? JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any],
                      let rl = findRateLimits(obj), rl["primary"] is [String: Any] || rl["secondary"] is [String: Any] else { continue }
                let ts = obj["timestamp"] as? String ?? ""
                if best == nil || ts > best!.ts { best = (rl, ts) }
                break   // 这个文件里最后一条就是它最新的
            }
        }
        return best
    }

    static func findRateLimits(_ o: Any) -> [String: Any]? {
        if let d = o as? [String: Any] {
            if let rl = d["rate_limits"] as? [String: Any] { return rl }
            for v in d.values { if let r = findRateLimits(v) { return r } }
        } else if let a = o as? [Any] { for v in a { if let r = findRateLimits(v) { return r } } }
        return nil
    }
```

（原 `AppDelegate.findRateLimits` 只有旧的 `quotaIn` 在用，随旧代码一起删掉。）

探针分支里服务失败时退回：把 `out = CodexQuotaSource.payload(source: rl != nil ? "live" : "none", …)` 改成

```swift
if let rl = rl { out = CodexQuotaSource.payload(source: "live", rateLimits: rl, error: nil, ms: ms) }
else if let r = CodexQuotaSource.records() { out = CodexQuotaSource.payload(source: "records", rateLimits: r.rateLimits, error: err, ms: ms) }
else { out = CodexQuotaSource.payload(source: "none", rateLimits: nil, error: err, ms: ms) }
```

找不到程序的分支同样先试 `records()`。

Run: `app/build.sh >/dev/null && CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh`
Expected: 全部 PASS（8 行）

- [ ] **Step 3: 调度 `refreshCodexQuota`，替换旧的 `refreshGPTQuota`**

`AppDelegate` 里（删掉旧 `refreshGPTQuota` / `codexQuota` / `quotaIn` / `findRateLimits` 后，在原位置）加：

```swift
    enum CodexWhy { case now, turnEnd, tick }
    var codexLiveAt = Date.distantPast       // 上次问服务的时间（不管成没成）
    var codexLiveOK = false                  // 上次问服务成功了没
    var codexInFlight = false
    var lastGptQuota = "还没读过"

    /// GPT 额度什么时候拿、从哪拿（规则见 docs/superpowers/specs/2026-10-12-codex-quota-design.md）
    func refreshCodexQuota(_ why: CodexWhy) {
        guard ready, defaults.bool(forKey: "gptQuota"), !codexInFlight else { return }
        let hidden = screensAsleep || userHidden
        if why == .tick && hidden { return }
        let since = Date().timeIntervalSince(codexLiveAt)
        let askLive: Bool
        switch why {
        case .now: askLive = true
        case .turnEnd: askLive = since >= 20
        case .tick: askLive = since >= 300
        }
        if !askLive {
            if !codexLiveOK { applyCodex(source: "records", rateLimits: nil, error: nil, ms: 0) }   // 服务不通：每分钟读记录
            return
        }
        guard let bin = CodexQuotaSource.findBinary() else {
            codexLiveAt = Date(); codexLiveOK = false
            applyCodex(source: "records", rateLimits: nil, error: "没找到 codex 程序", ms: 0); return
        }
        codexInFlight = true; codexLiveAt = Date()
        CodexQuotaSource.queryLive(binary: bin) { rl, err, ms in
            DispatchQueue.main.async {
                self.codexInFlight = false
                self.codexLiveOK = rl != nil
                if rl == nil { CodexQuotaSource.cachedBinary = nil }   // 程序可能被更新挪走了，下次重新找
                self.applyCodex(source: rl != nil ? "live" : "records", rateLimits: rl, error: err, ms: ms)
            }
        }
    }

    /// 交给网页；records 来源时这里现读记录（rateLimits 传 nil）
    func applyCodex(source: String, rateLimits: [String: Any]?, error: String?, ms: Int) {
        guard defaults.bool(forKey: "gptQuota") else { return }   // 查的途中关掉了额度显示
        var src = source, rl = rateLimits
        if src == "records" { rl = CodexQuotaSource.records()?.rateLimits; if rl == nil { src = "none" } }
        let tag = src == "live" ? "Codex 服务，\(String(format: "%.1f", Double(ms) / 1000)) 秒" : src == "records" ? "本机记录" : "没读到"
        lastGptQuota = "\(Self.clock()) （\(tag)\(error.map { "；服务：\($0)" } ?? "")）" + (rl.map { CodexQuotaSource.brief($0) } ?? "")
        guard src != "none", let p = try? JSONSerialization.data(withJSONObject: CodexQuotaSource.payload(source: src, rateLimits: rl, error: error, ms: ms)),
              let arg = String(data: p, encoding: .utf8) else { return }
        js("setCodexQuota(\(arg))")
    }
```

`CodexQuotaSource` 里加运行状态用的简写（只有数字，不排版）：

```swift
    /// 运行状态里的简写：5小时已用 3%，本周已用 40%
    static func brief(_ rl: [String: Any]) -> String {
        ["primary", "secondary"].compactMap { k -> String? in
            guard let w = rl[k] as? [String: Any] else { return nil }
            let used = (w["usedPercent"] ?? w["used_percent"]) as? NSNumber
            let mins = ((w["windowDurationMins"] ?? w["window_minutes"]) as? NSNumber)?.intValue ?? 0
            guard let u = used else { return nil }
            return "\(mins >= 10080 ? "本周" : mins >= 1440 ? "今日" : "\(max(1, mins / 60))小时")已用 \(u.intValue)%"
        }.joined(separator: "，")
    }
```

改调用处：
- 第 162 行定时器：`self?.refreshGPTQuota()` → `self?.refreshCodexQuota(.tick)`
- 第 392 行（页面就绪）：`refreshGPTQuota()` → `refreshCodexQuota(.now)`
- 第 567 行（gpt Stop 后 1.5 秒）：`self?.refreshGPTQuota()` → `self?.refreshCodexQuota(.turnEnd)`
- 第 998 行（开关）：`if on { refreshGPTQuota() } else { js("setQuota('gpt', null)") }` → `if on { refreshCodexQuota(.now) } else { js("setCodexQuota(null)") }`
- `updatePageHidden()` 里：从藏着变回显示时补一次 `refreshCodexQuota(.tick)`（`tick` 自带 5 分钟间隔，不会因此多问）
- 1783 行运行状态保持 `L.append("GPT 额度最近一次读到 \(lastGptQuota)")`

- [ ] **Step 4: 编译 + 测试 + 真机验证**

Run: `app/build.sh >/dev/null && CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh && node --test tests/codex_quota_view.test.js`
Expected: 全部 PASS

装到本机（`pkill -x CrossPet; rm -rf ~/Applications/CrossPet.app && ditto build/CrossPet.app ~/Applications/CrossPet.app && open ~/Applications/CrossPet.app`），切到 GPT 角色：
- 名牌下显示额度，**没有**「本机记录」
- 开发者控制台 / 反馈预览的运行状态里：`GPT 额度最近一次读到 HH:mm （Codex 服务，2.x 秒）5小时已用 …`
- 设置里关掉再打开：立刻刷新
- 退回路径：先 `pkill -x CrossPet`，再在终端 `CROSSPET_CODEX_BIN=/nonexistent ~/Applications/CrossPet.app/Contents/MacOS/CrossPet &` 起一份：名牌下显示带「 · 本机记录」，运行状态写「（本机记录；服务：没找到 codex 程序）」；看完 `pkill -x CrossPet && open ~/Applications/CrossPet.app` 恢复

- [ ] **Step 5: Commit**

```bash
git add app/CrossPet.swift tests/codex_quota_probe.sh
git commit -m "Mac：GPT 额度改成先问 Codex 服务、失败退回会话记录（按 CODEX_HOME、按记录时间挑最新），调度按 5 分钟 / 回完一轮 / 打开时；运行状态写来源和耗时"
```

---

### Task 4: 诊断列出 codex 程序

**Files:**
- Modify: `tools/integrate.py`（`wsl_codex_homes` 后面加 `codex_programs()`；`diagnose()` 的 Codex 段）
- Test: `tests/integrate_test.py`

**Interfaces:**
- Produces: `integrate.codex_programs() -> list[str]`，每项形如 `"/path/to/codex（codex-cli 0.162.0）"`，没找到返回 `[]`

- [ ] **Step 1: 写失败的测试**

`tests/integrate_test.py` 的 `IntegrationTests` 里加：

```python
    def test_codex_programs_lists_override_with_version(self):
        fake = str(Path(__file__).resolve().parent / 'fake_codex.py')
        with mock.patch.dict(os.environ, {'CROSSPET_CODEX_BIN': fake}):
            found = integrate.codex_programs()
        self.assertTrue(found and found[0].startswith(fake), found)
        self.assertIn('codex-cli 0.0.0-fake', found[0])
```

文件顶部 import 加 `import os` 和 `from unittest import mock`（已有的不重复加）。

Run: `python3 -m unittest tests.integrate_test -k codex_programs -v`
Expected: FAIL，`module 'integrate' has no attribute 'codex_programs'`

- [ ] **Step 2: 实现**

`tools/integrate.py`，`def wsl_codex_homes` 后面加：

```python
def codex_programs() -> list:
    """GPT 额度要问 Codex 本地服务：列出能找到的 codex 程序和版本（和桌宠找的顺序一致）"""
    import glob
    import shutil
    import subprocess
    cands = []
    if os.environ.get("CROSSPET_CODEX_BIN"):
        cands.append(os.environ["CROSSPET_CODEX_BIN"])
    if os.name == "nt":
        for name in ("codex.exe", "codex.cmd"):
            p = shutil.which(name)
            if p:
                cands.append(p)
        appdata = os.environ.get("APPDATA", "")
        if appdata:
            cands.append(os.path.join(appdata, "npm", "codex.cmd"))
        try:   # Codex 桌面版开着时，它的 codex.exe 在跑：拿到真实路径（装在受保护目录里也能拿到）
            out = subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-Process codex -ErrorAction SilentlyContinue).Path"],
                                 capture_output=True, text=True, timeout=8).stdout
            cands += [l.strip() for l in out.splitlines() if l.strip()]
        except Exception:
            pass
    else:
        cands.append("/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex")
        p = shutil.which("codex")
        if p:
            cands.append(p)
        cands += ["/opt/homebrew/bin/codex", "/usr/local/bin/codex"]
    seen_, out_ = set(), []
    for c in cands:
        if c in seen_ or not os.path.isfile(c):
            continue
        seen_.add(c)
        try:
            v = subprocess.run([c, "--version"], capture_output=True, text=True, timeout=8).stdout.strip() or "版本未知"
        except Exception as e:
            v = f"运行失败：{e.__class__.__name__}"
        out_.append(f"{c}（{v}）")
    return out_
```

`diagnose()` 里 Codex 段，在 `w = wsl_codex_homes()` 前加：

```python
    progs = codex_programs()
    L.append(f"  codex 程序（GPT 额度向它查）：{'；'.join(short(Path(p.split('（')[0])) + '（' + p.split('（', 1)[1] for p in progs) if progs else '没找到——额度只能读本机会话记录'}")
```

- [ ] **Step 3: 跑测试**

Run: `python3 -m unittest tests.integrate_test -v`
Expected: 全部 PASS（含新用例）

Run: `python3 tools/integrate.py diagnose | grep -A1 "codex 程序"`
Expected: 列出 `/Applications/ChatGPT.app/.../codex（codex-cli 0.162.0-…）`

- [ ] **Step 4: Commit**

```bash
git add tools/integrate.py tests/integrate_test.py
git commit -m "诊断：列出找到的 codex 程序和版本（GPT 额度向它查）"
```

---

### Task 5: Windows 版（问服务 + 记录含 WSL + 调度）

**Files:**
- Modify: `app/windows/src/CodexQuota.cs`（整体改写）
- Modify: `app/windows/src/Pet.cs`（`RefreshQuota` 拆出 GPT；Poll 里 Stop 后；开关；锁屏状态；运行状态行）

**Interfaces:**
- Consumes: 网页 `setCodexQuota(payload)`（Task 1）
- Produces（C#）:
  - `static string CodexQuota.FindBinary()`
  - `static Task<(Dictionary<string, object> rateLimits, string error, int ms)> CodexQuota.QueryLive(string binary, int timeoutMs = 15000)`
  - `static Dictionary<string, object> CodexQuota.Records()`（含 WSL；没有返回 null）
  - `static Dictionary<string, object> CodexQuota.Payload(string source, Dictionary<string, object> rateLimits, string error, int ms)`
  - `static string CodexQuota.Brief(Dictionary<string, object> rl)`
  - Pet：`enum CodexWhy { Now, TurnEnd, Tick }`、`void RefreshCodexQuota(CodexWhy why)`

- [ ] **Step 1: 改写 `CodexQuota.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrossPet
{
    /// <summary>
    /// GPT（Codex）额度来源，和 macOS 版 CodexQuotaSource 同一套规则（docs/superpowers/specs/2026-10-12-codex-quota-design.md）：
    /// 主来源起 `codex app-server` 问 account/rateLimits/read（Codex 用自己的登录查实时额度，不对话、不耗额度；CrossPet 不碰凭据）；
    /// 备用读会话记录（CODEX_HOME，加上开着的 WSL 里的）。排版在网页 codex-quota.js 里做，这里只交原始数据
    /// </summary>
    static class CodexQuota
    {
        static string cached;

        public static string FindBinary()
        {
            var o = Environment.GetEnvironmentVariable("CROSSPET_CODEX_BIN");
            if (!string.IsNullOrEmpty(o)) return File.Exists(o) ? o : null;
            if (cached != null && File.Exists(cached)) return cached;
            cached = null;
            // Codex 桌面版开着时，它自己的 codex.exe 在跑：用它（装在受保护目录里也能拿到路径），记下来下次直接用
            try
            {
                foreach (var p in Process.GetProcessesByName("codex"))
                    try { var f = p.MainModule?.FileName; if (f != null && File.Exists(f)) { Store.Settings["codexBin"] = f; Store.SaveSettings(); return cached = f; } } catch { }
            }
            catch { }
            if (Store.Settings.TryGetValue("codexBin", out var s) && s is string saved && File.Exists(saved)) return cached = saved;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                foreach (var name in new[] { "codex.exe", "codex.cmd" })
                    try { var f = Path.Combine(dir.Trim(), name); if (File.Exists(f)) return cached = f; } catch { }
            var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
            return File.Exists(npm) ? cached = npm : null;
        }

        public static void Forget() { cached = null; }

        public static async Task<(Dictionary<string, object> rateLimits, string error, int ms)> QueryLive(string binary, int timeoutMs = 15000)
        {
            var sw = Stopwatch.StartNew();
            var cmd = binary.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = cmd ? "cmd.exe" : binary,
                Arguments = cmd ? $"/d /s /c \"\"{binary}\" app-server\"" : "app-server",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            Process p;
            try { p = Process.Start(psi); } catch (Exception e) { return (null, "起不来 Codex：" + e.Message, (int)sw.ElapsedMilliseconds); }
            try
            {
                p.ErrorDataReceived += (_, __) => { }; p.BeginErrorReadLine();   // 不读 stderr 会把管道塞满
                var stdin = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                stdin.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"crosspet\",\"version\":\"1\"}}}");
                stdin.WriteLine("{\"method\":\"initialized\"}");
                stdin.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                var read = Task.Run(() =>
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        Dictionary<string, object> msg;
                        try { msg = Store.Json.DeserializeObject(line) as Dictionary<string, object>; } catch { continue; }
                        if (msg == null || !(msg.TryGetValue("id", out var id) && Convert.ToString(id) == "2")) continue;
                        if (msg.TryGetValue("error", out var e) && e is Dictionary<string, object> ed)
                            return ((Dictionary<string, object>)null, ed.TryGetValue("message", out var m) ? m as string : "Codex 服务返回错误");
                        var r = msg.TryGetValue("result", out var rv) ? rv as Dictionary<string, object> : null;
                        var rl = (r != null && r.TryGetValue("rateLimitsByLimitId", out var by) && by is Dictionary<string, object> byd && byd.TryGetValue("codex", out var c) ? c : null) as Dictionary<string, object>
                                 ?? (r != null && r.TryGetValue("rateLimits", out var rr) ? rr as Dictionary<string, object> : null);
                        return (rl, rl == null ? "Codex 服务没给额度" : null);
                    }
                    return ((Dictionary<string, object>)null, "Codex 服务提前退出");
                });
                var winner = await Task.WhenAny(read, Task.Delay(timeoutMs));
                if (winner != read) return (null, $"超时（{timeoutMs / 1000} 秒没回）", (int)sw.ElapsedMilliseconds);
                var (rl2, err) = read.Result;
                return (rl2, err, (int)sw.ElapsedMilliseconds);
            }
            catch (Exception e) { return (null, "问 Codex 出错：" + e.Message, (int)sw.ElapsedMilliseconds); }
            finally { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }
        }

        public static Dictionary<string, object> Payload(string source, Dictionary<string, object> rateLimits, string error, int ms)
        {
            var d = new Dictionary<string, object> { ["source"] = source, ["ms"] = ms, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            if (rateLimits != null) d["rateLimits"] = rateLimits;
            if (error != null) d["error"] = error;
            return d;
        }

        /// 运行状态里的简写：5小时已用 3%，本周已用 40%
        public static string Brief(Dictionary<string, object> rl)
        {
            var parts = new List<string>();
            foreach (var k in new[] { "primary", "secondary" })
            {
                if (!(rl.TryGetValue(k, out var w) && w is Dictionary<string, object> win)) continue;
                var used = win.TryGetValue("usedPercent", out var u) ? u : win.TryGetValue("used_percent", out var u2) ? u2 : null;
                var mins = win.TryGetValue("windowDurationMins", out var m) ? m : win.TryGetValue("window_minutes", out var m2) ? m2 : 0;
                if (!IsNumber(used)) continue;
                var mm = IsNumber(mins) ? Convert.ToInt32(mins) : 0;
                parts.Add($"{(mm >= 10080 ? "本周" : mm >= 1440 ? "今日" : $"{Math.Max(1, mm / 60)}小时")}已用 {(int)Math.Round(Convert.ToDouble(used))}%");
            }
            return string.Join("，", parts);
        }

        // ---------------- 备用：会话记录 ----------------

        public static Dictionary<string, object> Records()
        {
            var files = new List<FileInfo>();
            foreach (var home in new[] { Store.Codex }.Concat(WslHomes()))
                for (int back = 0; back < 4; back++)
                {
                    var day = DateTime.Now.AddDays(-back);
                    var dir = Path.Combine(home, "sessions", day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                    try { files.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl")); } catch { }
                }
            Dictionary<string, object> best = null; var bestTs = "";
            foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc).Take(12))
            {
                var (rl, ts) = FromFile(f.FullName);
                if (rl != null && string.CompareOrdinal(ts, bestTs) > 0) { best = rl; bestTs = ts; }
            }
            return best;
        }

        static (Dictionary<string, object>, string) FromFile(string path)
        {
            string text;
            try
            {
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var n = (int)Math.Min(s.Length, 400_000);
                    s.Seek(-n, SeekOrigin.End);
                    var buf = new byte[n]; var read = 0;
                    while (read < n) { var r = s.Read(buf, read, n - read); if (r <= 0) break; read += r; }
                    text = Encoding.UTF8.GetString(buf, 0, read);
                }
            }
            catch { return (null, ""); }
            foreach (var line in text.Split('\n').Reverse())
            {
                if (!line.Contains("\"rate_limits\"")) continue;
                object parsed; Dictionary<string, object> rl;
                try { parsed = Store.Json.DeserializeObject(line); rl = Find(parsed); } catch { continue; }
                if (rl == null || !(rl.ContainsKey("primary") && rl["primary"] != null || rl.ContainsKey("secondary") && rl["secondary"] != null)) continue;
                var stamp = parsed is Dictionary<string, object> top && top.TryGetValue("timestamp", out var tsv) ? tsv as string ?? "" : "";
                return (rl, stamp);
            }
            return (null, "");
        }

        // ---------------- WSL ----------------
        // 在 WSL 里用 Codex 的人，记录写在 WSL 的 ~/.codex。访问 \\wsl.localhost 会把没开的发行版拉起来（很重），
        // 所以只在 WSL 虚拟机开着时、只访问正在运行的发行版；找到的目录缓存 10 分钟
        static List<string> wslHomes = new List<string>();
        static DateTime wslCheckedAt = DateTime.MinValue;
        public static string WslNote = "";

        static IEnumerable<string> WslHomes()
        {
            if (Process.GetProcessesByName("vmmemWSL").Length == 0 && Process.GetProcessesByName("vmmem").Length == 0) { WslNote = ""; return Array.Empty<string>(); }
            if ((DateTime.UtcNow - wslCheckedAt).TotalMinutes < 10) return wslHomes;
            wslCheckedAt = DateTime.UtcNow;
            var found = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("wsl.exe", "-l --running -q") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.Unicode };
                using (var p = Process.Start(psi))
                {
                    var distros = p.StandardOutput.ReadToEnd().Replace("\0", "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
                    p.WaitForExit(5000);
                    foreach (var d in distros)
                        try
                        {
                            foreach (var u in Directory.GetDirectories($@"\\wsl.localhost\{d}\home"))
                                if (Directory.Exists(Path.Combine(u, ".codex", "sessions"))) found.Add(Path.Combine(u, ".codex"));
                        }
                        catch { }
                }
            }
            catch { }
            wslHomes = found;
            WslNote = found.Count == 0 ? "" : "，含 WSL：" + string.Join("、", found.Select(h => h.Replace(@"\\wsl.localhost\", "")));
            return wslHomes;
        }

        static bool IsNumber(object v) => v is int || v is long || v is double || v is decimal;

        static Dictionary<string, object> Find(object o)
        {
            if (o is Dictionary<string, object> d)
            {
                if (d.TryGetValue("rate_limits", out var rl) && rl is Dictionary<string, object> r) return r;
                foreach (var v in d.Values) { var f = Find(v); if (f != null) return f; }
            }
            else if (o is object[] arr) foreach (var v in arr) { var f = Find(v); if (f != null) return f; }
            return null;
        }
    }
}
```

- [ ] **Step 2: Pet.cs 调度**

删掉 `RefreshQuota()` 里的 GPT 分支（`if (Store.Flag("gptQuota")) { … }` 整段和 `lastGptQuota` 那行），`RefreshQuota()` 只剩 Gemini；在它旁边加：

```csharp
        enum CodexWhy { Now, TurnEnd, Tick }
        DateTime codexLiveAt = DateTime.MinValue;
        bool codexLiveOK, codexInFlight, sessionLocked;
        string lastGptQuota = "还没读过";

        /// GPT 额度什么时候拿、从哪拿（规则见 docs/superpowers/specs/2026-10-12-codex-quota-design.md）
        async void RefreshCodexQuota(CodexWhy why)
        {
            if (!ready || !Store.Flag("gptQuota") || codexInFlight) return;
            if (why == CodexWhy.Tick && (userHidden || sessionLocked)) return;
            var since = (DateTime.UtcNow - codexLiveAt).TotalSeconds;
            var askLive = why == CodexWhy.Now || (why == CodexWhy.TurnEnd && since >= 20) || (why == CodexWhy.Tick && since >= 300);
            if (!askLive) { if (!codexLiveOK) ApplyCodex("records", null, null, 0); return; }
            codexLiveAt = DateTime.UtcNow;
            var bin = CodexQuota.FindBinary();
            if (bin == null) { codexLiveOK = false; ApplyCodex("records", null, "没找到 codex 程序", 0); return; }
            codexInFlight = true;
            try
            {
                var (rl, err, ms) = await CodexQuota.QueryLive(bin);
                codexLiveOK = rl != null;
                if (rl == null) CodexQuota.Forget();   // 程序可能被更新挪走了，下次重新找
                ApplyCodex(rl != null ? "live" : "records", rl, err, ms);
            }
            catch (Exception e) { Store.Log("GPT 额度: " + e.Message); }
            finally { codexInFlight = false; }
        }

        void ApplyCodex(string source, Dictionary<string, object> rl, string error, int ms)
        {
            if (!Store.Flag("gptQuota")) return;   // 查的途中关掉了额度显示
            if (source == "records") { rl = CodexQuota.Records(); if (rl == null) source = "none"; }
            var tag = source == "live" ? $"Codex 服务，{ms / 1000.0:0.0} 秒" : source == "records" ? "本机记录" + CodexQuota.WslNote : "没读到";
            lastGptQuota = $"{DateTime.Now:HH:mm:ss} （{tag}{(error != null ? "；服务：" + error : "")}）" + (rl != null ? CodexQuota.Brief(rl) : "");
            if (source == "none") return;
            Js($"setCodexQuota({Store.Json.Serialize(CodexQuota.Payload(source, rl, error, ms))})");
        }
```

改调用处：
- `Every(TimeSpan.FromSeconds(60), RefreshQuota);` → `Every(TimeSpan.FromSeconds(60), () => { RefreshQuota(); RefreshCodexQuota(CodexWhy.Tick); });`
- `NavigationCompleted` 里 `RefreshQuota();` 后加 `RefreshCodexQuota(CodexWhy.Now);`
- Poll 里 1.3.2 加的 `if (id == "gpt" && … "Stop") After(1500, RefreshQuota);` → `After(1500, () => RefreshCodexQuota(CodexWhy.TurnEnd));`
- 设置开关 `case "gptQuota":` → `if (on) RefreshCodexQuota(CodexWhy.Now); else Js("setCodexQuota(null)");`
- 锁屏事件里记状态：`sessionLocked = e.Reason == SessionSwitchReason.SessionLock;`（在 `BeginInvoke` 的 lambda 里，和 `setPageHidden` 一起）
- 运行状态那一行不变（`lastGptQuota` 现在带来源）

- [ ] **Step 3: 编译**

Run: `tools/build-windows.sh 2>&1 | grep -E "error|Error\(s\)"`
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add app/windows/src/CodexQuota.cs app/windows/src/Pet.cs
git commit -m "Windows：GPT 额度先问 Codex 服务（含正在运行的桌面版 codex.exe），失败退回会话记录（含开着的 WSL 里的），调度和 Mac 一致"
```

- [ ] **Step 5: Windows 实测（loki）**

在 loki 的 Windows 电脑上装测试版：
- 打开额度显示：几秒内显示额度，无「本机记录」；反馈预览运行状态写 `（Codex 服务，x.x 秒）`
- 诊断「codex 程序」那行列出了哪个 `codex`（Codex 桌面版开着 / 关着各看一次）——把桌面版的真实路径记进 spec 第 2 节
- Codex 对话一轮：1.5 秒后刷新
- 有 WSL 的话：WSL 开着时运行状态带「含 WSL：…」

---

### Task 6: 文档

**Files:**
- Modify: `README.md`（隐私段）、`docs/常见问题.md`（「额度 / 余额不显示」GPT 条、「会不会读我的对话 / 密钥」）、`CHANGELOG.md`（加 `## 未发布`）

- [ ] **Step 1: README 隐私段**

把 `- GPT 额度默认关闭。打开后只从 Codex 会话记录里提取额度那几个数字。` 换成：

```markdown
- GPT 额度默认关闭。打开后，CrossPet 每 5 分钟（以及 Codex 每回完一轮时）让**你电脑上的 Codex 自己**向 OpenAI 查一次额度——和在 Codex 里看「使用情况」是同一件事，只是查用量，不对话、**不消耗额度**；CrossPet 不读你的登录凭据，只拿额度数字。找不到 Codex 程序、没登录或断网时，退回从本机 Codex 会话记录里提取额度数字（显示「本机记录」）。
```

把 `- CrossPet 自己只联网做一件事：…` 那条末尾补一句：`（GPT 额度的查询是 Codex 自己发的，见上一条）`

- [ ] **Step 2: 常见问题**

「额度 / 余额不显示」里 GPT 那条换成：

```markdown
- **GPT**：要在右键 →「设置…」里打开「显示 GPT 额度」。桌宠会让电脑上的 Codex 自己去查实时额度（Codex 桌面版或命令行装了、登录了就行），用了重置卡、在别的设备上用过都能跟上，最多 5 分钟。显示「本机记录」时说明没问到 Codex（没找到程序、没登录、断网），用的是本机会话记录，可能不是最新——在 Windows 上用 WSL 里的 Codex 的，WSL 开着时也会读那边的记录。右键「反馈问题…」里的诊断会列出找到的 codex 程序。
```

「会不会读我的对话 / 密钥」里 GPT 那条换成：`- GPT 额度（默认关）：让 Codex 自己查用量（不对话、不耗额度），查不到时只从会话记录里提取额度那几个数字。`

- [ ] **Step 3: CHANGELOG**

在 `# 更新记录` 下面加：

```markdown
## 未发布

- GPT 额度改成直接问 Codex：桌宠让你电脑上的 Codex 自己查实时额度（只查用量，不对话、不耗额度，CrossPet 不碰登录凭据）。用了重置卡、换了套餐、在别的电脑 / 网页上用过，最多 5 分钟就跟上，不用再在 Codex 里对话一轮；reset 动画也一样。查不到时退回读本机会话记录，显示「本机记录」
- Windows 上在 WSL 里用 Codex 的，WSL 开着时也会读那边的会话记录（之前一直显示 Windows 这边的旧额度）
- macOS 读会话记录时也认 CODEX_HOME 了
- 反馈的运行状态写清额度来自哪（Codex 服务 / 本机记录）、查了多久；诊断列出找到的 codex 程序和版本
```

- [ ] **Step 4: Commit**

```bash
git add README.md docs/常见问题.md CHANGELOG.md
git commit -m "文档：GPT 额度改成问 Codex（隐私说明：只查用量、不耗额度、不碰凭据），常见问题、更新记录"
```

---

## 完成后

- 全部测试：`node --test tests/codex_quota_view.test.js && python3 -m unittest tests.integrate_test && app/build.sh >/dev/null && CROSSPET_CODEX_TIMEOUT=3 tests/codex_quota_probe.sh && tools/build-windows.sh | grep "Error(s)"`
- 本地提交，不推送；等 loki 在 Windows 上实测 Task 5 Step 5 后，随下个测试版 / 正式版发（推送前先 `gh pr list` / `gh issue list`）
