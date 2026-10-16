const test = require('node:test')
const assert = require('node:assert')
const { rows, compact } = require('../app/web/quota-aggregate.js')

const names = { claude: 'Claude', gpt: 'GPT', deepseek: 'DeepSeek', gemini: 'Gemini' }
const nameOf = id => names[id] || id
const ALL = ['claude', 'deepseek', 'gemini', 'gpt']
const flat = parts => parts.map(p => p.text).join('')
const text = r => r.cells.map(flat)

test('一家一行；窗口名缩写，重置时间标在原来的那个窗口上', () => {
  const quota = { claude: { text: '5小时 剩余 98% · 07:50 重置 ｜ 本周 剩余 99%', low: false } }
  const [r] = rows(quota, 'claude', ALL, nameOf)
  assert.strictEqual(r.name, 'Claude')
  assert.deepStrictEqual(text(r), ['5h 98% · 07:50', '周 99%'])
})

test('当前角色排最前，其余按角色顺序', () => {
  const quota = {
    claude: { text: '5小时 剩余 80%', low: false },
    gpt: { text: '5小时 剩余 97%', low: false },
    deepseek: { text: '余额 ¥6.29', low: false },
  }
  assert.deepStrictEqual(rows(quota, 'gpt', ALL, nameOf).map(r => r.id), ['gpt', 'claude', 'deepseek'])
})

test('没有额度的 AI（没接入、没打开、读不到、空文本）不占行', () => {
  const quota = { claude: null, gpt: { text: '', low: false }, deepseek: { text: '余额 ¥1.00', low: false } }
  assert.deepStrictEqual(rows(quota, 'claude', ALL, nameOf).map(r => r.id), ['deepseek'])
  assert.deepStrictEqual(rows({}, 'claude', ALL, nameOf), [])
})

test('只有剩余 ≤ 10% 的百分比标红，别的部分不动', () => {
  const [r] = rows({ gpt: { text: '5小时 剩余 6% · 04:31 重置 ｜ 本周 剩余 78%', low: true } }, 'gpt', ALL, nameOf)
  const lows = r.cells.map(c => c.filter(p => p.low).map(p => p.text))
  assert.deepStrictEqual(lows, [['6%'], []])
  assert.strictEqual(compact('本周 剩余 10%', false).find(p => p.low).text, '10%')
  assert.strictEqual(compact('本周 剩余 11%', false).find(p => p.low), undefined)
})

test('认不出的句式（余额、模型池）原样显示，来源标了 low 才整段红', () => {
  assert.deepStrictEqual(compact('余额 ¥6.29', false), [{ text: '余额 ¥6.29', low: false }])
  assert.deepStrictEqual(compact('余额 ¥0.20', true), [{ text: '余额 ¥0.20', low: true }])
  assert.strictEqual(flat(compact('Gemini/Claude 剩余 80% · 14:20 重置', false)), 'Gemini/Claude 80% · 14:20')
})

test('GPT 问不到、用的是旧数时，「· 11:30 的数据」跟在后面', () => {
  const [r] = rows({ gpt: { text: '5小时 剩余 9% · 12:30 重置 · 11:30 的数据', low: true } }, 'gpt', ALL, nameOf)
  assert.deepStrictEqual(text(r), ['5h 9% · 12:30 · 11:30 的数据'])
})

test('当前角色不在列表里（自定义角色）：只排别家，不报错', () => {
  const quota = { claude: { text: '5小时 剩余 80%', low: false } }
  assert.deepStrictEqual(rows(quota, 'iris', ALL, nameOf).map(r => r.id), ['claude'])
})

test('名字太长按显示宽度截断（中日韩字符算 2 格），短名字原样', () => {
  const { shortName } = require('../app/web/quota-aggregate.js')
  assert.strictEqual(shortName('Claude'), 'Claude')
  assert.strictEqual(shortName('Geoffrey Everest Hinton'), 'Geoffrey Ev…')
  assert.strictEqual(shortName('亚历山大·冯·洪堡'), '亚历山大·冯…')
  assert.strictEqual(shortName('亚历山大·冯'), '亚历山大·冯')
})

test('顺序：当前角色置顶，其余按角色清单的顺序（角色目录名的字母序），不受名字影响', () => {
  const all = ['claude', 'deepseek', 'gemini', 'glm', 'gpt', 'iris']
  const quota = Object.fromEntries(all.map(id => [id, { text: '余额 ¥1.00', low: false }]))
  const renamed = id => ({ claude: '小蓝', gpt: 'Zed' }[id] || id)   // 改名不改变位置
  assert.deepStrictEqual(rows(quota, 'gemini', all, renamed).map(r => r.id), ['gemini', 'claude', 'deepseek', 'glm', 'gpt', 'iris'])
  assert.deepStrictEqual(rows(quota, 'iris', all, renamed).map(r => r.id), ['iris', 'claude', 'deepseek', 'gemini', 'glm', 'gpt'])
})

test('表放不下时名字可以截得更短（最大宽度作参数）', () => {
  const { shortName } = require('../app/web/quota-aggregate.js')
  assert.strictEqual(shortName('Geoffrey Everest Hinton', 9), 'Geoffrey…')
  assert.strictEqual(shortName('亚历山大·冯·洪堡', 9), '亚历山大…')
  assert.strictEqual(shortName('DeepSeek', 9), 'DeepSeek')
})

test('5 小时窗口是满的（100%）：不标重置时间，重置时间落在用得最多的周窗口上，日期格式 M/d', () => {
  // GPT 的真实形态：5h 没用过，「用得最多」的是周窗口，重置时间标在它身上
  const [r] = rows({ gpt: { text: '5小时 剩余 100% ｜ 本周 剩余 78% · 10/18 重置', low: false } }, 'gpt', ALL, nameOf)
  assert.deepStrictEqual(text(r), ['5h 100%', '周 78% · 10/18'])
  assert.deepStrictEqual(r.cells.map(c => c.filter(p => p.low).length), [0, 0])   // 100% 和 78% 都不紧张
})

test('顺序不依赖传进来的清单顺序（macOS 上清单每次启动的顺序都不同）', () => {
  const quota = Object.fromEntries(['claude', 'deepseek', 'gemini', 'gpt'].map(id => [id, { text: '余额 ¥1.00', low: false }]))
  const shuffled = ['gpt', 'gemini', 'claude', 'deepseek']
  assert.deepStrictEqual(rows(quota, 'iris', shuffled, nameOf).map(r => r.id), ['claude', 'deepseek', 'gemini', 'gpt'])
  assert.deepStrictEqual(rows(quota, 'gpt', shuffled, nameOf).map(r => r.id), ['gpt', 'claude', 'deepseek', 'gemini'])
})

test('4 行以上不管宽不宽都要收紧（底栏高度预算：3 行正常字号 / 4 行收紧后）', () => {
  const { isCrowded } = require('../app/web/quota-aggregate.js')
  assert.deepStrictEqual([0, 1, 2, 3].map(isCrowded), [false, false, false, false])
  assert.deepStrictEqual([4, 5, 6].map(isCrowded), [true, true, true])
})
