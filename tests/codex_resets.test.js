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
  assert.deepStrictEqual(a({ scheduled_reset: sched() }), { tier: 'announced', pose: 'reset_bell', key: 'resetAnnounced', vars: { when: '10/16 14:00' } })
  assert.deepStrictEqual(a({ active_watch: watch('strong', 85) }), { tier: 'high', pose: 'reset_hope', key: 'resetHigh', vars: { chance: 85, window: '接下来 48 小时内' } })
  assert.strictEqual(a({ active_watch: watch('elevated', 70) }).tier, 'high')
  assert.strictEqual(a({ active_watch: watch('elevated', 50) }).tier, 'mid')
  assert.strictEqual(a({ active_watch: watch('elevated', 50) }).pose, 'reset_maybe')
  assert.strictEqual(a({ active_watch: watch('elevated', 10) }).pose, 'reset_nope')
  assert.strictEqual(a({ active_watch: watch('elevated', 29) }).tier, 'low')
  assert.strictEqual(a({ active_watch: watch('strong', null) }).tier, 'mid')     // 没给概率但等级高：不能说「不太像」
  assert.strictEqual(a({ active_watch: watch('elevated', null) }).tier, 'low')
  assert.deepStrictEqual(a({}), { tier: 'none', pose: 'reset_nope', key: 'resetNone', vars: { since: '1.4', avg: '6.5' } })
  assert.strictEqual(R.answer(R.parse(base({ stats: null })), NOW, 0).key, 'resetQuiet')
  assert.deepStrictEqual(R.answer(null, NOW, 0), { tier: 'offline', pose: 'reset_nope', key: 'resetOffline', vars: {} })
  assert.strictEqual(R.answer(R.parse(base({ scheduled_reset: sched({ scheduled_for: null }) })), NOW, 0).vars.when, '很快')
})

test('answer：宣布之后已经重置了（站点记为已执行，或我们看到额度重置）→ 说已经重置了，不说没风声', () => {
  const done = R.parse(base({ scheduled_reset: sched(), latest_reset: { id: '999', reset_type: 'regular', announced_at: '2026-10-14T13:00:00.000Z', text: 't', source: { type: 'x_post', author: 'thsottiaux', url: 'u' } } }))
  assert.deepStrictEqual(R.answer(done, NOW, 0), { tier: 'done', pose: 'reset', key: 'resetDone', vars: {} })
  const seen = R.parse(base({ scheduled_reset: sched() }))
  assert.strictEqual(R.answer(seen, NOW, Date.parse('2026-10-14T13:00:00Z') / 1000).tier, 'done')
  assert.strictEqual(R.toAnnounce(seen, NOW, Date.parse('2026-10-14T13:00:00Z') / 1000, []), null)   // 已经重置了：不主动说
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
  for (const k of ['resetAnnounced', 'resetDone', 'resetHigh', 'resetMid', 'resetLow', 'resetNone', 'resetQuiet', 'resetOffline'])
    assert.ok(Array.isArray(R.LINES[k]) && R.LINES[k].length, k)
})
