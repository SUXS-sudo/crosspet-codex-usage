# Codex 重置提醒 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GPT 角色在官方提前宣布 Codex 重置时于额度下方显示一行，右键「会重置吗？」按 codex-resets.com 的预测分档回答，强预测时主动说一次；默认关闭。

**Architecture:** 判断全在网页层：新文件 `app/web/codex-resets.js`（纯函数，node 测试）负责解析、分档、要不要说；`index.html` 负责联网（fetch，对方允许跨域）、定时、显示和说话。两个外壳只加设置开关、右键菜单项、设置页的来源链接动作和反馈里的开关状态。

**Tech Stack:** 原生 JS（网页层，node:test 测试）、Swift（macOS 外壳）、C# WPF（Windows 外壳）。

**Spec:** `docs/superpowers/specs/2026-10-14-codex-resets-design.md`

## Global Constraints

- 设置键 `codexResets`，布尔，**默认 false**；关着时不联网、不显示、菜单里没有「会重置吗？」
- 只对 `gpt` 角色（含皮肤，皮肤的角色 id 仍是 `gpt`）生效
- 接口：`GET https://codex-resets.com/api/v1/status`；超时 10 秒；429 按 `Retry-After` 秒数等（没给按 1800 秒）；其他失败 10 分钟后重试；成功后 30 分钟再查；问她时数据超过 5 分钟就现查
- 来源说明必须带 codex-resets.com 链接，写明「预测不是 OpenAI 的承诺」
- 档位：announced（宣布未执行）→ `reset`；high（≥70）→ `happy`；mid（30–69，或无概率但 strong）→ `hum`；low（<30，或无概率且 elevated）→ `sulk`；none → `idle`；offline（从没查到过）→ `idle`
- 台词键：`resetAnnounced` `resetHigh` `resetMid` `resetLow` `resetNone` `resetQuiet`（没有统计数据时的 none）`resetOffline`；占位符 `{when}` `{chance}` `{window}` `{since}` `{avg}`
- 主动说：新宣布，或新预测满足 `level === 'strong'` 或概率 ≥ 70；同一 id 只说一次，记在 localStorage `crosspet-resets-told`
- 不加新立绘；只 `git add` 自己改的文件（仓库规矩，不许 `git add -A`）；所有文字用中文

## Review Focus

- 接口返回 `reset_chance_percent: null` 的 strong 预测：应落到 mid 档、会主动说，不能说「不太像」
- 宣布已执行但站点还没撤（`latest_reset.id` 等于宣布 id，或我们自己检测到 Codex 额度重置晚于宣布）：额度下方那一行必须消失，问她也不能再说「要重置」
- 预测已过 `expires_at`：既不主动说、也不按它分档（按 none 回答）
- 断网 / 国内连不上：问她回答 offline 档，不报错、不卡在 thinking；之前查到过的数据继续用
- 开关关掉：额度下方那一行立刻消失、定时器停掉、菜单项不再出现

---

### Task 1: 纯函数模块 `codex-resets.js`

**Files:**
- Create: `app/web/codex-resets.js`
- Test: `tests/codex_resets.test.js`

**Interfaces:**
- Produces（挂在 `window.CodexResets`，node 里 `module.exports`）：
  - `parse(json) → { announced, watch, latest, stats } | null`
    - `announced: { id: string, at: number(秒), for: number(秒)|null, url: string } | null`
    - `watch: { id: string(observed_at 原文), level: 'elevated'|'strong', chance: number|null, window: string, expires: number(秒，0=没给) } | null`
    - `latest: { id: string, at: number } | null`；`stats: { since: number, avg: number } | null`
  - `notice(data, resetSeenAt) → string`（`''` = 不显示）
  - `answer(data, now, resetSeenAt) → { tier, pose, key, vars }`
  - `toAnnounce(data, now, resetSeenAt, told: string[]) → { id: string } | null`
  - `windowText(s) → string`；`fill(line, vars) → string`；`fmt(sec) → 'MM/DD HH:mm'`（本地时间）
  - `LINES`：各台词键的默认台词（`{ resetAnnounced: [...], ... }`）

- [ ] **Step 1: 写测试**

`tests/codex_resets.test.js`：

