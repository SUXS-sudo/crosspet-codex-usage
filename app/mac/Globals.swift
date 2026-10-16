// CrossPet：跨 AI 桌宠。透明无边框、永远在最上层、可拖动的小窗。
//
// 目录约定
//   ~/Library/Application Support/CrossPet/   运行目录（每次启动从 App 包里同步 web/ 和内置角色）
//       web/            画面（index.html + effects.js）
//       characters/     角色：<id>/character.json + 各姿态立绘（idle.webp、thinking-2.webp、idle-blink.webp …）
//                       自己加的角色放这里不会被覆盖
//       crosspet-hook.py  钩子脚本（Claude Code / Codex / Gemini CLI 用）
//   /tmp/crosspet/      各 AI 接入写的状态：<id>-state.json、<id>-quota.json
//
// 交互：单击 = 摸摸头；拖动 = 移动；右键 = 菜单（戳一下 / 彩蛋 / 换角色 / 改名 / 设置 / 退出）
import Carbon
import Cocoa
import ServiceManagement
import WebKit

let defaults = UserDefaults.standard
let stateDir = URL(fileURLWithPath: "/tmp/crosspet", isDirectory: true)
