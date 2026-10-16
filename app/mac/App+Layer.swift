import Carbon
import Cocoa
import ServiceManagement
import WebKit

extension App {
    // MARK: 显示层级
    // always：始终在最上层；ai：前台是 AI 应用、AI 等你回答、刚干完活时在最上层，其他时候是普通窗口（会被挡住）；
    // normal：普通窗口。全屏时隐藏用 macOS 自己的机制：不允许出现在全屏程序的空间里
    var layerMode: String { defaults.string(forKey: "layer") ?? "always" }
    var hideInFullscreen: Bool { defaults.object(forKey: "hideFullscreen") as? Bool ?? true }

    var aiActive: Bool {
        if let front = NSWorkspace.shared.frontmostApplication?.bundleIdentifier,
           appToCharacter[front] != nil || hostApp(front) != nil { return true }
        let now = Date()
        return lastPose.values.contains { p in
            let age = now.timeIntervalSince(p.at)
            if p.pose == "asking" { return age < 600 }                        // 等你回答：一直等到你回答
            return age < 8 && ["happy", "proud"].contains(p.pose)              // 刚干完活：浮上来一会儿
            // AI 在后台干活时不浮上来：选这个模式的人就是嫌挡路，干活时往往正是你切去忙别的
        }
    }

    func applyLayer() {
        // 每次轮询（0.3 秒）都看：程序一进全屏，窗口列表马上就是全屏后的大小，但「切换空间」的通知要等动画放完才来，
        // 只靠它和每秒一次的检查，她会在全屏动画上露一秒多（闪一下）
        if userHidden { return }   // 你用快捷键藏起来了：什么都别把她翻出来
        if Date().timeIntervalSince(fullscreenCheckedAt) >= 0.25 { checkFullscreen() }
        if hiddenForFullscreen { return }   // 全屏时收着：别被换层级的逻辑翻出来
        let top = layerMode == "always" || (layerMode == "ai" && aiActive)
        let level: NSWindow.Level = top ? .floating : .normal
        guard panel.level != level else { return }
        // 原本被别的窗口挡着、要浮上来：先藏起来，换完层级再淡入（没被挡住就直接换，免得闪一下）
        guard top, isCovered() else {
            panel.level = level
            if top { panel.orderFrontRegardless() } else { orderBelowFrontWindow() }
            return
        }
        web.evaluateJavaScript("sink()") { [weak self] _, _ in
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) {
                guard let self, !self.userHidden, self.layerMode == "always" || (self.layerMode == "ai" && self.aiActive) else { self?.js("surface()"); return }
                self.panel.level = .floating
                self.panel.orderFrontRegardless()
                self.js("surface()")
            }
        }
    }

}
