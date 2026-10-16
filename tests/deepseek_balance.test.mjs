// DeepSeek 余额汇总（插件 lib/balance.js）：node --test tests/deepseek_balance.test.mjs
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { summarize, lowThresholds } from '../integrations/deepseek/dsh-plugin-crosspet/lib/balance.js'

const usd = { currency: 'USD', amount: '4.97' }, cny = { currency: 'CNY', amount: '10.00' }

test('只有一种币种：和以前写的一模一样（再加上 balances）', () => {
  assert.deepEqual(summarize([cny]), { text: '余额 ¥10.00', balance: 10, balances: { CNY: 10 }, low: false })
  assert.deepEqual(summarize([{ currency: 'CNY', amount: '3.2' }]).low, true)
})

test('两种币种：顺序对调结果完全一样（#10 实测接口顺序会变）', () => {
  const a = summarize([usd, cny]), b = summarize([cny, usd])
  assert.deepEqual(a, b)
  assert.equal(a.text, '余额 ¥10.00｜$4.97')
  assert.equal(a.balance, 10)
  assert.deepEqual(a.balances, { CNY: 10, USD: 4.97 })
})

test('余额低按币种门槛：$4.97 不算低（以前被当成 < 5）', () => {
  assert.equal(summarize([usd]).low, false)
  assert.equal(summarize([{ currency: 'USD', amount: '0.5' }]).low, true)
})

test('所有币种都低才算低', () => {
  assert.equal(summarize([{ currency: 'CNY', amount: '1' }, usd]).low, false)
  assert.equal(summarize([{ currency: 'CNY', amount: '1' }, { currency: 'USD', amount: '0.3' }]).low, true)
})

test('账号不可用（is_available=false）一律算低', () => {
  assert.equal(summarize([cny, usd], { available: false }).low, true)
})

test('同一币种的现金和赠送钱包加在一起', () => {
  const q = summarize([{ currency: 'CNY', amount: 3 }, { currency: 'CNY', amount: '2.5' }])
  assert.equal(q.text, '余额 ¥5.50')
  assert.equal(q.low, false)
})

test('余额为 0 的币种不显示；全是 0 时显示人民币', () => {
  assert.equal(summarize([{ currency: 'CNY', amount: '0.00' }, usd]).text, '余额 $4.97')
  assert.equal(summarize([{ currency: 'USD', amount: '0' }, { currency: 'CNY', amount: '0' }]).text, '余额 ¥0.00')
})

test('没有任何币种返回 null（插件记成 no-balance）', () => {
  assert.equal(summarize([]), null)
  assert.equal(summarize(undefined), null)
  assert.equal(summarize([{ amount: '3' }]), null)
})

test('门槛配置：数字是人民币门槛（老写法），对象按币种', () => {
  assert.deepEqual(lowThresholds(undefined), { CNY: 5, USD: 0.7 })
  assert.deepEqual(lowThresholds(10), { CNY: 10, USD: 0.7 })
  assert.deepEqual(lowThresholds({ USD: 2 }), { CNY: 5, USD: 2 })
  assert.equal(summarize([usd], { low: lowThresholds({ USD: 5 }) }).low, true)
})
