# Codex 重置提醒 · 设计

日期：2026-10-14　状态：loki 已同意三部分（10-14），待审阅本文
来源：issue #16（SkywingJam 的提案和插件侧演示）；呈现方式由 loki 重新设计

## 为什么做

Codex 的额度会被官方（@thsottiaux）不定期重置，用的人常去刷推文看「要不要重置了」。第三方站点 codex-resets.com 把这些推文整理成了公开接口。CrossPet 本来就显示 GPT 额度，顺手告诉用户「官方宣布要重置了」，并能问她「会重置吗？」。

## 目标

- 平时什么都不多显示；只有**官方提前宣布**了重置，额度下方才多一行
- 右键「会重置吗？」：她按预测概率用不同的动作和台词回答
- 出现新的强预测时，她闲着主动说一次
- **默认关**，设置里写明数据来自第三方、是预测不是承诺；只对 GPT 角色（含皮肤）生效
- 连不上那个网站时安静失败，不打扰
- 判断写在网页层，外壳只加开关和菜单项（`docs/架构.md`）

不做：新立绘（第一版用 GPT 已有的姿态）、在额度栏显示概率、点那一行打开推文、其他 AI 的重置提醒。

## 一、数据来源

`GET https://codex-resets.com/api/v1/status`（不用 key，约 540 字节，`access-control-allow-origin: *`，网页可以直接 fetch）。
要求：显示数据的地方注明来源并链接 codex-resets.com（放在设置页的说明里）。

| 字段 | 含义 | 我们怎么用 |
|---|---|---|
| `data.scheduled_reset` | 官方已宣布、还没执行：`id`、`announced_at`、`scheduled_for`（可能为 null）、`source.url` | 额度下方那一行；最高一档回答；主动说 |
| `data.active_watch` | AI 分类的预测：`level`（elevated / strong）、`reset_chance_percent`（可能为 null）、`forecast_window`、`expires_at`、`source` | 问她时的档位；strong 或 ≥70% 时主动说 |
| `data.latest_reset` | 最近一次已执行的重置 | 判断宣布是否已经执行（id 相同 = 执行了） |
| `data.stats` | `days_since_last`、`avg_interval_days` | 没有预测时的回答里说「上次是 N 天前，平均 M 天一次」 |

注意：
- 不支持 304（带 ETag 再问还是 200），反正很小
- 429 带 `Retry-After`；503 / 网络错误按失败处理
- 国内网络可能连不上（和检查更新失败同一类），必须安静失败
- 2026-10 的历史：61 次，平均 6.5 天一次，几乎都是执行完才发推（「It's done」），提前宣布很少——所以额度下方那一行很少出现，这是预期的

## 二、用户看到的

### 设置 →「额度」页

新开关，默认关：