```js
process.env.TZ = 'Asia/Shanghai'   // fmt 用本地时间，测试固定时区
const test = require('node:test')
const assert = require('node:assert')
const R = require('../app/web/codex-resets.js')

const NOW = Date.parse('2026-10-14T15:00:00Z') / 1000
const base = (data) => ({ data: { latest_reset: null, scheduled_reset: null, active_watch: null, stats: { total: 61, last_reset_at: '2026-10-13T05:06:06.000Z', days_since_last: 1.4, avg_interval_days: 6.5 }, ...data }, meta: { api_version: 'v1' } })
const watch = (level, chance, extra = {}) => ({ level, reset_chance_percent: chance, forecast_window: 'next 48h', observed_at: '2026-10-14T10:00:00.000Z', expires_at: '2026-10-16T10:00:00.000Z', text: 't', source: { type: 'x_post', author: 'thsottiaux', url: 'https://x.com/a/status/1' }, ...extra })
const sched = (extra = {}) => ({ id: '999', status: 'scheduled', reset_type: 'regular', announced_at: '2026-10-14T12:00:00.000Z', scheduled_for: '2026-10-16T06:00:00.000Z', text: 't', source: { type: 'x_post', author: 'thsottiaux', url: 'https://x.com/a/status/999' }, ...extra })

test('parse：全空（真实接口 10-14 的样子）', () => {
  const d = R.parse(base({ latest_reset: { id: '210', reset_type: 'regular', announced_at: '2026-10-13T05:06:06.000Z', text: 't', source: { type: 'x_post', author: 'thsottiaux', url: 'u' } } }))
  assert.strictEqual(d.announced, null)
  assert.strictEqual(d.watch, null)
  assert.deepStrictEqual(d.latest, { id: '210', at: Date.parse('2026-10-13T05:06:06Z') / 1000 })
  assert.deepStrictEqual(d.stats, { since: 1.4, avg: 6.5 })
})

test('parse：不认识的形状返回 null 或把那一块当没有', () => {
  for (const bad of [null, {}, { data: null }, { data: 3 }, 'x']) assert.strictEqual(R.parse(bad), null)
  const d = R.parse(base({ active_watch: { level: 'huge' }, scheduled_reset: { id: 5 }, stats: { days_since_last: 'a' } }))
  assert.strictEqual(d.watch, null)
  assert.strictEqual(d.announced, null)
  assert.strictEqual(d.stats, null)
})

test('notice：有宣布才有一行；带时间 / 不带时间', () => {
  assert.strictEqual(R.notice(R.parse(base({})), 0), '')
  assert.strictEqual(R.notice(R.parse(base({ active_watch: watch('strong', 90) })), 0), '')
  assert.strictEqual(R.notice(R.parse(base({ scheduled_reset: sched() })), 0), '🔔 官方已宣布重置 · 预计 10/16 14:00')
  assert.strictEqual(R.notice(R.parse(base({ scheduled_reset: sched({ scheduled_for: null }) })), 0), '🔔 官方已宣布重置，还没生效')
})

test('notice：宣布已执行就不显示（站点记为已执行，或我们在宣布之后看到额度重置）', () => {
  const done = R.parse(base({ scheduled_reset: sched(), latest_reset: { id: '999', reset_type: 'regular', announced_at: '2026-10-14T13:00:00.000Z', text: 't', source: { type: 'x_post', author: 'thsottiaux', url: 'u' } } }))
  assert.strictEqual(R.notice(done, 0), '')
  const d = R.parse(base({ scheduled_reset: sched() }))
  assert.strictEqual(R.notice(d, Date.parse('2026-10-14T13:00:00Z') / 1000), '')   // 宣布 12:00，13:00 我们看到重置
  assert.notStrictEqual(R.notice(d, Date.parse('2026-10-14T11:00:00Z') / 1000), '') // 宣布之前的重置不算
})

test('answer：各档的动作、台词键和填空', () => {
  const a = (data) => R.answer(R.parse(base(data)), NOW, 0)
  assert.deepStrictEqual(a({ scheduled_reset: sched() }), { tier: 'announced', pose: 'reset', key: 'resetAnnounced', vars: { when: '10/16 14:00' } })
  assert.deepStrictEqual(a({ active_watch: watch('strong', 85) }), { tier: 'high', pose: 'happy', key: 'resetHigh', vars: { chance: 85, window: '接下来 48 小时内' } })
  assert.strictEqual(a({ active_watch: watch('elevated', 70) }).tier, 'high')
  assert.strictEqual(a({ active_watch: watch('elevated', 50) }).tier, 'mid')
  assert.strictEqual(a({ active_watch: watch('elevated', 29) }).tier, 'low')
  assert.strictEqual(a({ active_watch: watch('strong', null) }).tier, 'mid')     // 没给概率但等级高：不能说「不太像」
  assert.strictEqual(a({ active_watch: watch('elevated', null) }).tier, 'low')
  assert.deepStrictEqual(a({}), { tier: 'none', pose: 'idle', key: 'resetNone', vars: { since: '1.4', avg: '6.5' } })
  assert.strictEqual(R.answer(R.parse(base({ stats: null })), NOW, 0).key, 'resetQuiet')
  assert.deepStrictEqual(R.answer(null, NOW, 0), { tier: 'offline', pose: 'idle', key: 'resetOffline', vars: {} })
  assert.strictEqual(R.answer(R.parse(base({ scheduled_reset: sched({ scheduled_for: null }) })), NOW, 0).vars.when, '很快')
})

test('answer：过期的预测按没有预测回答', () => {
  const d = R.parse(base({ active_watch: watch('strong', 90, { expires_at: '2026-10-14T14:00:00.000Z' }) }))
  assert.strictEqual(R.answer(d, NOW, 0).tier, 'none')
})

test('toAnnounce：新的强预测 / 新宣布说一次，说过的、弱的、过期的不说', () => {
  const strong = R.parse(base({ active_watch: watch('strong', 60) }))
  assert.deepStrictEqual(R.toAnnounce(strong, NOW, 0, []), { id: 'w:2026-10-14T10:00:00.000Z' })
  assert.strictEqual(R.toAnnounce(strong, NOW, 0, ['w:2026-10-14T10:00:00.000Z']), null)
  assert.ok(R.toAnnounce(R.parse(base({ active_watch: watch('elevated', 75) })), NOW, 0, []))
  assert.strictEqual(R.toAnnounce(R.parse(base({ active_watch: watch('elevated', 50) })), NOW, 0, []), null)
  assert.strictEqual(R.toAnnounce(R.parse(base({ active_watch: watch('elevated', null) })), NOW, 0, []), null)
  assert.strictEqual(R.toAnnounce(R.parse(base({ active_watch: watch('strong', 90, { expires_at: '2026-10-14T14:00:00.000Z' }) })), NOW, 0, []), null)
  assert.deepStrictEqual(R.toAnnounce(R.parse(base({ scheduled_reset: sched(), active_watch: watch('strong', 90) })), NOW, 0, []), { id: 'a:999' })
  assert.strictEqual(R.toAnnounce(null, NOW, 0, []), null)
})

test('windowText / fill', () => {
  assert.strictEqual(R.windowText('next 48h'), '接下来 48 小时内')
  assert.strictEqual(R.windowText('next 2 days'), '接下来 2 天内')
  assert.strictEqual(R.windowText('this week'), 'this week')
  assert.strictEqual(R.fill('{chance}%，{window}', { chance: 85, window: '接下来 48 小时内' }), '85%，接下来 48 小时内')
  assert.strictEqual(R.fill('没有 {x}', {}), '没有 {x}')
})

test('LINES：每个台词键都有默认句', () => {
  for (const k of ['resetAnnounced', 'resetHigh', 'resetMid', 'resetLow', 'resetNone', 'resetQuiet', 'resetOffline'])
    assert.ok(Array.isArray(R.LINES[k]) && R.LINES[k].length, k)
})
```

