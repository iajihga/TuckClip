import AppKit
import Carbon.HIToolbox
import Combine
import CoreGraphics
import Foundation

enum EventPostingAccessStatus: Equatable, Sendable {
    case granted
    case denied
}

enum CopyOnlyReason: Equatable, Sendable {
    case automaticPasteDisabled
    case eventPostingPermissionDenied
    case targetApplicationUnavailable
    case targetActivationFailed
    /// Another process replaced the selected payload before Command-V could be
    /// posted. No key event is sent because doing so would paste the wrong value.
    case clipboardContentsChanged
    case keyboardEventCreationFailed
}

enum PasteResult: Equatable, Sendable {
    case pasted
    /// No Command-V was sent. Most reasons leave the requested value available
    /// for manual paste; `clipboardContentsChanged` explicitly means it was
    /// superseded and must not be pasted.
    case copiedOnly(CopyOnlyReason)
    case failed(PasteboardWriteError)
    /// The owning UI request or panel session was superseded. Callers should not
    /// publish feedback for this stale operation.
    case cancelled
}

/// An immutable, Sendable identity for the app that owned focus when the panel
/// opened. Capturing a PID prevents slow image reads from silently retargeting a
/// paste if the panel is opened again before the first request completes.
struct PasteTargetSnapshot: Equatable, Sendable {
    let processIdentifier: pid_t?
    let bundleIdentifier: String?
    let launchDate: Date?
}

/// Injectable boundary around the macOS APIs that require a real login session.
/// Tests can exercise every fallback without changing focus or posting keys.
@MainActor
protocol PasteSystemControlling: AnyObject {
    func hasEventPostingAccess() -> Bool
    func requestEventPostingAccess() -> Bool
    func activateTarget(processIdentifier: pid_t) async -> Bool
    func postCommandV(
        to processIdentifier: pid_t,
        expectedPasteboardChangeCount: Int
    ) -> PasteEventPostResult
}

enum PasteEventPostResult: Equatable, Sendable {
    case posted
    case clipboardContentsChanged
    case failed
}

@MainActor
private final class MacPasteSystemController: PasteSystemControlling {
    private let activationTimeout: Duration = .milliseconds(800)
    private let activationPollInterval: Duration = .milliseconds(20)
    private let focusStabilizationDelay: Duration = .milliseconds(60)
    private let currentPasteboardChangeCount: () -> Int

    init(currentPasteboardChangeCount: @escaping () -> Int) {
        self.currentPasteboardChangeCount = currentPasteboardChangeCount
    }

    func hasEventPostingAccess() -> Bool {
        CGPreflightPostEventAccess()
    }

    func requestEventPostingAccess() -> Bool {
        CGRequestPostEventAccess()
    }

    func activateTarget(processIdentifier: pid_t) async -> Bool {
        guard !Task.isCancelled,
              let targetApplication = NSRunningApplication(
            processIdentifier: processIdentifier
        ), !targetApplication.isTerminated else {
            return false
        }

        if isFrontmost(processIdentifier: processIdentifier) {
            return true
        }

        // macOS 14+ foreground hand-off is cooperative. TuckClip owns activation
        // while its panel is key, so it must yield before the previous app asks to
        // become active again.
        NSApp.yieldActivation(to: targetApplication)
        guard targetApplication.activate(
            from: NSRunningApplication.current,
            options: []
        ) else {
            return false
        }

        let clock = ContinuousClock()
        let deadline = clock.now + activationTimeout
        while clock.now < deadline {
            guard !Task.isCancelled, !targetApplication.isTerminated else {
                return false
            }
            if isFrontmost(processIdentifier: processIdentifier) {
                do {
                    try await Task.sleep(for: focusStabilizationDelay)
                } catch {
                    return false
                }
                return !Task.isCancelled
                    && !targetApplication.isTerminated
                    && isFrontmost(processIdentifier: processIdentifier)
            }
            do {
                try await Task.sleep(for: activationPollInterval)
            } catch {
                return false
            }
        }
        return false
    }

