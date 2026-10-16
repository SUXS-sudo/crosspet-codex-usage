import Carbon
import Cocoa
import ServiceManagement
import WebKit

extension App {
    // MARK: 开发者控制台
    // 一个独立窗口（web/devconsole.html），直接调用桌宠页面里真实的动作代码：逐个检查姿态、跑场景、记反馈。
    var feedbackURL: URL { root.appendingPathComponent("dev-feedback.md") }

    func devJS(_ code: String) { devWeb?.evaluateJavaScript(code, completionHandler: nil) }

    @objc func openDevConsole() {
        if let w = devWindow {
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
            return
        }
        let config = WKWebViewConfiguration()
        config.userContentController.add(DevBridge(app: self, kind: .dev), name: "dev")
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1060, height: 760),
                         styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        w.title = "CrossPet 开发者控制台"
        w.isReleasedWhenClosed = false
        w.center()
        let v = WKWebView(frame: w.contentView!.bounds, configuration: config)
        v.autoresizingMask = [.width, .height]
        v.navigationDelegate = self
        v.loadFileURL(root.appendingPathComponent("web/devconsole.html"), allowingReadAccessTo: root)
        w.contentView!.addSubview(v)
        devWindow = w
        devWeb = v
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { [weak self] _ in
            guard let self else { return }
            self.pauseRealEvents = false
            self.js("devRelease()")
            self.devWeb = nil
            self.devWindow = nil
        }
        NSApp.activate(ignoringOtherApps: true)
        w.makeKeyAndOrderFront(nil)
    }

    func handleDev(_ body: Any) {
        guard let msg = body as? [String: Any], let cmd = msg["cmd"] as? String else { return }
        switch cmd {
        case "pet":       // 在桌宠页面里执行一段代码（控制台自己的页面发来的，只在本机）
            if let code = msg["code"] as? String { js(code) }
        case "status":    // 读桌宠当前状态，回给控制台
            web.evaluateJavaScript("JSON.stringify(devStatus())") { [weak self] r, _ in
                if let text = r as? String { self?.devJS("onPetStatus(\(text))") }
            }
        case "pause":
            pauseRealEvents = msg["on"] as? Bool ?? false
        case "save":
            let text = msg["text"] as? String ?? ""
            try? text.write(to: feedbackURL, atomically: true, encoding: .utf8)
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(text, forType: .string)
            devJS("onSaved(\(quote(feedbackURL.path)))")
        case "reveal":
            NSWorkspace.shared.activateFileViewerSelecting([feedbackURL])
        case "reload":
            web.reload()
        default:
            break
        }
    }
    func quote(_ s: String) -> String {
        let data = try? JSONSerialization.data(withJSONObject: [s])
        return String(data: data ?? Data(), encoding: .utf8).map { String($0.dropFirst().dropLast()) } ?? "\"\""
    }
}
