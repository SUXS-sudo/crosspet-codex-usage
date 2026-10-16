// 额度聚合：设置里「合并显示各家额度」打开后，小窗不只显示当前角色的额度，而是每家一行排成表：
//   Claude   5h 98% · 07:50 │ 周 99%
//   GPT      5h 12% · 04:31 │ 周 78%
// 只做排版，不碰姿态：「累了」「额度紧张」仍然只看她自己的额度（见 index.html 的 setQuota / restPose）。
// 纯函数 rows() 和测试（tests/quota_aggregate.test.js）共用；render() 把结果画进小窗，index.html 和图例脚本共用。
;(function (root) {
  const SHORT = { '5小时': '5h', '本周': '周' }   // 窗口名缩写；别的（花费、Gemini 的模型池）照原样
  const LOW_LEFT = 10                             // 剩余 ≤ 10% 算紧张，和各家接入的 low 判断（用了 90%）一致

  // 一段额度原文（「5小时 剩余 98% · 07:50 重置」）拆成几块，只有百分比可以单独标红。
  // 认不出句式（余额、以后新增的写法）就整段原样显示，来源标了 low 才整段变红
  function compact(seg, sourceLow) {
    const m = /^(.+?) 剩余 (\d+)%(?: · (\S+) 重置)?(.*)$/.exec(seg)
    if (!m) return [{ text: seg, low: sourceLow }]
    const [, label, pct, time, tail] = m
    const parts = [{ text: (SHORT[label] || label) + ' ' }, { text: pct + '%', low: +pct <= LOW_LEFT }]
    if (time) parts.push({ text: ' · ' + time })
    if (tail.trim()) parts.push({ text: ' ' + tail.trim() })   // GPT 问不到时的「· 11:30 的数据」
    return parts
  }

  // quota：{ <角色 id>: { text, low } | null }；own：当前角色 id，排在最前；all：角色 id 的顺序；
  // nameOf：id → 名字。返回 [{ id, name, cells: [parts] }]，一家 AI 一行，每个额度窗口一格（原文用「｜」隔开）。
  // 没有额度（没接入、没打开开关、读不到）的 AI 直接跳过，不占行
  function rows(quota, own, all, nameOf) {
    const out = []
    // 其余按 id 字母序：不能指望 all 自带顺序——macOS 上角色清单是 Swift 字典序列化出来的，每次启动的顺序都不一样
    for (const id of [own, ...all.filter(i => i !== own).sort()]) {
      const q = quota[id]
      if (!q || !q.text) continue
      const cells = String(q.text).split(/\s*｜\s*/).filter(Boolean).map(seg => compact(seg, !!q.low))
      if (cells.length) out.push({ id, name: nameOf(id), cells })
    }
    return out
  }

  // 名字太长就截断：按显示宽度算（中日韩字符算 2，其余算 1），不靠 CSS 的省略号——表格里列宽会被挤到比名字还窄
  const NAME_WIDTH = 12, NAME_WIDTH_TIGHT = 9   // 表放不下、收紧了字号还不够时，名字再短一点
  const widthOf = ch => (ch.codePointAt(0) >= 0x2e80 ? 2 : 1)
  function shortName(name, max = NAME_WIDTH) {
    const chars = [...name]
    if (chars.reduce((w, c) => w + widthOf(c), 0) <= max) return name
    let w = 0, out = ''
    for (const c of chars) {
      if (w + widthOf(c) > max - 1) break   // 留一格给「…」
      w += widthOf(c); out += c
    }
    return out + '…'
  }

  const LOOSE_ROWS = 3   // 正常字号放得下的行数（见 render 里的说明）
  const isCrowded = rowCount => rowCount > LOOSE_ROWS

  // 画成对齐的表：名字一列、每个窗口一列，列宽自动撑开，不要求等宽字体。
  // 格子比别的行少的行，最后一格占满剩下的列（余额、Gemini 的长文本不撑歪别的行）
  function render(el, list, nameWidth = NAME_WIDTH) {
    el.replaceChildren()
    const n = Math.max(0, ...list.map(r => r.cells.length))
    el.className = 'agg'
    el.style.gridTemplateColumns = n ? `auto repeat(${n}, auto)` : ''
    const div = (cls, text) => { const d = document.createElement('div'); d.className = cls; if (text != null) d.textContent = text; return d }
    for (const r of list) {
      const name = div('qn', shortName(r.name, nameWidth))
      name.title = r.name   // 名字被截断时，鼠标停上去能看全
      el.append(name)
      r.cells.forEach((parts, i) => {
        const cell = div('qc')
        if (i === r.cells.length - 1 && r.cells.length < n) cell.style.gridColumn = `span ${n - r.cells.length + 1}`
        for (const p of parts) {
          const s = document.createElement('span'); s.textContent = p.text
          if (p.low) s.className = 'low'
          cell.append(s)
        }
        el.append(cell)
      })
    }
    // 收紧（字号 9px、行高 1.1）的两个条件：
    // 1. 行太多：底栏（#foot）留给额度小窗的高度预算只够 3 行正常字号，或 4 行收紧后的字号；
    //    4 家都有时不管宽不宽都要收紧，不然人物会被顶上去
    // 2. 窗口放不下（Gemini 的模型池名字长）：字和间距收紧一点，别把名字列挤没
    if (isCrowded(list.length) || el.scrollWidth > el.clientWidth + 1) el.classList.add('tight')
    // 收紧了还放不下（名字长 + 四家都有 + 微软雅黑比苹方宽）：名字再截短一点，重画一次
    if (el.scrollWidth > el.clientWidth + 1 && nameWidth > NAME_WIDTH_TIGHT) render(el, list, NAME_WIDTH_TIGHT)
  }

  root.QuotaAggregate = { rows, render, compact, shortName, isCrowded }
  if (typeof module !== 'undefined') module.exports = { rows, compact, shortName, isCrowded }
})(typeof window !== 'undefined' ? window : globalThis)
