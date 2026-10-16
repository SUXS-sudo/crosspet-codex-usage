// Codex 重置提醒（设置里默认关）：数据来自第三方网站 codex-resets.com（根据 @thsottiaux 的推文整理，预测不是 OpenAI 的承诺）。
// 这里只做判断：接口的数据整理成三块（官方宣布 / 预测 / 统计）、额度下方显示什么、问她时回答哪一档、要不要主动说。
// 联网、显示、说话在 index.html；纯函数，网页和测试（tests/codex_resets.test.js）共用。设计见 docs/superpowers/specs/2026-10-14-codex-resets-design.md
;(function (root) {
  const sec = s => { const t = typeof s === 'string' ? Date.parse(s) : NaN; return isNaN(t) ? null : t / 1000 }

  function parse(j) {
    const d = j && typeof j === 'object' ? j.data : null
    if (!d || typeof d !== 'object') return null
    const s = d.scheduled_reset, w = d.active_watch, l = d.latest_reset, st = d.stats
    const announced = s && typeof s.id === 'string' && sec(s.announced_at) !== null
      ? { id: s.id, at: sec(s.announced_at), for: sec(s.scheduled_for), url: s.source?.url || '' } : null
    const watch = w && (w.level === 'elevated' || w.level === 'strong') && typeof w.observed_at === 'string'
      ? { id: w.observed_at, level: w.level, chance: Number.isInteger(w.reset_chance_percent) ? w.reset_chance_percent : null,
          window: typeof w.forecast_window === 'string' ? w.forecast_window : '', expires: sec(w.expires_at) || 0 } : null
    const latest = l && typeof l.id === 'string' ? { id: l.id, at: sec(l.announced_at) || 0 } : null
    const stats = st && typeof st.days_since_last === 'number' && typeof st.avg_interval_days === 'number'
      ? { since: st.days_since_last, avg: st.avg_interval_days } : null
    return { announced, watch, latest, stats }
  }

  // 还有效的宣布：站点没记为已执行，我们也没在宣布之后看到 Codex 额度重置（resetSeenAt：index.html 的 quotaEvent 判出 reset 的时间，秒）
  function liveAnnounced(d, resetSeenAt) {
    const a = d?.announced
    if (!a || d.latest?.id === a.id || (resetSeenAt && resetSeenAt > a.at)) return null
    return a
  }
  // 宣布过、而且已经执行了（站点记为已执行，或我们在宣布之后看到了 Codex 额度重置）
  const doneAnnounced = (d, resetSeenAt) => !!(d?.announced && !liveAnnounced(d, resetSeenAt))
  const liveWatch = (d, now) => (d?.watch && (!d.watch.expires || d.watch.expires > now) ? d.watch : null)

  const pad = n => String(n).padStart(2, '0')
  function fmt(s) { const t = new Date(s * 1000); return `${pad(t.getMonth() + 1)}/${pad(t.getDate())} ${pad(t.getHours())}:${pad(t.getMinutes())}` }

  function notice(d, resetSeenAt) {
    const a = liveAnnounced(d, resetSeenAt)
    if (!a) return ''
    return a.for ? `🔔 官方已宣布重置 · 预计 ${fmt(a.for)}` : '🔔 官方已宣布重置，还没生效'
  }

  function windowText(s) {
    let m = /^next\s+(\d+)\s*h(?:ours?)?$/i.exec(s || '')
    if (m) return `接下来 ${m[1]} 小时内`
    m = /^next\s+(\d+)\s*d(?:ays?)?$/i.exec(s || '')
    if (m) return `接下来 ${m[1]} 天内`
    return s || ''
  }

  // 专属立绘（docs/prompts/重置提醒/）；没画的在 index.html 的 POSE_FALLBACK 里退回 reset / happy / hum / sulk
  const POSE = { announced: 'reset_bell', high: 'reset_hope', mid: 'reset_maybe', low: 'reset_nope', none: 'reset_nope', offline: 'reset_nope' }
  function tierOf(d, now, resetSeenAt) {
    if (liveAnnounced(d, resetSeenAt)) return 'announced'
    const w = liveWatch(d, now)
    if (!w) return 'none'
    if (w.chance === null) return w.level === 'strong' ? 'mid' : 'low'
    return w.chance >= 70 ? 'high' : w.chance >= 30 ? 'mid' : 'low'
  }

  function answer(d, now, resetSeenAt) {
    if (!d) return { tier: 'offline', pose: POSE.offline, key: 'resetOffline', vars: {} }
    if (doneAnnounced(d, resetSeenAt)) return { tier: 'done', pose: 'reset', key: 'resetDone', vars: {} }   // 不能说「没风声」
    const tier = tierOf(d, now, resetSeenAt)
    if (tier === 'announced') { const a = liveAnnounced(d, resetSeenAt); return { tier, pose: POSE[tier], key: 'resetAnnounced', vars: { when: a.for ? fmt(a.for) : '很快' } } }
    if (tier === 'none') return d.stats
      ? { tier, pose: POSE.none, key: 'resetNone', vars: { since: d.stats.since.toFixed(1), avg: d.stats.avg.toFixed(1) } }
      : { tier, pose: POSE.none, key: 'resetQuiet', vars: {} }
    const w = liveWatch(d, now)
    const key = { high: 'resetHigh', mid: 'resetMid', low: 'resetLow' }[tier]
    const window = windowText(w.window)
    return { tier, pose: POSE[tier], key, vars: w.chance !== null ? { chance: w.chance, window } : { window } }
  }

  // 主动说：新宣布，或新的强预测（strong 或 ≥70%）；told 是已经说过的 id（宣布 'a:<id>'，预测 'w:<observed_at>'）
  function toAnnounce(d, now, resetSeenAt, told) {
    const a = liveAnnounced(d, resetSeenAt)
    if (a) return told.includes('a:' + a.id) ? null : { id: 'a:' + a.id }
    const w = liveWatch(d, now)
    if (!w || !(w.level === 'strong' || (w.chance ?? 0) >= 70)) return null
    return told.includes('w:' + w.id) ? null : { id: 'w:' + w.id }
  }

  const fill = (line, vars) => String(line).replace(/\{(\w+)\}/g, (m, k) => (k in vars ? String(vars[k]) : m))

  // 默认台词（角色 character.json 的 lines 里写了同名键就用角色自己的）
  const LINES = {
    resetAnnounced: ['官方说了，要重置！预计 {when}。'],
    resetDone: ['官方说的重置已经到了，额度满啦！'],
    resetHigh: ['这次很有戏：{chance}%，{window}。'],
    resetMid: ['有点风声，{chance}% 吧，别太指望。', '有点风声……别太指望。'],
    resetLow: ['有动静，但不太像。'],
    resetNone: ['最近没风声。上次重置是 {since} 天前，平均 {avg} 天一次。'],
    resetQuiet: ['最近没风声。'],
    resetOffline: ['我这会儿连不上那个网站……'],
  }

  const api = { parse, notice, answer, toAnnounce, windowText, fill, fmt, LINES }
  root.CodexResets = api
  if (typeof module !== 'undefined') module.exports = api
})(typeof window !== 'undefined' ? window : globalThis)