- [ ] **Step 2: 跑测试，确认失败**

Run: `node --test tests/codex_resets.test.js`
Expected: FAIL，`Cannot find module '../app/web/codex-resets.js'`

- [ ] **Step 3: 写实现**

`app/web/codex-resets.js`：

```js
// Codex 重置提醒（设置里默认关）：数据来自第三方网站 codex-resets.com（根据 @thsottiaux 的推文整理，预测不是 OpenAI 的承诺）。
// 这里只做判断：接口的数据整理成三块（官方宣布 / 预测 / 统计）、额度下方显示什么、问她时回答哪一档、要不要主动说。
// 联网、显示、说话在 index.html；纯函数，网页和测试（tests/codex_resets.test.js）共用。设计见 docs/superpowers/specs/2026-10-14-codex-resets-design.md
;(function (root) {
  const sec = s => { const t = typeof s === 'string' ? Date.parse(s) : NaN; return isNaN(t) ? null : t / 1000 }

  function parse(j) {
    const d = j && typeof j === 'object' ? j.data : null
    if (!d || typeof d !== 'object') return null
    const s = d.scheduled_reset, w = d.active_watch, l = d.latest_reset, st = d.stats
    const announced = s && typeof s.id === 'string' && sec(s.announced_at) !== null
      ? { id: s.id, at: sec(s.announced_at), for: sec(s.scheduled_for), url: s.source?.url || '' } : null
    const watch = w && (w.level === 'elevated' || w.level === 'strong') && typeof w.observed_at === 'string'
      ? { id: w.observed_at, level: w.level, chance: Number.isInteger(w.reset_chance_percent) ? w.reset_chance_percent : null,
          window: typeof w.forecast_window === 'string' ? w.forecast_window : '', expires: sec(w.expires_at) || 0 } : null
    const latest = l && typeof l.id === 'string' ? { id: l.id, at: sec(l.announced_at) || 0 } : null
    const stats = st && typeof st.days_since_last === 'number' && typeof st.avg_interval_days === 'number'
      ? { since: st.days_since_last, avg: st.avg_interval_days } : null
    return { announced, watch, latest, stats }
  }

  // 还有效的宣布：站点没记为已执行，我们也没在宣布之后看到 Codex 额度重置（resetSeenAt：index.html 的 quotaEvent 判出 reset 的时间，秒）
  function liveAnnounced(d, resetSeenAt) {
    const a = d?.announced
    if (!a || d.latest?.id === a.id || (resetSeenAt && resetSeenAt > a.at)) return null
    return a
  }
  const liveWatch = (d, now) => (d?.watch && (!d.watch.expires || d.watch.expires > now) ? d.watch : null)

  const pad = n => String(n).padStart(2, '0')
  function fmt(s) { const t = new Date(s * 1000); return `${pad(t.getMonth() + 1)}/${pad(t.getDate())} ${pad(t.getHours())}:${pad(t.getMinutes())}` }

  function notice(d, resetSeenAt) {
    const a = liveAnnounced(d, resetSeenAt)
    if (!a) return ''
    return a.for ? `🔔 官方已宣布重置 · 预计 ${fmt(a.for)}` : '🔔 官方已宣布重置，还没生效'
  }

  function windowText(s) {
    let m = /^next\s+(\d+)\s*h(?:ours?)?$/i.exec(s || '')
    if (m) return `接下来 ${m[1]} 小时内`
    m = /^next\s+(\d+)\s*d(?:ays?)?$/i.exec(s || '')
    if (m) return `接下来 ${m[1]} 天内`
    return s || ''
  }

  const POSE = { announced: 'reset', high: 'happy', mid: 'hum', low: 'sulk', none: 'idle', offline: 'idle' }
  function tierOf(d, now, resetSeenAt) {
    if (liveAnnounced(d, resetSeenAt)) return 'announced'
    const w = liveWatch(d, now)
    if (!w) return 'none'
    if (w.chance === null) return w.level === 'strong' ? 'mid' : 'low'
    return w.chance >= 70 ? 'high' : w.chance >= 30 ? 'mid' : 'low'
  }

  function answer(d, now, resetSeenAt) {
    if (!d) return { tier: 'offline', pose: 'idle', key: 'resetOffline', vars: {} }
    const tier = tierOf(d, now, resetSeenAt)
    if (tier === 'announced') { const a = liveAnnounced(d, resetSeenAt); return { tier, pose: POSE[tier], key: 'resetAnnounced', vars: { when: a.for ? fmt(a.for) : '很快' } } }
    if (tier === 'none') return d.stats
      ? { tier, pose: 'idle', key: 'resetNone', vars: { since: d.stats.since.toFixed(1), avg: d.stats.avg.toFixed(1) } }
      : { tier, pose: 'idle', key: 'resetQuiet', vars: {} }
    const w = liveWatch(d, now)
    const key = { high: 'resetHigh', mid: 'resetMid', low: 'resetLow' }[tier]
    const window = windowText(w.window)
    return { tier, pose: POSE[tier], key, vars: w.chance !== null ? { chance: w.chance, window } : { window } }
  }

  // 主动说：新宣布，或新的强预测（strong 或 ≥70%）；told 是已经说过的 id（宣布 'a:<id>'，预测 'w:<observed_at>'）
  function toAnnounce(d, now, resetSeenAt, told) {
    const a = liveAnnounced(d, resetSeenAt)
    if (a) return told.includes('a:' + a.id) ? null : { id: 'a:' + a.id }
    const w = liveWatch(d, now)
    if (!w || !(w.level === 'strong' || (w.chance ?? 0) >= 70)) return null
    return told.includes('w:' + w.id) ? null : { id: 'w:' + w.id }
  }

  const fill = (line, vars) => String(line).replace(/\{(\w+)\}/g, (m, k) => (k in vars ? String(vars[k]) : m))

  // 默认台词（角色 character.json 的 lines 里写了同名键就用角色自己的）
  const LINES = {
    resetAnnounced: ['官方说了，要重置！预计 {when}。'],
    resetHigh: ['这次很有戏：{chance}%，{window}。'],
    resetMid: ['有点风声，{chance}% 吧，别太指望。', '有点风声……别太指望。'],
    resetLow: ['有动静，但不太像。'],
    resetNone: ['最近没风声。上次重置是 {since} 天前，平均 {avg} 天一次。'],
    resetQuiet: ['最近没风声。'],
    resetOffline: ['我这会儿连不上那个网站……'],
  }

  const api = { parse, notice, answer, toAnnounce, windowText, fill, fmt, LINES }
  root.CodexResets = api
  if (typeof module !== 'undefined') module.exports = api
})(typeof window !== 'undefined' ? window : globalThis)
```

