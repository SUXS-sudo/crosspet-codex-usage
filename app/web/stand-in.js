// 自定义角色响应其他 AI 的事件：character.json 里写 "reactTo": ["claude", "gpt"]（或 "*" 代表所有 AI），
// 这个角色在场时，这些 AI 的工作状态、多会话、庆祝（reset / 吃饭）也由她来演，不用等换成那个 AI 自己的角色。
// 内置角色不受影响，照常响应自己的 AI。
//
// 代码里叫“代演 / standIn / standsIn”：是 current 通道（没有对应角色的模型由当前角色来演）的通用化。
// 对用户只说“响应 / 来演”，不说“代演”，免得被理解成别的角色被顶掉了。
// 只在她是当前角色时生效，不会自己切出去——想让她常驻，要关掉「跟随前台应用」（Windows 还有「跟随 AI 工作事件」）。
// 纯函数，网页和测试（tests/stand_in.test.js）共用。
;(function (root) {
  // reactTo 可以写 "*"、["claude", "gpt"]，也容忍只写一个 "claude"；别的写法当没写
  function listOf(spec) {
    if (spec === '*') return '*'
    if (Array.isArray(spec)) return spec.filter(x => typeof x === 'string')
    return typeof spec === 'string' && spec ? [spec] : null
  }
  // spec：她的 reactTo 字段；own：她自己的角色 id；host：事件来自哪个 AI（角色 id）
  function covers(spec, own, host) {
    const list = listOf(spec)
    if (!list || typeof host !== 'string' || host === own) return false
    return list === '*' || list.includes(host)
  }
  // 额度小窗里该显示哪些 AI 的额度：她自己加上她响应的。没写或不认识就原样返回全部
  function quotaIds(spec, own, all) {
    const list = listOf(spec)
    if (!list || list === '*') return all
    return all.filter(id => id === own || list.includes(id))
  }
  root.StandIn = { covers, quotaIds }
  if (typeof module !== 'undefined') module.exports = { covers, quotaIds }
})(typeof window !== 'undefined' ? window : globalThis)
