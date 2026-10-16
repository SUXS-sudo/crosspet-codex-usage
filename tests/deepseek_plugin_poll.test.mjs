// DeepSeek Harness 插件什么时候查余额：node --test tests/deepseek_plugin_poll.test.mjs
// 用假的 Harness（只记事件处理函数）和假的余额接口，数插件发了几次余额请求
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { apply } from '../integrations/deepseek/dsh-plugin-crosspet/lib/index.js'

async function run(config) {
  let calls = 0
  globalThis.fetch = async () => { calls++; return { ok: true, json: async () => ({ is_available: true, balance_infos: [{ currency: 'CNY', total_balance: '10.00' }] }) } }
  const handlers = {}, effects = []
  const ctx = { on(n, f) { (handlers[n] ||= []).push(f) }, inject() { throw new Error('none') }, effect(f) { effects.push(f) }, logger: { warn() {} },
    credentials: { resolve: async () => ({ value: 'sk-test' }) } }
  apply(ctx, { stateDir: mkdtempSync(join(tmpdir(), 'crosspet-dsh-')), autostart: false, ...config })
  const stops = effects.map((f) => f())
  const wait = (ms) => new Promise((r) => setTimeout(r, ms))
  const fire = async (n, ...a) => { for (const h of handlers[n] || []) await h(...a, async () => {}); await wait(50) }
  await wait(50); const counts = [calls]
  await fire('agent/pre-step', { messages: [{ role: 'user', content: 'hi' }] }); counts.push(calls)
  await fire('agent/turn-stopping'); counts.push(calls)
  stops.forEach((s) => typeof s === 'function' && s())
  return counts
}

test('启动时查一次，发消息、一轮结束时各查一次（充值后一说话就更新）', async () => {
  assert.deepEqual(await run({ balanceGapMs: 0 }), [1, 2, 3])
})

test('30 秒内查过就不再查（连着对话时不会问得太勤）', async () => {
  assert.deepEqual(await run({}), [1, 1, 1])
})