注意：`resetMid` 的第二句没有 `{chance}`，没给概率（strong + null）时 index.html 只从不含 `{chance}` 的句子里挑（Task 2 的 `pickLine`）。

- [ ] **Step 4: 跑测试，确认通过**

Run: `node --test tests/codex_resets.test.js`
Expected: 9 个全 PASS

- [ ] **Step 5: 提交**

```bash
git add app/web/codex-resets.js tests/codex_resets.test.js
git commit -m "Codex 重置提醒：判断逻辑（codex-resets.js）和测试"
```

---

### Task 2: 桌宠网页接上（联网、额度下方一行、问她、主动说）

**Files:**
- Modify: `app/web/index.html`（script 引用区约 164–169 行；`LOCAL_LINES` 约 195 行之后；`renderQuota` 约 359 行；`setCharacter` 的切换回调约 445 行 `renderQuota()` 之后；`setQuota` 约 990 行）

**Interfaces:**
- Consumes：Task 1 的 `CodexResets.*`
- Produces（外壳调用）：`setCodexResets(on: boolean)`、`askReset()`

- [ ] **Step 1: 引用脚本、默认台词**

script 区 `<script src="stand-in.js"></script>` 下一行加：

```html
<script src="codex-resets.js"></script>
```

`LOCAL_LINES` 定义（`const LOCAL_LINES = { ... }`）结束后加一行：

```js
Object.assign(LOCAL_LINES, CodexResets.LINES)   // 重置提醒的默认台词（角色自己写了同名键就用角色的）
```

- [ ] **Step 2: 联网和状态**

在 `function setQuotaAggregate` 那一段（额度聚合）之前加：

