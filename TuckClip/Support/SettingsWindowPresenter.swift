import AppKit
import SwiftUI

@MainActor
final class SettingsWindowPresenter: NSObject, NSWindowDelegate {
    private var windowController: NSWindowController?
    private let settings: UISettingsStore
    private let panelViewModel: ClipboardPanelViewModel

    init(settings: UISettingsStore, panelViewModel: ClipboardPanelViewModel) {
        self.settings = settings
        self.panelViewModel = panelViewModel
    }

    func show() {
        let controller = windowController ?? makeWindowController()
        windowController = controller
        controller.showWindow(nil)
        controller.window?.center()
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func makeWindowController() -> NSWindowController {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 640, height: 440),
            styleMask: [.titled, .closable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "TuckClip 设置"
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.contentView = NSHostingView(
            rootView: TuckClipSettingsView(
                settings: settings,
                panelViewModel: panelViewModel
            )
        )
        return NSWindowController(window: window)
    }
}
