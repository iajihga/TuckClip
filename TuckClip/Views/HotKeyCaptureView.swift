import AppKit
import Carbon.HIToolbox
import SwiftUI

struct HotKeyCaptureView: NSViewRepresentable {
    let onCapture: (GlobalHotKey) -> Void
    let onCancel: () -> Void

    func makeNSView(context: Context) -> CaptureView {
        let view = CaptureView()
        view.onCapture = onCapture
        view.onCancel = onCancel
        return view
    }

    func updateNSView(_ view: CaptureView, context: Context) {
        view.onCapture = onCapture
        view.onCancel = onCancel
        view.focus()
    }

    @MainActor
    final class CaptureView: NSView {
        var onCapture: ((GlobalHotKey) -> Void)?
        var onCancel: (() -> Void)?

        override var acceptsFirstResponder: Bool { true }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            focus()
        }

        func focus() {
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                window?.makeFirstResponder(self)
            }
        }

        override func keyDown(with event: NSEvent) {
            let modifiers = Self.carbonModifiers(from: event.modifierFlags)
            if Int(event.keyCode) == kVK_Escape, modifiers == 0 {
                onCancel?()
                return
            }

            onCapture?(GlobalHotKey(
                keyCode: UInt32(event.keyCode),
                modifiers: modifiers
            ))
        }

        private static func carbonModifiers(
            from flags: NSEvent.ModifierFlags
        ) -> UInt32 {
            var result: UInt32 = 0
            if flags.contains(.command) { result |= UInt32(cmdKey) }
            if flags.contains(.option) { result |= UInt32(optionKey) }
            if flags.contains(.control) { result |= UInt32(controlKey) }
            if flags.contains(.shift) { result |= UInt32(shiftKey) }
            return result
        }
    }
}
