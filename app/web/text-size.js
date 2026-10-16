// 文字大小和人物大小分开调（设计：docs/superpowers/specs/2026-10-13-text-size-design.md）。两个平台共用这一份，规则只写一遍：
// - 锁着（默认）时文字 = 人物，和以前一样；设置里单独拖了文字滑块就解锁，各调各的
// - k = 文字 ÷ 人物：气泡、名牌、额度按 k 放大；字比人大时画布（网页的 CSS 尺寸）跟着变大给字腾地方，外壳照着设窗口
// - 画布只由这两个设置值决定，不看当前有没有气泡：窗口只在调设置时变，平时不动
;(function (root) {
  const BASE = { w: 260, h: 362, foot: 62 }   // 原来固定的画布：人物上方 59 给气泡，下方 62 给名牌和额度
  const clamp = v => { const n = Number(v); return Number.isFinite(n) ? Math.min(1.6, Math.max(0.6, n)) : 1 }

  function effectiveText(size, textSize, linked) {
    return linked === false ? clamp(textSize === undefined ? 1 : textSize) : clamp(size)
  }

  function layout(size, text, usageFooter = false) {
    const base = usageFooter ? { w: 320, h: 396, foot: 96 } : BASE
    const k = clamp(text) / clamp(size)
    if (k <= 1) return { k, ...base }   // 字小了本来就放得下；底栏也不缩，人物不往下掉
    return {
      k,
      w: Math.max(base.w, Math.round((base.w - 20) * k + 20)),   // 气泡最宽 228、额度最宽 236，放大后两边再留点
      h: base.h + Math.round((59 + base.foot) * (k - 1)),            // 气泡区 59 + 底栏 62，都按 k 放大
      foot: Math.round(base.foot * k),
    }
  }

  root.TextSize = { effectiveText, layout }
  if (typeof module !== 'undefined') module.exports = { effectiveText, layout }
})(typeof window !== 'undefined' ? window : globalThis)
