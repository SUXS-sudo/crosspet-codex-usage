// 插件更新提醒：外壳跑接入工具的只读检查（integrate.py plugins），把各插件的三个版本交过来——
// CrossPet 自带的 / 装到对方那边的 / 对方正在跑的。这里决定要不要提醒、说什么。两个平台共用这一份。
// 只提醒、不自动更新：改别家配置一律让用户自己点「更新」（1.3.0 自动刷新 DeepSeek Harness 接入时删过用户的服务商配置）
;(function (root) {
  const parse = v => String(v).split(/[.-]/).map(n => parseInt(n, 10) || 0)
  function older(a, b) {   // a 比 b 旧
    const x = parse(a), y = parse(b)
    for (let i = 0; i < Math.max(x.length, y.length); i++) if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0)
    return false
  }

  // 返回 null（不用提醒）或 { kind: 'update' | 'restart', text（设置页那一行写的）, say（桌宠说的） }
  function advice(p) {
    if (!p || !p.bundled) return null
    if (!p.installed || older(p.installed, p.bundled)) return {
      kind: 'update',
      text: `插件有新版本（${p.installed ? p.installed + ' → ' : ''}${p.bundled}）：点右边的「更新」，再完全退出 ${p.label} 重新打开`,
      say: `${p.label} 插件有新版本啦，去「设置…→接入 AI」点一下更新吧～`,
    }
    if (p.running && older(p.running, p.installed)) return {
      kind: 'restart',
      text: `插件已经更新到 ${p.installed}，但 ${p.label} 还在跑旧的 ${p.running}：完全退出它（Windows 包括右下角托盘图标）再打开才生效`,
      say: `${p.label} 的插件更新好了，完全退出再打开一下才生效哦`,
    }
    return null
  }

  root.PluginUpdate = { advice }
  if (typeof module !== 'undefined') module.exports = { advice }
})(typeof window !== 'undefined' ? window : globalThis)
