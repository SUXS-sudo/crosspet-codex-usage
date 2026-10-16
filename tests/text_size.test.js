const test = require('node:test')
const assert = require('node:assert')
const { effectiveText, layout } = require('../app/web/text-size.js')

test('锁着（默认）：文字 = 人物；没存过 textLinked 也算锁着', () => {
  assert.equal(effectiveText(0.7, 1.3, true), 0.7)
  assert.equal(effectiveText(0.7, 1.3, undefined), 0.7)
  assert.equal(effectiveText(0.7, undefined, undefined), 0.7)
})

test('解锁：用文字大小', () => {
  assert.equal(effectiveText(0.7, 1.3, false), 1.3)
  assert.equal(effectiveText(0.7, undefined, false), 1)   // 解锁了但没存过文字大小：按 100%
})

test('超出范围收回 60%～160%，不是数字按 100%', () => {
  assert.equal(effectiveText(0.7, 5, false), 1.6)
  assert.equal(effectiveText(0.7, 0.1, false), 0.6)
  assert.equal(effectiveText(0.7, 'x', false), 1)
  assert.equal(effectiveText(9, 1, true), 1.6)
})

test('字和人一样大：画布不变（锁着的用户和以前一样）', () => {
  assert.deepEqual(layout(1, 1), { k: 1, w: 260, h: 362, foot: 62 })
  assert.deepEqual(layout(0.6, 0.6), { k: 1, w: 260, h: 362, foot: 62 })
  assert.deepEqual(layout(1.6, 1.6), { k: 1, w: 260, h: 362, foot: 62 })
})

test('字比人小：画布不变，底栏不缩', () => {
  const L = layout(1.6, 0.6)
  assert.ok(Math.abs(L.k - 0.375) < 1e-9)
  assert.deepEqual([L.w, L.h, L.foot], [260, 362, 62])
})

test('人 60%、字 100%：画布 420×443，底栏 103', () => {
  const L = layout(0.6, 1)
  assert.ok(Math.abs(L.k - 1 / 0.6) < 1e-9)
  assert.deepEqual([L.w, L.h, L.foot], [420, 443, 103])
})

test('最极端：人 60%、字 160%', () => {
  const L = layout(0.6, 1.6)
  assert.deepEqual([L.w, L.h, L.foot], [660, 564, 165])
})

test('只看两个数：同样的输入每次结果一样', () => {
  assert.deepEqual(layout(0.8, 1.2), layout(0.8, 1.2))
})

test('Windows 合并版：放大用量并给今日 Token 预留空间，不改变其他平台默认布局', () => {
  assert.deepEqual(layout(1, 1, true), { k: 1, w: 320, h: 396, foot: 96 })
  const L = layout(0.6, 1.6, true)
  assert.deepEqual([L.w, L.h, L.foot], [820, 654, 256])
  assert.equal(L.h - L.foot, 398)
  assert.deepEqual(layout(1, 1, false), { k: 1, w: 260, h: 362, foot: 62 })
})