```js
// ---- Codex 重置提醒（设置里默认关；数据来自第三方网站 codex-resets.com，判断见 codex-resets.js）----
// 只对 GPT 生效：官方提前宣布了重置，额度下方多一行；右键「会重置吗？」（外壳调 askReset）她按预测分档回答；
// 出现新的强预测 / 新宣布，她闲着时主动说一次。连不上那个网站时安静失败（国内网络常见）
const RESETS_URL = 'https://codex-resets.com/api/v1/status'
let resetsOn = false, resetsData = null, resetsAt = 0, resetsTimer = null, codexResetSeenAt = 0, resetsWaiting = false
function setCodexResets(on) {
  resetsOn = !!on
  clearTimeout(resetsTimer)
  if (!resetsOn) { resetsData = null; resetsAt = 0; renderQuota(); return }
  pollResets()
}
// 查一次：成功更新 resetsData；返回下次多久后再查（毫秒）。失败时保留上次查到的
async function fetchResets() {
  const ctl = new AbortController(), timer = setTimeout(() => ctl.abort(), 10000)
  try {
    const r = await fetch(RESETS_URL, { signal: ctl.signal, cache: 'no-store' })
    if (r.status === 429) return (parseInt(r.headers.get('Retry-After'), 10) || 1800) * 1000
    if (!r.ok) return 10 * 60 * 1000
    const d = CodexResets.parse(await r.json())
    if (!d) return 10 * 60 * 1000
    resetsData = d; resetsAt = Date.now()
    return 30 * 60 * 1000
  } catch { return 10 * 60 * 1000 } finally { clearTimeout(timer) }
}
async function pollResets() {
  clearTimeout(resetsTimer)
  const wait = await fetchResets()
  if (!resetsOn) return
  renderQuota()
  announceReset()
  resetsTimer = setTimeout(pollResets, wait)
}
function resetsTold(add) {
  let list = []
  try { list = JSON.parse(localStorage.getItem('crosspet-resets-told')) || [] } catch {}
  if (add) { list = [...list.filter(x => x !== add), add].slice(-50); try { localStorage.setItem('crosspet-resets-told', JSON.stringify(list)) } catch {} }
  return list
}
// 挑一句台词填空；没给概率时只挑不含 {chance} 的句子（都含就退回默认台词里不含的）
function pickLine(key, vars) {
  let list = linesOf(key) || ['']
  if (!('chance' in vars)) {
    const ok = list.filter(l => !l.includes('{chance}'))
    list = ok.length ? ok : (CodexResets.LINES[key] || ['']).filter(l => !l.includes('{chance}'))
    if (!list.length) list = ['']
  }
  return CodexResets.fill(pick(list), vars)
}
function tellReset() {
  const a = CodexResets.answer(resetsData, Date.now() / 1000, codexResetSeenAt)
  show(a.pose, true)
  say(pickLine(a.key, a.vars))
  clearTimeout(revertTimer)
  revertTimer = setTimeout(() => { setPose(restPose()); say('') }, 8000)
}
// 右键「会重置吗？」：先想一下；数据超过 5 分钟没更新就现查
async function askReset() {
  if (!resetsOn || charId !== 'gpt' || egg) return
  clearTimeout(revertTimer)
  show('thinking', true)
  say(pick(linesOf('thinking') || ['……']))
  if (Date.now() - resetsAt > 5 * 60 * 1000) {
    const wait = await fetchResets()
    clearTimeout(resetsTimer); resetsTimer = setTimeout(pollResets, wait)
    renderQuota()
  } else await new Promise(r => setTimeout(r, 1200))   // 数据是新的：也装作想一下
  if (charId === 'gpt' && !egg) tellReset()
}
// 主动说一次：只在她是 GPT、闲着时说；不是 GPT 就等 setCharacter 切到 GPT 时再调
function announceReset() {
  if (!resetsOn || charId !== 'gpt' || resetsWaiting) return
  const a = CodexResets.toAnnounce(resetsData, Date.now() / 1000, codexResetSeenAt, resetsTold())
  if (!a) return
  resetsWaiting = true
  let tries = 0
  const attempt = () => {
    if (!resetsOn || charId !== 'gpt') { resetsWaiting = false; return }
    if (pose === 'idle' && !egg && aiActivity() !== 'work' && $('bubble').classList.contains('hide')) {
      resetsWaiting = false; resetsTold(a.id); tellReset(); return
    }
    if (++tries < 30) setTimeout(attempt, 20000); else resetsWaiting = false
  }
  setTimeout(attempt, 3000)
}
// 额度下方那一行（只在官方宣布、还没执行时有）
function appendResetNotice(el) {
  if (!resetsOn || charId !== 'gpt') return
  const t = CodexResets.notice(resetsData, codexResetSeenAt)
  if (!t) return
  const line = document.createElement('div')
  line.className = 'notice'
  line.textContent = t
  if (el.className === 'agg') line.style.gridColumn = '1 / -1'   // 合并显示时是表格：这一行横跨整张表，放在最下面
  el.appendChild(line)
}
```

- [ ] **Step 3: 接到额度显示、额度重置检测、切换角色**

`renderQuota()` 里聚合分支的 `QuotaAggregate.render(...)` 之后、`return` 之前加 `appendResetNotice(el)`；函数最后一行 `el.className = q?.low ? 'low' : ''` 之后加 `appendResetNotice(el)`：

```js
    QuotaAggregate.render(el, QuotaAggregate.rows(quota, charId, ids, id => manifest?.characters?.[id]?.name || id))
    appendResetNotice(el)
    return
  }
  ...
  el.className = q?.low ? 'low' : ''
  appendResetNotice(el)
}
```

`setQuota` 里 `const ev = quotaEvent(id, q)` 下一行加：

```js
  if (ev === 'reset' && id === 'gpt') codexResetSeenAt = Date.now() / 1000   // 官方宣布之后真的重置了：额度下方那一行收起
```

`setCharacter` 切换回调里 `renderQuota()` 下一行加：

```js
    if (id === 'gpt') setTimeout(announceReset, 2000)   // 不是 GPT 时攒下的重置提醒，切过来再说
```

- [ ] **Step 4: 样式**

在 `#quota` 相关样式附近加（让这一行和额度数字区分开，不随 low 变红）：

```css
#quota .notice { color: var(--accent-ink, #8a5a2b); }
```

（先 `grep -n "#quota" app/web/index.html` 看现有写法；如果 `--accent-ink` 不存在，就用 `#quota.low` 以外的现有正文色，保持和额度同字号。）

- [ ] **Step 5: 浏览器里验证**

预览 `app/web/index.html`（和以前文字大小、插件提醒一样用浏览器打开页面，按页面顶部注释用 `init(...)` 喂一个 gpt 角色清单），在控制台：