    func postCommandV(
        to processIdentifier: pid_t,
        expectedPasteboardChangeCount: Int
    ) -> PasteEventPostResult {
        // Keep this check in the event-poster boundary as well as PasteService.
        // External processes can mutate the pasteboard between panel dismissal
        // and this synchronous call.
        guard currentPasteboardChangeCount() == expectedPasteboardChangeCount else {
            return .clipboardContentsChanged
        }
        guard !Task.isCancelled,
              hasEventPostingAccess(),
              isFrontmost(processIdentifier: processIdentifier),
              let source = CGEventSource(stateID: .combinedSessionState),
              let keyDown = CGEvent(
                  keyboardEventSource: source,
                  virtualKey: CGKeyCode(kVK_ANSI_V),
                  keyDown: true
              ), let keyUp = CGEvent(
                  keyboardEventSource: source,
                  virtualKey: CGKeyCode(kVK_ANSI_V),
                  keyDown: false
              ) else {
            return .failed
        }

        keyDown.flags = .maskCommand
        keyUp.flags = .maskCommand
        guard currentPasteboardChangeCount() == expectedPasteboardChangeCount else {
            return .clipboardContentsChanged
        }
        // The verified foreground app receives these through the same session
        // routing path as a physical key press, including Electron and web views.
        keyDown.post(tap: .cghidEventTap)
        keyUp.post(tap: .cghidEventTap)
        return .posted
    }

    private func isFrontmost(processIdentifier: pid_t) -> Bool {
        NSWorkspace.shared.frontmostApplication?.processIdentifier
            == processIdentifier
    }
}

/// Restores a history item and, when authorized, sends Command-V to the app that
/// was frontmost before TuckClip displayed its panel.
@MainActor
final class PasteService: ObservableObject {
    @Published private(set) var eventPostingAccessStatus: EventPostingAccessStatus
    @Published private(set) var targetApplicationName: String?

    private let writer: PasteboardWriter
    private weak var monitor: ClipboardMonitor?
    private let settings: AppSettings
    private let systemController: PasteSystemControlling
    private let currentPasteboardChangeCount: () -> Int
    private var targetApplication: NSRunningApplication?

    init(
        writer: PasteboardWriter? = nil,
        monitor: ClipboardMonitor? = nil,
        settings: AppSettings? = nil,
        systemController: PasteSystemControlling? = nil,
        currentPasteboardChangeCount: (() -> Int)? = nil
    ) {
        let resolvedChangeCount = currentPasteboardChangeCount
            ?? { NSPasteboard.general.changeCount }
        let resolvedSystemController = systemController ?? MacPasteSystemController(
            currentPasteboardChangeCount: resolvedChangeCount
        )
        self.writer = writer ?? PasteboardWriter()
        self.monitor = monitor
        self.settings = settings ?? .shared
        self.systemController = resolvedSystemController
        self.currentPasteboardChangeCount = resolvedChangeCount
        eventPostingAccessStatus = resolvedSystemController.hasEventPostingAccess()
            ? .granted
            : .denied
    }

    /// Call immediately before showing the TuckClip panel. A non-activating panel
    /// normally leaves this application frontmost, but retaining it removes any
    /// ambiguity when focus changes while the user searches.
    func rememberFrontmostApplication() {
        rememberTargetApplication(NSWorkspace.shared.frontmostApplication)
    }

    func rememberTargetApplication(_ application: NSRunningApplication?) {
        guard let application,
              application.processIdentifier != ProcessInfo.processInfo.processIdentifier,
              application.bundleIdentifier != Bundle.main.bundleIdentifier else {
            clearRememberedApplication()
            return
        }
        targetApplication = application
        targetApplicationName = application.localizedName
    }

    func clearRememberedApplication() {
        targetApplication = nil
        targetApplicationName = nil
    }

    func captureTargetSnapshot() -> PasteTargetSnapshot {
        PasteTargetSnapshot(
            processIdentifier: targetApplication?.processIdentifier,
            bundleIdentifier: targetApplication?.bundleIdentifier,
            launchDate: targetApplication?.launchDate
        )
    }

    func restoreRememberedApplicationFocus() {
        guard let targetApplication, !targetApplication.isTerminated else { return }
        let processIdentifier = targetApplication.processIdentifier
        Task { [weak self] in
            guard let self else { return }
            _ = await systemController.activateTarget(
                processIdentifier: processIdentifier
            )
        }
    }

