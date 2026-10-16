# GPT（Codex）额度：改成直接问 Codex

2026-10-12 · 状态：已实现（1.3.3）

> **修订（2026-10-12 晚，loki 确认）：去掉「备用：读本机会话记录」。** Windows 实测：对话了一轮，会话记录里的额度也不一定更新；退回读记录时 20 秒内在 5% / 6% 之间来回跳（挑到不同对话的记录）。会话记录不是给别人读额度用的，读出来的数「看着像新的、其实是错的」，比不显示更糟。改为：
> - 只认 Codex 服务问来的数；问不到时接着显示**最后一次问到的数**，标上「· HH:MM 的数据」（`source: "stale"`）；从没问到过就不显示，设置里那个开关下面写原因
> - WSL：Windows 这边问不到、WSL 又开着时，通过 `wsl.exe -d <发行版> -e bash -lic "exec <codex> app-server"` 在 WSL 里问服务（发行版和 codex 路径找一次缓存 10 分钟；WSL 没开不碰）
> - 下面第 1 节第 2 条、第 2.1 节、第 4 节里关于「本机记录」的内容作废

## 为什么要改

GPT 额度现在只有一个来源：翻本机 Codex 会话记录（`~/.codex/sessions/**/*.jsonl`），找最后一条 `rate_limits`。1.3.0–1.3.2 收到的额度问题全都出在这个来源上，补一个冒一个：

| 遇到过的问题 | 根子 |
|---|---|
| 用了重置卡、换了套餐，要在 Codex 里对话一轮才更新 | 记录只在 Codex 回复时写 |
| WSL 用户「重置后没更新」（反馈 10-11 23:52） | 记录写在 WSL 里，桌宠看不到，显示 Windows 这边的旧记录 |
| 在别的电脑 / 网页 / 手机上用了，桌宠不知道 | 记录只有本机这份 |
| Windows 上显示旧额度、要关开一次设置才刷新 | 按文件修改时间挑「最新」，开着的文件修改时间不更新（1.3.2 已绕开） |
| 有时只有「本周」没有「5 小时」 | 旧记录里本来就只有一个窗口 |
| Mac 不认 `CODEX_HOME` | 两个平台各写各的找文件逻辑 |

## 新来源：Codex 本地服务（app-server）

Codex 自带一个本地服务 `codex app-server`（Codex 桌面版就是通过它和 Codex 说话的），协议里有 `account/rateLimits/read`：由 Codex 用它自己的登录状态向 OpenAI 查**实时**额度，和在 Codex 里看「使用情况」是同一件事。CrossPet 不读、不碰登录凭据。

2026-10-12 在 macOS 上实测（ChatGPT.app 自带的 codex-cli 0.162.0-alpha.17.2）：

- 起服务 + 握手 0.3 秒，查额度 2.7 秒（主要是网络）
- 返回 `rateLimits`（`limitId: codex`）：`primary` / `secondary` 各有 `usedPercent`、`windowDurationMins`、`resetsAt`，另有 `planType`、`credits`、`rateLimitResetCredits`（重置卡）
- 没登录时立刻返回错误 `codex account authentication required to read rate limits`（code -32600），不会卡住
- 副作用：只刷新了 Codex 自己的日志库和 `models_cache.json`；没有新对话、不跑后台任务、不耗额度

## 设计

### 1. 两个来源，一个结果

1. **主来源：问 Codex 本地服务。** 找到 `codex` 程序 → 起 `codex app-server`（stdio）→ `initialize`（clientInfo `crosspet`）→ `initialized` → `account/rateLimits/read` → 关掉进程。整个过程 15 秒超时，超时就杀进程。
2. **备用来源：本机会话记录（现在的做法）。** 找不到 `codex` 程序、返回错误（没登录、断网、旧版 Codex 没有这个接口）、超时时才用。两个平台都按 `CODEX_HOME`（没设就是 `~/.codex`）找记录；Windows 上还加上 WSL 里的记录（见第 2.1 节）。所有记录放在一起，按每条记录自带的 `timestamp` 挑最新的。
3. 两个来源都整理成同一个结构交给网页（不变）：`{ text, low, windows: [{label, used, reset}] }`，另加 `source: "live" | "records"`。

### 2. 找 `codex` 程序

按顺序找，第一个能用的就用，结果缓存到下次 App 启动（或者查询失败时重新找一次）：

- **macOS**：`/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex`（桌面版自带，随桌面版更新）→ 登录 shell 里的 `command -v codex`（npm / Homebrew 装的命令行；GUI 程序的 PATH 不全，所以用 `zsh -lc` 问一次）→ `/opt/homebrew/bin/codex`、`/usr/local/bin/codex`
- **Windows**：`PATH` 里的 `codex.exe` / `codex.cmd`（npm 装的）→ Codex 桌面版自带的位置（**待查**：作者没有 Windows 版 Codex，先在诊断里列出找到的候选，实测后补进来）

`codex` 程序继承 CrossPet 的环境变量（含 `CODEX_HOME`）。

### 2.1 WSL 里的 Codex（Windows）