```js
// 假接口：先有强预测
window.fetch = async () => ({ ok: true, status: 200, headers: { get: () => null }, json: async () => ({ data: { latest_reset: null, scheduled_reset: null, active_watch: { level: 'strong', reset_chance_percent: 80, forecast_window: 'next 48h', observed_at: new Date().toISOString(), expires_at: new Date(Date.now() + 864e5).toISOString(), text: '', source: { type: 'x_post', author: 'thsottiaux', url: 'u' } }, stats: { total: 61, last_reset_at: null, days_since_last: 1.4, avg_interval_days: 6.5 } } }) })
localStorage.removeItem('crosspet-resets-told')
setCharacter('gpt', true); setCodexResets(true)
```

期望：约 3 秒后她换 `happy` 并说「这次很有戏：80%，接下来 48 小时内。」；再 `setCodexResets(false); setCodexResets(true)` 不会再说（说过了）。
然后把假接口的 `scheduled_reset` 换成 `{ id: '9', status: 'scheduled', reset_type: 'regular', announced_at: new Date().toISOString(), scheduled_for: null, text: '', source: { type: 'x_post', author: 'thsottiaux', url: 'u' } }`，`setCodexResets(true)` → 额度下方出现「🔔 官方已宣布重置，还没生效」；`askReset()` → 先 thinking，再 `reset` 动作说「官方说了，要重置！预计 很快。」；`setCodexResets(false)` → 那一行立刻消失。
最后 `window.fetch = async () => { throw new Error('offline') }; resetsData = null; resetsAt = 0; askReset()` → 说「我这会儿连不上那个网站……」，回到待机。
换成别的角色（`setCharacter('claude')`）时 `askReset()` 不做任何事、额度下方没有那一行。

- [ ] **Step 6: 跑全部网页测试**

Run: `node --test tests/*.test.js tests/*.test.mjs`
Expected: 全 PASS（原 50 + 新 9）

- [ ] **Step 7: 提交**

```bash
git add app/web/index.html
git commit -m "Codex 重置提醒：桌宠网页（联网、额度下方一行、问她、主动说）"
```

---

### Task 3: GPT 两套台词

**Files:**
- Modify: `characters/gpt/character.json`（`lines` 里加键）
- Modify: `characters/gpt-onesie/character.json`（`lines` 里加键）

- [ ] **Step 1: 写台词**

`characters/gpt/character.json` 的 `lines` 加（御姐白龙，话少、冷静）：

```json
"resetAnnounced": ["官方说了，要重置。{when}。", "……他发推了，{when} 重置。"],
"resetHigh": ["{chance}%。{window}，可以期待一下。", "这次很有戏。{chance}%。"],
"resetMid": ["{chance}%……别太当真。", "有风声，没定数。"],
"resetLow": ["有动静，但不太像。", "……别等了，先写着。"],
"resetNone": ["没风声。上次是 {since} 天前，平均 {avg} 天一次。"],
"resetQuiet": ["没风声。"],
"resetOffline": ["那个网站……我这会儿连不上。"]
```

`characters/gpt-onesie/character.json` 的 `lines` 加（降智龙崽，迷糊、慢半拍、爱吃）：

```json
"resetAnnounced": ["重置卡要来了！{when}……那天吃什么好呢。", "官方说要重置！……是真的吗？是真的！"],
"resetHigh": ["{chance}%……好像很高？零食袋要满了～", "{window}……要重置了吧？吧？"],
"resetMid": ["{chance}%……一半一半？那就……半包零食。", "嗯……可能会？也可能不会……"],
"resetLow": ["有一点点动静……但是我闻不到饭香。", "……不太像。先省着吃吧。"],
"resetNone": ["没有风声……上次是 {since} 天前，平均 {avg} 天……要等好久。"],
"resetQuiet": ["没有风声……"],
"resetOffline": ["那个网站……连不上。是网断了还是我断了……"]
```

注意 `resetMid` 两套都各有一句不含 `{chance}`（没给概率时只挑这种）。

- [ ] **Step 2: 验证 JSON 有效、键齐全**

Run:
```bash
python3 -c "
import json
for c in ['gpt','gpt-onesie']:
    L=json.load(open(f'characters/{c}/character.json',encoding='utf-8'))['lines']
    ks=['resetAnnounced','resetHigh','resetMid','resetLow','resetNone','resetQuiet','resetOffline']
    assert all(k in L for k in ks), c
    assert any('{chance}' not in l for l in L['resetMid']), c
print('ok')"
```
Expected: `ok`

- [ ] **Step 3: 提交**

```bash
git add characters/gpt/character.json characters/gpt-onesie/character.json
git commit -m "Codex 重置提醒：GPT 原版和降智龙崽的台词"
```

---

### Task 4: 设置页开关和来源链接

**Files:**
- Modify: `app/web/settings.html`（`data-tab="quota"` 那张卡片，`quotaAggregate` 那一项之后）

- [ ] **Step 1: 加开关**

在 `<input type="checkbox" data-key="quotaAggregate">` 所在的 `</div></div>` 之后、卡片 `</div>` 之前加：

```html
  <div class="item"><div class="label">Codex 重置提醒<small>官方提前宣布重置时，GPT 的额度下方多一行；右键「会重置吗？」可以问她。数据来自第三方网站 <button class="link" data-action="openCodexResets">codex-resets.com</button>，根据官方推文整理，预测不是 OpenAI 的承诺</small></div>
    <div class="ctl"><label class="switch"><input type="checkbox" data-key="codexResets"><span></span></label></div></div>
```

（`data-key` 和 `data-action` 都由页面里现有的通用代码处理：开关发 `{cmd:'set', key:'codexResets', value}`，链接发 `{cmd:'action', name:'openCodexResets'}`。）

- [ ] **Step 2: 浏览器里看一眼**