    @discardableResult
    func refreshEventPostingAccess() -> EventPostingAccessStatus {
        let status: EventPostingAccessStatus = systemController.hasEventPostingAccess()
            ? .granted
            : .denied
        eventPostingAccessStatus = status
        return status
    }

    /// Requests macOS event-synthesis access. If macOS opens System Settings,
    /// approval is asynchronous and this call can still return `false`; the next
    /// selection will observe the newly granted state.
    @discardableResult
    func requestEventPostingAccess() -> Bool {
        let isGranted = systemController.requestEventPostingAccess()
        eventPostingAccessStatus = isGranted ? .granted : .denied
        return isGranted
    }

    /// The value is always copied first. Automatic paste is an optional second
    /// step and therefore has a safe, permission-free fallback.
    func paste(
        _ item: ClipItem,
        imageData: Data? = nil,
        asPlainText: Bool = false,
        requestPermissionIfNeeded: Bool = true,
        targetSnapshot: PasteTargetSnapshot? = nil,
        beforeSendingPaste: (() -> Bool)? = nil
    ) async -> PasteResult {
        guard !Task.isCancelled else { return .cancelled }
        let resolvedTargetApplication: NSRunningApplication?
        if let targetSnapshot {
            resolvedTargetApplication = targetSnapshot.processIdentifier.flatMap { processIdentifier in
                guard let application = NSRunningApplication(
                    processIdentifier: processIdentifier
                ), application.bundleIdentifier == targetSnapshot.bundleIdentifier,
                   application.launchDate == targetSnapshot.launchDate else {
                    return nil
                }
                return application
            }
        } else {
            resolvedTargetApplication = targetApplication
        }

        let receipt: PasteboardWriteReceipt
        do {
            let plainText = item.plainText
                ?? (item.filePaths.isEmpty ? nil : item.filePaths.joined(separator: "\n"))
            if asPlainText, let plainText {
                receipt = try writer.write(kind: .text, plainText: plainText)
            } else {
                receipt = try writer.write(item, imageData: imageData)
            }
        } catch let error as PasteboardWriteError {
            return .failed(error)
        } catch {
            return .failed(.pasteboardRejectedWrite)
        }

        // Keep the poller synchronized even though the internal marker also
        // prevents this write from entering history.
        monitor?.synchronizeChangeCount(receipt.changeCount)

        guard !Task.isCancelled else { return .cancelled }

        guard settings.automaticallyPasteAfterSelection else {
            return .copiedOnly(.automaticPasteDisabled)
        }

        if refreshEventPostingAccess() != .granted {
            let granted = requestPermissionIfNeeded && requestEventPostingAccess()
            guard granted else {
                return .copiedOnly(.eventPostingPermissionDenied)
            }
        }

        guard !Task.isCancelled else { return .cancelled }

        guard let resolvedTargetApplication,
              !resolvedTargetApplication.isTerminated else {
            return .copiedOnly(.targetApplicationUnavailable)
        }

        let processIdentifier = resolvedTargetApplication.processIdentifier
        let didActivate = await systemController.activateTarget(
            processIdentifier: processIdentifier
        )
        guard !Task.isCancelled else { return .cancelled }
        guard didActivate else {
            // Keep the panel visible when activation fails so the copy-only notice
            // is actually readable and the user can press Command-V manually.
            return .copiedOnly(.targetActivationFailed)
        }

        // Activation is asynchronous. A copy made by any other process during
        // that wait invalidates this receipt, so never send Command-V for it.
        guard currentPasteboardChangeCount() == receipt.changeCount else {
            return .copiedOnly(.clipboardContentsChanged)
        }

        // A successful activation normally hides a hides-on-deactivate panel.
        // This callback also orders it out deterministically and removes its local
        // key monitor before the synthesized shortcut enters the session stream.
        if let beforeSendingPaste, !beforeSendingPaste() {
            return .cancelled
        }
        guard !Task.isCancelled else { return .cancelled }

        switch systemController.postCommandV(
            to: processIdentifier,
            expectedPasteboardChangeCount: receipt.changeCount
        ) {
        case .posted:
            return .pasted
        case .clipboardContentsChanged:
            return .copiedOnly(.clipboardContentsChanged)
        case .failed:
            return .copiedOnly(.keyboardEventCreationFailed)
        }
    }
}