> **Codex 重置提醒**　数据来自第三方网站 [codex-resets.com](https://codex-resets.com)，根据官方推文整理，预测不是 OpenAI 的承诺

关着时下面三样都不出现，也不联网。

### ① 额度下方一行（只在官方已宣布时）

- `🔔 官方已宣布重置 · 预计 10/16 14:00`；没给时间：`🔔 官方已宣布重置，还没生效`
- 只在当前角色是 GPT（含皮肤）时显示；开了「合并显示各家额度」时挂在 GPT 那一行下面
- 消失条件（任一）：站点上 `scheduled_reset` 没了；`latest_reset.id` 等于这条宣布的 id；我们自己在宣布之后检测到了 Codex 额度重置（已有的 `quotaEvent` 判出 `reset`）

### ② 右键「会重置吗？」

- 只在开关开着、当前角色是 `gpt` 时出现在右键菜单里
- 点了：先 `thinking`；数据超过 5 分钟没更新就现查一次（最多等 10 秒）；然后按下表回答

| 档 | 条件（从上往下取第一个） | 动作 | 台词键 | 默认台词大意（GPT 原版） |
|---|---|---|---|---|
| announced | 有 `scheduled_reset` 且未执行 | `reset` | `resetAnnounced` | 「官方说了，要重置！预计 {when}」 |
| high | 预测概率 ≥ 70 | `happy` | `resetHigh` | 「这次很有戏：{chance}%，{window}」 |
| mid | 预测概率 30–69；或没给概率但等级是 strong | `hum` | `resetMid` | 「有点风声，{chance}% 吧，别太指望」 |
| low | 有预测但概率 < 30；或没给概率、等级是 elevated | `sulk` | `resetLow` | 「有动静，但不太像…」 |
| none | 没有预测 | `idle` | `resetNone` | 「最近没风声。上次重置是 {since} 天前，平均 {avg} 天一次」 |
| offline | 这次和之前都没查到 | `idle` | `resetOffline` | 「我这会儿连不上那个网站…」 |

- 台词写在角色 `character.json` 的 `lines` 里（同一个键可以多句，随机挑），`{when}` `{chance}` `{window}` `{since}` `{avg}` 由程序填；皮肤自己的 `character.json` 可以写自己的一套（降智龙崽另写）；角色没写就用 `LOCAL_LINES` 里的默认句
- `{window}` 用站点的 `forecast_window` 原文翻成中文（`next 48h` → 「接下来 48 小时内」；认不出的原样显示）
- 动作缺图时照常退回 `idle`

### ③ 主动说一次

- 触发：出现新的 `scheduled_reset`，或新的 `active_watch` 满足 `level === 'strong'` 或概率 ≥ 70
- 只在当前角色是 GPT 时说；不是的话先记着，切到 GPT 时再说（过了 `expires_at` 就不说了）
- 和插件更新提醒一样：闲着、不在彩蛋、没在说别的话时才说，等不到就过一会儿再看
- 说的就是②里对应那一档的台词
- 说过的 id 记在 localStorage（`crosspet-resets-told`，宣布和预测各自的 id；预测没有 id，用 `observed_at` 当 id），重启也不重复

## 三、程序分工

### 网页层 `app/web/codex-resets.js`（新，纯函数，node 测试）

- `parse(json)` → `{ announced, watch, latest, stats }` 或 null（字段缺失、类型不对的那块当没有）
- `notice(data, resetSeenAt)` → 额度下方那一行的文字或 `''`
- `answer(data, now)` → `{ tier, pose, vars }`（`vars` 是填台词用的值）
- `toAnnounce(data, told)` → 要主动说的 `{ id, tier }` 或 null
- `windowText(s)` → `forecast_window` 的中文

### `app/web/index.html`

- `setCodexResets(on)`：外壳在网页加载完和开关变化时调用；开着就立刻查一次，之后每 30 分钟查一次；关了停止并清掉显示
- 查询：`fetch`，10 秒超时；429 按 `Retry-After` 等（没给就 30 分钟），其他失败 10 分钟后重试；结果和时间存在内存
- `renderQuota()` 末尾：当前是 GPT 时把 `notice()` 的文字挂到额度下方（和 GPT 额度的排版各管各的）
- `askReset()`：外壳菜单点了调用
- `codexResetsStatus()`：给反馈用，返回「重置提醒：关 / 最近一次 14:03 成功 / 连不上（14:03）」

### 两个外壳

- 设置 `codexResets`（布尔，默认 false）；设置页开关；网页加载完、开关变化时调 `setCodexResets`
- 右键菜单「会重置吗？」：开关开着且当前角色是 `gpt` 时才加，点了调 `askReset()`
- 反馈运行状态加一段 `codexResetsStatus()` 的结果

### 设置页 `settings.html`

「额度」页加开关和说明（带来源链接，链接用外部浏览器打开，和现有链接一样）。

## 四、测试

- `tests/codex_resets.test.js`：用真实接口形状做样本——全空；只有宣布（有 / 无 `scheduled_for`）；宣布已执行（`latest_reset.id` 相同）；预测各档（85 / 50 / 10 / null）；预测过期；字段类型不对；`forecast_window` 认得 / 认不出；主动说的规则（新的说、说过的不说、elevated 低概率不说、过期不说）
- 网页：浏览器里用假数据看额度下方那一行、问她各档的动作和台词、主动说只说一次
- 外壳：两端编译；Mac 实测菜单只在 GPT 时出现；Windows 由 Codex 跑一轮
- 文档：`docs/架构.md` 文件列表和测试表各补一行；CHANGELOG；README 提一句；issue #16 做好后 @ SkywingJam 帮测

## 修订（写计划和实现时）

- 合并显示各家额度时，那一行放在表格最下面、横跨整张表（不是插在 GPT 那一行下面）：GPT 是当前角色时本来就排第一行，放最下面实现简单、不改 quota-aggregate.js
- 反馈里只写「重置提醒 开 / 关」，不写最近一次查询结果：反馈是外壳同步拼的、读不到网页里的状态，为一行诊断加双向通道不值；连不上时问她会直接说
- 没有统计数据时 none 档用单独的台词键 `resetQuiet`
- 问她时至少想 1.2 秒再回答（数据是新的、或一下子就连不上时，也看得见她在想）
- （10-15 loki 决定）**加专属立绘**，推翻「不加新立绘」：新姿态 `reset_check`（被问时刷手机）、`reset_bell`（官方已宣布，摇铃）、`reset_hope`（≥70%）、`reset_maybe`（30–69%）、`reset_nope`（更低 / 没风声 / 连不上），GPT 原版和降智龙崽各一套，提示词在 `docs/prompts/重置提醒/`；没画的退回 `thinking` / `reset` / `happy` / `hum` / `sulk`（`POSE_FALLBACK`）。`reset` 仍只用于「真的重置了」的庆祝
- 开发者控制台「场景演练」加「Codex 重置提醒（6 档）」：不联网，用假数据依次演各档，演完恢复真实设置
- （10-15 Windows 实测发现）宣布之后已经重置了（站点记为已执行、或我们看到额度重置）时，问她答新档 `done`（`reset` 动作、台词键 `resetDone`），不再落到「没风声」；开发者控制台演「额度提前 reset」用的是假额度，不算「看到重置」（以前演过一次之后，真实的宣布会一直被当成已执行，直到重启）
- （10-15 断网实测）问她时**每次都现查**，这次没查到就答「连不上」，不再拿上次的结果冒充（原来 5 分钟内不查、查不到也用旧的，断网后一直重复缓存）；额度下方那一行和主动提醒仍用最近一次查到的
