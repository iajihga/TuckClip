import AppKit
import QuartzCore
import SwiftUI

@MainActor
final class ClipboardPanelController: NSObject, NSWindowDelegate {
    let panel: NSPanel

    var onWillShow: (() -> Void)?
    var onCancel: (() -> Void)?
    var isVisible: Bool { panel.isVisible }

    private let viewModel: ClipboardPanelViewModel
    private let settings: UISettingsStore
    private var localKeyMonitor: Any?

    init(
        viewModel: ClipboardPanelViewModel,
        settings: UISettingsStore
    ) {
        self.viewModel = viewModel
        self.settings = settings

        let panel = TuckClipPanel(
            contentRect: NSRect(x: 0, y: 0, width: 1_080, height: 390),
            styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        self.panel = panel
        super.init()

        panel.delegate = self
        panel.level = .floating
        panel.collectionBehavior = [
            .canJoinAllSpaces,
            .fullScreenAuxiliary,
            .transient,
            .ignoresCycle
        ]
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = true
        panel.isMovableByWindowBackground = true
        panel.isReleasedWhenClosed = false
        panel.animationBehavior = .none
#if DEBUG
        panel.sharingType = .readOnly
#else
        panel.sharingType = .none
#endif
        panel.title = "TuckClip"

        panel.contentView = NSHostingView(
            rootView: ClipboardPanelView(
                viewModel: viewModel,
                settings: settings,
                dismiss: { [weak self] in self?.hide() }
            )
        )
    }

    func toggle() {
        isVisible ? hide() : show()
    }

    func show() {
        guard !isVisible else {
            NotificationCenter.default.post(name: .tuckClipFocusSearch, object: nil)
            return
        }

        viewModel.beginPanelSession()
        onWillShow?()
        viewModel.ensureSelection()
        positionPanel()
        installKeyMonitor()

        let destinationFrame = panel.frame
        var initialFrame = destinationFrame
        initialFrame.origin.y -= 14
        panel.setFrame(initialFrame, display: false)
        panel.alphaValue = 0
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        NotificationCenter.default.post(name: .tuckClipFocusSearch, object: nil)

        if NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            panel.setFrame(destinationFrame, display: true)
            panel.alphaValue = 1
        } else {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.17
                context.timingFunction = CAMediaTimingFunction(name: .easeOut)
                self.panel.animator().setFrame(destinationFrame, display: true)
                self.panel.animator().alphaValue = 1
            }
        }
    }

    func hide(
        animated: Bool = true,
        restorePreviousApplication: Bool = true,
        cancelPendingPaste: Bool = true
    ) {
        if cancelPendingPaste {
            viewModel.cancelPendingPaste()
        }
        removeKeyMonitor()
        guard isVisible else { return }

        guard animated, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else {
            panel.orderOut(nil)
            panel.alphaValue = 1
            if restorePreviousApplication {
                onCancel?()
            }
            return
        }

        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.12
            panel.animator().alphaValue = 0
        } completionHandler: { [weak self] in
            Task { @MainActor [weak self] in
                guard let self else { return }
                self.panel.orderOut(nil)
                self.panel.alphaValue = 1
                if restorePreviousApplication {
                    self.onCancel?()
                }
            }
        }
    }

    func hideForPaste() {
        hide(
            animated: false,
            restorePreviousApplication: false,
            cancelPendingPaste: false
        )
    }

    func windowWillClose(_ notification: Notification) {
        viewModel.cancelPendingPaste()
        removeKeyMonitor()
    }

    private func positionPanel() {
        let mouseLocation = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouseLocation, $0.frame, false) }
            ?? NSScreen.main
            ?? NSScreen.screens.first
        guard let screen else { return }

        let visibleFrame = screen.visibleFrame
        let width = min(1_120, max(560, visibleFrame.width - 32))
        let height = min(390, max(330, visibleFrame.height * 0.42))
        let origin = NSPoint(
            x: visibleFrame.midX - width / 2,
            y: visibleFrame.minY + 12
        )
        panel.setFrame(NSRect(origin: origin, size: NSSize(width: width, height: height)), display: false)
    }

    private func installKeyMonitor() {
        guard localKeyMonitor == nil else { return }
        localKeyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.panel.isKeyWindow else { return event }
            return self.handleKeyDown(event) ? nil : event
        }
    }

    private func removeKeyMonitor() {
        if let localKeyMonitor {
            NSEvent.removeMonitor(localKeyMonitor)
            self.localKeyMonitor = nil
        }
    }

    private func handleKeyDown(_ event: NSEvent) -> Bool {
        let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let hasCommand = modifiers.contains(.command)
        let fieldEditor = panel.firstResponder as? NSTextView
        let hasMarkedText = fieldEditor?.hasMarkedText() == true
        let navigationModifiers = modifiers.subtracting([.capsLock, .numericPad, .function])

        if hasMarkedText, [36, 76, 123, 124, 125, 126, 53].contains(event.keyCode) {
            return false
        }

        if !hasMarkedText,
           hasCommand,
           modifiers.contains(.shift),
           [51, 117].contains(event.keyCode) {
            viewModel.deleteSelected()
            return true
        }

        switch event.keyCode {
        case 123 where fieldEditor == nil && navigationModifiers.isEmpty: // Left arrow
            viewModel.moveSelection(by: -1)
            return true
        case 124 where fieldEditor == nil && navigationModifiers.isEmpty: // Right arrow
            viewModel.moveSelection(by: 1)
            return true
        case 126 where fieldEditor != nil && navigationModifiers.isEmpty: // Up arrow
            viewModel.moveSelection(by: -1)
            return true
        case 125 where fieldEditor != nil && navigationModifiers.isEmpty: // Down arrow
            viewModel.moveSelection(by: 1)
            return true
        case 36, 76: // Return and keypad Enter
            viewModel.pasteSelected(asPlainText: hasCommand)
            return true
        case 53: // Escape
            hide()
            return true
        default:
            break
        }

        guard hasCommand,
              let character = event.charactersIgnoringModifiers?.lowercased() else {
            return false
        }

        if character == "d" {
            viewModel.togglePinSelected()
            return true
        }

        if let number = Int(character), (1 ... 9).contains(number) {
            viewModel.selectVisibleItem(at: number - 1)
            viewModel.pasteSelected()
            return true
        }

        return false
    }
}

private final class TuckClipPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}
