import AppKit
import Combine
import Foundation

/// Polls `NSPasteboard.changeCount` because AppKit does not publish a general
/// pasteboard-changed notification.
@MainActor
final class ClipboardMonitor: NSObject, ObservableObject {
    @Published private(set) var isRunning = false
    @Published private(set) var accessStatus: PasteboardAccessStatus
    @Published private(set) var lastCapture: ClipboardCapture?

    /// Runs on the main actor after a value has been normalized and filtered.
    var onCapture: ((ClipboardCapture) -> Void)?

    private let settings: AppSettings
    private let reader: PasteboardReader
    private let workspaceNotificationCenter: NotificationCenter
    private var timer: Timer?
    private var lastObservedChangeCount: Int
    private var lastActiveApplication: NSRunningApplication?
    private var isObservingApplicationActivation = false

    init(
        settings: AppSettings? = nil,
        reader: PasteboardReader? = nil
    ) {
        let resolvedSettings = settings ?? .shared
        self.settings = resolvedSettings
        let resolvedReader = reader ?? PasteboardReader(settings: resolvedSettings)
        self.reader = resolvedReader
        workspaceNotificationCenter = NSWorkspace.shared.notificationCenter
        accessStatus = resolvedReader.accessStatus
        lastObservedChangeCount = resolvedReader.changeCount
        lastActiveApplication = NSWorkspace.shared.frontmostApplication
        super.init()
    }

    /// Starts observing future copies. Existing clipboard contents are not
    /// imported at launch.
    func start() {
        guard !isRunning else { return }
        lastObservedChangeCount = reader.changeCount
        lastActiveApplication = NSWorkspace.shared.frontmostApplication
        accessStatus = reader.accessStatus
        isRunning = true
        beginObservingApplicationActivation()
        scheduleTimer()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        endObservingApplicationActivation()
        isRunning = false
    }

    /// Applies a changed poll interval without treating the current clipboard
    /// value as a new capture.
    func reschedule() {
        guard isRunning else { return }
        timer?.invalidate()
        timer = nil
        lastObservedChangeCount = reader.changeCount
        scheduleTimer()
    }

    /// Exposed for deterministic tests and for an optional manual "capture now" action.
    func pollNow() {
        capturePasteboardChange(sourceApplication: lastActiveApplication)
    }

    /// Handles an application activation boundary while preserving the source of
    /// a copy made immediately before the switch. This is internal so the boundary
    /// behavior can be tested with a named pasteboard and no global side effects.
    func handleActivatedApplication(_ application: NSRunningApplication?) {
        guard isRunning else { return }

        // NSWorkspace posts didActivate after the frontmost application has
        // already changed. Consume a pending pasteboard value with the previously
        // cached app before replacing it, otherwise a password-manager copy can be
        // attributed to the destination app and bypass its exclusion rule.
        capturePasteboardChange(sourceApplication: lastActiveApplication)
        lastActiveApplication = application
    }

    private func capturePasteboardChange(sourceApplication: NSRunningApplication?) {
        accessStatus = reader.accessStatus

        let currentChangeCount = reader.changeCount
        guard currentChangeCount != lastObservedChangeCount else { return }

        // Advance before reading. Permission prompts and lazy pasteboard owners
        // can re-enter the run loop; the same change must never be imported twice.
        lastObservedChangeCount = currentChangeCount

        guard settings.isMonitoringEnabled,
              accessStatus.allowsBackgroundCapture,
              let capture = reader.readCapture(sourceApplication: sourceApplication) else {
            return
        }

        lastCapture = capture
        onCapture?(capture)
    }

    /// Must be called immediately after TuckClip writes to the pasteboard. The
    /// marker is a second line of defense; synchronizing the counter also avoids
    /// doing unnecessary parsing on the next timer tick.
    func synchronizeChangeCount(_ changeCount: Int? = nil) {
        lastObservedChangeCount = changeCount ?? reader.changeCount
    }

    private func scheduleTimer() {
        let interval = min(max(settings.pollingInterval, 0.1), 2.0)
        let newTimer = Timer(
            timeInterval: interval,
            target: self,
            selector: #selector(timerDidFire(_:)),
            userInfo: nil,
            repeats: true
        )
        newTimer.tolerance = min(0.1, interval * 0.25)
        RunLoop.main.add(newTimer, forMode: .common)
        timer = newTimer
    }

    private func beginObservingApplicationActivation() {
        guard !isObservingApplicationActivation else { return }
        workspaceNotificationCenter.addObserver(
            self,
            selector: #selector(workspaceDidActivateApplication(_:)),
            name: NSWorkspace.didActivateApplicationNotification,
            object: nil
        )
        isObservingApplicationActivation = true
    }

    private func endObservingApplicationActivation() {
        guard isObservingApplicationActivation else { return }
        workspaceNotificationCenter.removeObserver(
            self,
            name: NSWorkspace.didActivateApplicationNotification,
            object: nil
        )
        isObservingApplicationActivation = false
    }

    @objc private func workspaceDidActivateApplication(_ notification: Notification) {
        guard let application = notification.userInfo?[NSWorkspace.applicationUserInfoKey]
            as? NSRunningApplication else {
            return
        }
        handleActivatedApplication(application)
    }

    @objc private func timerDidFire(_ timer: Timer) {
        pollNow()
    }
}