预览 `app/web/settings.html`，点「额度」标签：最后一项是「Codex 重置提醒」，说明里的 codex-resets.com 是链接样式，没有撑破布局；窄窗口（340px）下换行正常。

- [ ] **Step 3: 提交**

```bash
git add app/web/settings.html
git commit -m "Codex 重置提醒：设置页开关（默认关）和来源链接"
```

---

### Task 5: macOS 外壳

**Files:**
- Modify: `app/mac/App+Dock.swift:15`（设置读取）
- Modify: `app/mac/App+Integrations.swift`（`case "quotaAggregate":` 附近、`case "openCharacters":` 附近）
- Modify: `app/mac/App+Settings.swift:132`（settingsState）
- Modify: `app/mac/App+Update.swift:226`（网页加载完的初始化）
- Modify: `app/mac/App+Menu.swift`（「召唤彩蛋」之后）
- Modify: `app/mac/App+StatusItem.swift:58`（菜单动作，和 `egg()` 放一起）
- Modify: `app/mac/App+Feedback.swift:102`（反馈里的开关状态）

- [ ] **Step 1: 改代码**

`App+Dock.swift`，`var quotaAggregate` 下一行：

```swift
    var codexResets: Bool { defaults.bool(forKey: "codexResets") }   // Codex 重置提醒（默认关，数据来自 codex-resets.com）
```

`App+Integrations.swift`，`case "quotaAggregate":` 那段之后：

```swift
            case "codexResets":
                let on = value as? Bool ?? false
                defaults.set(on, forKey: "codexResets"); js("setCodexResets(\(on))")
```

同文件 action 分支 `case "openCharacters": openCharacters()` 下一行：

```swift
            case "openCodexResets": NSWorkspace.shared.open(URL(string: "https://codex-resets.com")!)
```

`App+Settings.swift` settingsState 字典里 `"quotaAggregate": quotaAggregate,` 后加 `"codexResets": codexResets,`。

`App+Update.swift:226` 那行 js 末尾 `setQuotaAggregate(\(quotaAggregate))` 后加 `; setCodexResets(\(codexResets))`：

```swift
        js("setAuraMode(\(quote(auraMode))); setShowName(\(showName)); setEggsEnabled(\(eggsEnabled)); setPowerSave(\(powerSave)); setQuotaAggregate(\(quotaAggregate)); setCodexResets(\(codexResets))")
```

`App+Menu.swift`，`add(menu, "召唤彩蛋", #selector(egg))` 下一行：

```swift
        if codexResets && currentId == "gpt" { add(menu, "会重置吗？", #selector(askReset)) }   // 只有 GPT 有这个需求
```

`App+StatusItem.swift`，`@objc func egg()` 下一行：

```swift
    @objc func askReset() { js("askReset()") }
```

`App+Feedback.swift:102`，`额度合并 \(quotaAggregate ? "开" : "关")` 之后加 `，重置提醒 \(codexResets ? "开" : "关")`。

- [ ] **Step 2: 编译**

Run: `app/build.sh`
Expected: `已编译：…/build/CrossPet.app`，无 error

- [ ] **Step 3: 实测（在自己 Mac 上）**

按 `crosspet-probe-launches-real-app` 的规矩：先退出 ~/Applications 里的 CrossPet，再 `open build/CrossPet.app`，测完退出它、重新打开 ~/Applications/CrossPet.app，确认只剩一个进程且 `/tmp/crosspet/pet.pid` 对；开发版启动会把网页同步进 Application Support，测完用 `rsync -a --delete ~/Applications/CrossPet.app/Contents/Resources/web/ ~/Library/Application\ Support/CrossPet/web/` 恢复。
检查：设置里打开开关 → 右键（当前是 GPT）有「会重置吗？」，点了她先想再答（现在接口全空，应说 none 档带天数）；换成 Claude 右键没有这一项；关掉开关后 GPT 右键也没有。**特别确认 Mac 的网页（file://）能 fetch 到接口**：若她答 offline 档，在开发者控制台里看 `resetsData`，确认是跨域被拦还是网络问题，再决定改法（不要猜）。

- [ ] **Step 4: 提交**

```bash
git add app/mac/App+Dock.swift app/mac/App+Integrations.swift app/mac/App+Settings.swift app/mac/App+Update.swift app/mac/App+Menu.swift app/mac/App+StatusItem.swift app/mac/App+Feedback.swift
git commit -m "Codex 重置提醒：macOS 外壳（开关、右键「会重置吗？」、来源链接）"
```

---

### Task 6: Windows 外壳

**Files:**
- Modify: `app/windows/src/Pet.Dock.cs:37`
- Modify: `app/windows/src/Pet.Integrations.cs`（settingsState 约 141 行、`case "quotaAggregate":` 约 196 行、`case "openCharacters":` 约 247 行）
- Modify: `app/windows/src/Pet.cs:171`（NavigationCompleted 初始化脚本）
- Modify: `app/windows/src/Pet.Menu.cs:45`
- Modify: `app/windows/src/Pet.Feedback.cs:93`

- [ ] **Step 1: 改代码**

`Pet.Dock.cs`，`static bool QuotaAggregate` 下一行：

```csharp
        static bool CodexResets => Store.Flag("codexResets");   // Codex 重置提醒（默认关，数据来自 codex-resets.com）
```

`Pet.Integrations.cs` settingsState 里 `["quotaAggregate"] = QuotaAggregate,` 后加 `["codexResets"] = CodexResets,`；`case "quotaAggregate": …` 下一行：

