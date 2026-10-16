// 「新功能」（What's New）：每版的新功能，写给普通用户看的短文案——只写新功能，不写修了什么（修复看 CHANGELOG）。
// 更新后桌宠头顶的卡片（index.html）数条数，设置里的「新功能」页（settings.html）列出来。两个平台共用，随包发布。
// 发版前在最上面加一版（没有新功能的版本不写）；go：「去设置 ›」跳到哪个标签页、哪个开关（key 是设置页里 data-key 的名字）。
// 设计见 docs/superpowers/specs/2026-10-16-whats-new-design.md；测试 tests/whats_new.test.js
;(function (root) {
  const LIST = [
    { version: '1.3.6', items: [
      { icon: '🔔', title: 'Codex 重置提醒', text: '官方提前宣布要重置 Codex 额度时，GPT 的额度下方会多一行；右键「会重置吗？」可以问她，她会按预测的概率回答。默认关着，在「额度」页打开。', go: { tab: 'quota', key: 'codexResets' } },
      { icon: '📱', title: 'GPT 的新动作', text: '被问「会重置吗？」时她会查手机，再摇铃铛、期待、怀疑或者摇头。原版和降智龙崽各一套。' },
      { icon: '✨', title: '「新功能」页', text: '就是这里。以后每次更新，新加的功能都会写在这一页，更新完她也会提醒你来看。' },
    ] },
    { version: '1.3.5', items: [
      { icon: '🔌', title: '插件更新提醒', text: 'DeepSeek Harness 的插件有新版本、或者更新了还没重开 Harness 时，「接入 AI」页会写明怎么做，她也会提一句。只提醒，不会自己改 Harness 的配置。', go: { tab: 'ai' } },
      { icon: '🎭', title: '自定义角色替别的 AI 演', text: '自己画的角色可以替 Claude、GPT 这些 AI 演干活的动作。在角色的 character.json 里写 reactTo，用法见仓库里的「自定义角色」文档。' },
    ] },
    { version: '1.3.4', items: [
      { icon: '🐉', title: '降智龙崽皮肤', text: 'GPT 换上龙崽连体服，变成天然呆。穿着它时有专属彩蛋：DeepSeek 端着白米饭路过，点一下龙崽就闻着饭香爬过来。在「角色」页给 GPT 换衣服。', go: { tab: 'character' } },
      { icon: '🔤', title: '文字大小单独调', text: '人调小了，名牌、气泡、额度的字也能保持看得清：单独拖「文字大小」滑块就和人物分开了，点锁链图标再锁回去。', go: { tab: 'look' } },
      { icon: '📊', title: '合并显示各家额度', text: '额度小窗把各家 AI 的额度排成一张表，每家一行，不用来回切角色。', go: { tab: 'quota', key: 'quotaAggregate' } },
    ] },
  ]

  // 去掉开头的 v 和 - 后面的部分：1.3.6-test.2 按 1.3.6 算
  const base = v => String(v || '').replace(/^v/i, '').split('-')[0]
  const parts = v => base(v).split('.').map(n => parseInt(n, 10) || 0)
  // a 比 b 新（只看正式版本号）
  function newer(a, b) {
    const x = parts(a), y = parts(b)
    for (let i = 0; i < Math.max(x.length, y.length); i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0)
    return false
  }
  // 比 last 新、不比 current 新的版本里的全部条目（从新到旧）；没有 last（新安装）就是空的
  function since(list, last, current) {
    if (!last) return []
    return list.filter(v => newer(v.version, last) && !newer(v.version, current)).flatMap(v => v.items)
  }
  // 「新功能」标签要不要亮红点
  const unseen = (list, seen, current) => since(list, seen, current).length > 0
  // 设置页列哪几版：不比 current 新的前 n 个
  const recent = (list, current, n = 5) => list.filter(v => !newer(v.version, current)).slice(0, n)

  const api = { LIST, base, newer, since, unseen, recent }
  root.WhatsNew = api
  if (typeof module !== 'undefined') module.exports = api
})(typeof window !== 'undefined' ? window : globalThis)
