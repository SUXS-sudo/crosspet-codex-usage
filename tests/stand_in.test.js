const test = require('node:test')
const assert = require('node:assert')
const { covers, quotaIds } = require('../app/web/stand-in.js')

test('covers：写了 "*" 响应所有别家 AI，但不包括她自己', () => {
  assert.strictEqual(covers('*', 'iris', 'claude'), true)
  assert.strictEqual(covers('*', 'iris', 'gpt'), true)
  assert.strictEqual(covers('*', 'iris', 'iris'), false)
})

test('covers：写了列表只响应列表里的', () => {
  assert.strictEqual(covers(['claude', 'gpt'], 'iris', 'claude'), true)
  assert.strictEqual(covers(['claude', 'gpt'], 'iris', 'deepseek'), false)
})

test('covers：容忍只写一个字符串；没写 / 写错了的当没写', () => {
  assert.strictEqual(covers('claude', 'iris', 'claude'), true)
  assert.strictEqual(covers('claude', 'iris', 'gpt'), false)
  for (const bad of [undefined, null, '', [], {}, 3, true]) assert.strictEqual(covers(bad, 'iris', 'claude'), false)
  assert.strictEqual(covers('*', 'iris', undefined), false)
})

test('quotaIds：reactTo 列表里的加上她自己；"*" 或没写就是全部', () => {
  const all = ['claude', 'deepseek', 'gemini', 'gpt', 'iris']
  assert.deepStrictEqual(quotaIds(['claude', 'gpt'], 'iris', all), ['claude', 'gpt', 'iris'])
  assert.deepStrictEqual(quotaIds('deepseek', 'iris', all), ['deepseek', 'iris'])
  assert.deepStrictEqual(quotaIds('*', 'iris', all), all)
  assert.deepStrictEqual(quotaIds(undefined, 'iris', all), all)
})
