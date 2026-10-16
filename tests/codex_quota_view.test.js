process.env.TZ = 'Asia/Shanghai'
const test = require('node:test')
const assert = require('node:assert')
const { view } = require('../app/web/codex-quota.js')

const NOW = Date.UTC(2026, 9, 12, 4, 0, 0) / 1000   // 2026-10-12 12:00 北京时间
const win = (used, mins, reset) => ({ usedPercent: used, windowDurationMins: mins, resetsAt: reset })

test('实时：两个窗口，重置时间标在用得最多的那个上', () => {
  const v = view({ source: 'live', at: NOW, rateLimits: { primary: win(3, 300, NOW + 3600), secondary: win(40, 10080, NOW + 3 * 86400) } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 97% ｜ 本周 剩余 60% · 10/15 重置')
  assert.strictEqual(v.low, false)
  assert.deepStrictEqual(v.windows, [{ label: '5小时', used: 3, reset: NOW + 3600 }, { label: '本周', used: 40, reset: NOW + 3 * 86400 }])
  assert.strictEqual(v.source, 'live')
})

test('问不到时保留最后一次的数，标上那是几点的数据', () => {
  const v = view({ source: 'stale', at: NOW - 1800, rateLimits: { primary: win(91, 300, NOW + 1800), secondary: null } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 9% · 12:30 重置 · 11:30 的数据')
  assert.strictEqual(v.low, true)
  assert.strictEqual(v.source, 'stale')
})

test('旧数据里已经过了重置时间的窗口显示 100%，不参与 low', () => {
  const v = view({ source: 'stale', at: NOW - 7200, rateLimits: { primary: win(95, 300, NOW - 10), secondary: win(20, 10080, NOW + 86400 * 2) } }, NOW)
  assert.strictEqual(v.text, '5小时 剩余 100% ｜ 本周 剩余 80% · 10/14 重置 · 10:00 的数据')
  assert.strictEqual(v.low, false)
})

test('小数百分比四舍五入；只有一个窗口', () => {
  const v = view({ source: 'live', at: NOW, rateLimits: { primary: win(12.6, 10080, NOW + 86400 * 5) } }, NOW)
  assert.strictEqual(v.text, '本周 剩余 87% · 10/17 重置')
})

test('没有可用窗口 / 空 payload 返回 null', () => {
  assert.strictEqual(view(null, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: null, secondary: null } }, NOW), null)
  assert.strictEqual(view({ source: 'live', rateLimits: { primary: { windowDurationMins: 300 } } }, NOW), null)
})

test('「今日」窗口标签', () => {
  const v = view({ source: 'live', at: NOW, rateLimits: { primary: win(0, 1440, 0) } }, NOW)
  assert.strictEqual(v.text, '今日 剩余 100%')
})