在 WSL 里用 Codex 的人（含 Codex 桌面版「在 WSL 里运行」），会话记录写在 WSL 的 `~/.codex/sessions`，Windows 这边只剩旧记录。这类用户 Windows 这边通常没登录 Codex，主来源会失败、退回备用来源，所以备用来源要能读到 WSL 那边：

- **只在 WSL 已经开着时读。** 访问 `\\wsl.localhost\<发行版>\...` 会把没开的发行版拉起来（很重），所以：
  1. 先看 WSL 的虚拟机进程（`vmmemWSL`，老版本叫 `vmmem`）在不在，不在就整个跳过——WSL 没开，也就不可能正在用 WSL 里的 Codex
  2. 在的话跑一次 `wsl.exe -l --running -q` 拿到**正在运行**的发行版，只访问这些
- **有哪些 Codex 目录：** 在运行的发行版里找 `\\wsl.localhost\<发行版>\home\*\.codex\sessions`，结果缓存 10 分钟（发行版列表和目录都缓存），不每分钟都翻
- **读法和 Windows 这边一样：** 最近 4 天的会话里挑修改 / 创建时间最新的几个，读末尾，取带时间戳的最新额度记录，和 Windows 这边的记录一起比
- 显示同样带「 · 本机记录」；诊断里写清读到的是哪个发行版的记录
- 读失败（权限、路径不存在、WSL 中途关了）就当没有，不报错、不重试到卡住

### 3. 什么时候查

- 打开「显示 GPT 额度」时：立刻查一次
- Codex 每回完一轮（收到 gpt 的 Stop）：1.5 秒后查一次；两次之间至少隔 20 秒
- 平时：每 5 分钟查一次
- 不查：额度显示关着；桌宠藏着（快捷键隐藏）、锁屏、屏幕休眠
- 主来源失败时：退回备用来源，并按现在的节奏（每分钟）读记录；之后每 5 分钟再试一次主来源

### 4. 显示

- 格式不变：`5小时 剩余 97% ｜ 本周 剩余 99% · 21:40 重置`
- **不显示重置卡张数**（2026-10-12 和 loki 确认）
- 来自备用来源时末尾加「 · 本机记录」，让人知道可能不是最新
- 过了重置时间的窗口显示 100%（不变，主要给备用来源用）
- `low`：剩得最少的窗口用掉 ≥ 90%（不变）

### 5. reset 动画

规则不变（某个窗口在预定重置时间之前恢复一大截 → reset）。主来源是实时的，用了重置卡、官方重置时，下一次查询（最多 5 分钟）就能演，不用等在 Codex 里对话一轮。正在干活时留到这一轮结束再演（1.3.2 已有）。

### 6. 诊断和反馈

- 运行状态里的「GPT 额度最近一次读到」加上来源和耗时，比如 `23:52:27（Codex 服务，2.7 秒）…` 或 `（本机记录：Codex 服务失败——需要登录）…`
- 接入诊断的 Codex 部分列出找到的 `codex` 程序路径和版本（`codex --version`）
- 不写进日志 / 反馈：`accountId`、重置卡 id 等账号标识

### 7. 隐私说明（README、常见问题）

现在写的是「CrossPet 自己只联网查 GitHub 版本」。改成：打开 GPT 额度后，CrossPet 会定期让**你电脑上的 Codex 自己**向 OpenAI 查一次额度（等同于在 Codex 里看使用情况）；CrossPet 不读登录凭据，只拿额度数字。查不到时退回读本机会话记录。

## 不在这次范围

- 通过 `wsl.exe` 在 WSL 里起 `codex app-server` 查实时额度（WSL 用户这次先靠第 2.1 节读 WSL 里的记录）
- WSL 里的 Codex 干活时跟着动（钩子要装进 WSL 并跨系统写状态）
- 显示重置卡张数、积分余额

## 测试

- 用录下来的 `account/rateLimits/read` 返回（成功、只有一个窗口、需要登录、方法不存在）做固定样例，两个平台的解析各跑一遍，结果和网页现在收到的格式逐字段对上
- 找不到程序 / 超时 / 返回错误时退回备用来源，显示带「本机记录」
- macOS 真机：开关额度、Codex 对话一轮、等 5 分钟、藏起来时不查（看日志里查询次数）
- Windows：在 loki 的 Windows 电脑上看诊断列出的 `codex` 程序，再验证同样几项
- WSL：模拟 `\\wsl.localhost` 下的会话目录验证合并挑选；WSL 没开时确认不访问（不拉起发行版）；在有 WSL 的 Windows 上实测（loki 的 Windows 或反馈用户）
- 回归：1.3.2 的「干活途中刷新 → 干完演 reset」不受影响

## 修订 2（2026-10-13）：定时查询改快

用户希望额度跟得更快。实测（macOS，真实 Codex 连问 6 次）：一次约 3 秒（主要等网络）、约 0.15 秒 CPU、临时 130MB 内存。
「平时每 5 分钟」改成：Codex 在干活（10 分钟内有不是 Stop 的事件）每 1 分钟、闲着每 2 分钟；定时器误差各留 5 秒（55 / 115 秒）。
每分钟一次平均约 0.25% 一个核心。不再压到 30 秒以下：OpenAI 查用量接口的频率限制未知。规则在 `App.codexTickDue` / `Pet.CodexTickDue`，`tests/codex_quota_probe.sh` 测