```csharp
                        case "codexResets": Store.Settings["codexResets"] = on; Store.SaveSettings(); Js($"setCodexResets({(on ? "true" : "false")})"); break;
```

`case "openCharacters": Open(Store.Characters); break;` 下一行：

```csharp
                        case "openCodexResets": Open("https://codex-resets.com"); break;
```

`Pet.cs:171` 初始化脚本末尾 `setQuotaAggregate({(QuotaAggregate ? "true" : "false")})` 后加 `; setCodexResets({(CodexResets ? "true" : "false")})`。

`Pet.Menu.cs`，`menu.Items.Add(Item("召唤彩蛋", ...));` 下一行：

```csharp
            if (CodexResets && current == "gpt") menu.Items.Add(Item("会重置吗？", () => Js("askReset()")));   // 只有 GPT 有这个需求
```

`Pet.Feedback.cs:93`，`额度合并 {(QuotaAggregate ? "开" : "关")}` 之后加 `，重置提醒 {(CodexResets ? "开" : "关")}`。

- [ ] **Step 2: 编译、逻辑测试**

Run: `~/.dotnet/dotnet build app/windows/CrossPet.csproj -c Release -nologo -v quiet` 然后 `~/.dotnet/dotnet run --project tests/windows/LogicTests`
Expected: `0 Warning(s) 0 Error(s)`；`全部通过`

- [ ] **Step 3: 提交**

```bash
git add app/windows/src/Pet.Dock.cs app/windows/src/Pet.Integrations.cs app/windows/src/Pet.cs app/windows/src/Pet.Menu.cs app/windows/src/Pet.Feedback.cs
git commit -m "Codex 重置提醒：Windows 外壳（开关、右键「会重置吗？」、来源链接）"
```

---

### Task 7: 文档、设计修订、Windows 测试方案

**Files:**
- Modify: `docs/架构.md`（网页层文件列表、测试表）
- Modify: `docs/自定义角色.md`（台词键说明）
- Modify: `CHANGELOG.md`（未发布）
- Modify: `README.md`（额度相关段落提一句）
- Modify: `docs/superpowers/specs/2026-10-14-codex-resets-design.md`（加「修订」）
- Create: `~/Projects/crosspet-test/Windows-重置提醒测试.md`（本地，不入库）

- [ ] **Step 1: 改文档**

`docs/架构.md` 网页层那一格，在 `stand-in.js（…）、` 之后加 `codex-resets.js（Codex 重置提醒：接口数据怎么分档、要不要说）、`；测试表在「自定义角色响应其他 AI」那行后加：

```markdown
| Codex 重置提醒（网页层） | `node --test tests/codex_resets.test.js` |
```

`docs/自定义角色.md` 台词部分加一段：GPT 的 `lines` 可以写 `resetAnnounced` `resetHigh` `resetMid` `resetLow` `resetNone` `resetQuiet` `resetOffline`，占位符 `{when}` `{chance}` `{window}` `{since}` `{avg}`；`resetMid` 至少留一句不带 `{chance}`。

`CHANGELOG.md` 的「## 未发布」下加：

```markdown
- 新增：Codex 重置提醒（默认关，「设置… → 额度」里打开）：官方提前宣布重置时，GPT 的额度下方多一行「🔔 官方已宣布重置」；右键「会重置吗？」可以问她，她按预测的概率用不同的样子回答；出现很可能重置的新预测时，她会主动说一句（每条只说一次）。数据来自第三方网站 codex-resets.com，根据官方推文整理，预测不是 OpenAI 的承诺（感谢 @SkywingJam 的提案和演示，#16）
```

`README.md`：在介绍额度显示的地方加一句「可以打开 Codex 重置提醒（数据来自 codex-resets.com）」。先 `grep -n "额度" README.md` 找到合适段落。

设计文档末尾加：

```markdown
## 修订（写计划时）

- 合并显示各家额度时，那一行放在表格最下面、横跨整张表（不是插在 GPT 那一行下面）：GPT 是当前角色时本来就排第一行，放最下面实现简单、不改 quota-aggregate.js
- 反馈里只写「重置提醒 开 / 关」，不写最近一次查询结果：反馈是外壳同步拼的、读不到网页里的状态，为一行诊断加双向通道不值；连不上时问她会直接说
- 没有统计数据时 none 档用单独的台词键 `resetQuiet`
```

- [ ] **Step 2: Windows 测试方案（本地）**

`~/Projects/crosspet-test/Windows-重置提醒测试.md`：照 `Windows-1.3.5测试.md` 的格式写给 Codex，步骤：备份设置；设置「额度」页打开「Codex 重置提醒」，点 codex-resets.com 链接能用浏览器打开；当前 GPT 时右键有「会重置吗？」、点了她先想再答（读气泡文字）；换 Claude 没有这一项；关掉开关后 GPT 也没有；反馈里有「重置提醒 开」；断网（请用户关 Wi-Fi）后问她说「连不上」；恢复设置。

- [ ] **Step 3: 全部测试**

Run: `node --test tests/*.test.js tests/*.test.mjs && python3 -m unittest tests.integrate_test && ~/.dotnet/dotnet run --project tests/windows/LogicTests`
Expected: 全部通过

- [ ] **Step 4: 提交**

```bash
git add docs/架构.md docs/自定义角色.md CHANGELOG.md README.md docs/superpowers/specs/2026-10-14-codex-resets-design.md
git commit -m "Codex 重置提醒：文档、CHANGELOG、设计修订"
```
