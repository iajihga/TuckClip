import AppKit
import Combine
import CoreGraphics
import Foundation

@MainActor
final class UISettingsStore: ObservableObject {
    private let appSettings: AppSettings

    @Published var recordingEnabled: Bool {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.isMonitoringEnabled != recordingEnabled {
                appSettings.isMonitoringEnabled = recordingEnabled
            }
            onRecordingChanged?(recordingEnabled)
        }
    }

    @Published var automaticPasteEnabled: Bool {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.automaticallyPasteAfterSelection != automaticPasteEnabled {
                appSettings.automaticallyPasteAfterSelection = automaticPasteEnabled
            }
            onAutomaticPasteChanged?(automaticPasteEnabled)
        }
    }

    @Published var capturesImages: Bool {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.capturesImages != capturesImages {
                appSettings.capturesImages = capturesImages
            }
            onCaptureImagesChanged?(capturesImages)
        }
    }

    @Published var retentionDays: Int {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.historyRetentionDays != retentionDays {
                appSettings.historyRetentionDays = retentionDays
            }
            onRetentionChanged?(retentionDays)
        }
    }

    @Published var maximumItemCount: Int {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.maximumHistoryItems != maximumItemCount {
                appSettings.maximumHistoryItems = maximumItemCount
            }
            onMaximumItemCountChanged?(maximumItemCount)
        }
    }

    @Published var excludedBundleIdentifiersText: String {
        didSet {
            guard excludedBundleIdentifiers != appSettings.excludedBundleIdentifiers else {
                exclusionCommitTask?.cancel()
                hasPendingExcludedBundleIdentifierChanges = false
                return
            }
            hasPendingExcludedBundleIdentifierChanges = true
            scheduleExcludedBundleIdentifiersCommit()
        }
    }
    // Match the exact capability used by PasteService. AX trust is broader and
    // can transiently disagree with CoreGraphics event-synthesis authorization.
    @Published private(set) var isAccessibilityTrusted = CGPreflightPostEventAccess()
    @Published var pasteboardAccessStatus: PasteboardAccessStatus = .unavailable
    @Published private(set) var globalHotKey: GlobalHotKey
    @Published private(set) var isRecordingHotKey = false
    @Published var hotKeyErrorDescription: String?
    @Published var storageErrorDescription: String?
    @Published var isStorageReadOnly = false

    var onRecordingChanged: ((Bool) -> Void)?
    var onAutomaticPasteChanged: ((Bool) -> Void)?
    var onCaptureImagesChanged: ((Bool) -> Void)?
    var onRetentionChanged: ((Int) -> Void)?
    var onMaximumItemCountChanged: ((Int) -> Void)?
    var onExcludedBundleIdentifiersChanged: ((Set<String>) -> Void)?
    var onRetryGlobalHotKey: (() -> Void)?
    var onGlobalHotKeyChanged: ((GlobalHotKey) -> Void)?

    private var exclusionCommitTask: Task<Void, Never>?
    private var hasPendingExcludedBundleIdentifierChanges = false
    private var isApplyingStateFromAppSettings = false

    init(
        appSettings: AppSettings,
        defaults _: UserDefaults = .standard
    ) {
        self.appSettings = appSettings

        recordingEnabled = appSettings.isMonitoringEnabled
        automaticPasteEnabled = appSettings.automaticallyPasteAfterSelection
        capturesImages = appSettings.capturesImages
        retentionDays = appSettings.historyRetentionDays
        maximumItemCount = appSettings.maximumHistoryItems
        globalHotKey = appSettings.globalHotKey

        let excluded = appSettings.excludedBundleIdentifiers.sorted()
        excludedBundleIdentifiersText = excluded.joined(separator: "\n")
        hotKeyErrorDescription = nil
        storageErrorDescription = nil
    }

    var excludedBundleIdentifiers: Set<String> {
        Set(
            excludedBundleIdentifiersText
                .components(separatedBy: CharacterSet.newlines.union(CharacterSet(charactersIn: ",")))
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { !$0.isEmpty }
        )
    }

    var clipboardAccessSummary: String {
        guard recordingEnabled else { return "可访问 · 记录已暂停" }
        switch pasteboardAccessStatus {
        case .unavailable:
            return "可访问 · 此系统无需单独授权"
        case .notDetermined:
            return "尚未确定 · 复制后由 macOS 询问"
        case .ask:
            return "每次询问 · 建议在系统设置中始终允许"
        case .alwaysAllow:
            return "始终允许 · 正在记录"
        case .alwaysDeny:
            return "已拒绝 · 无法记录"
        }
    }

    var isPasteboardAccessReady: Bool {
        pasteboardAccessStatus != .alwaysDeny
    }

    var recordingStatusTitle: String {
        recordingStatusTitle(for: recordingEnabled)
    }

    func recordingStatusTitle(for enabled: Bool) -> String {
        guard enabled else { return "已暂停" }
        if isStorageReadOnly { return "存储受保护" }
        if storageErrorDescription != nil { return "存储失败" }
        return isPasteboardAccessReady ? "记录中" : "权限受限"
    }

    var dataDirectory: URL {
        let applicationSupport = FileManager.default.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first!
        return applicationSupport.appendingPathComponent("TuckClip", isDirectory: true)
    }

    func commitExcludedBundleIdentifiers() {
        exclusionCommitTask?.cancel()
        let values = excludedBundleIdentifiers.sorted()
        hasPendingExcludedBundleIdentifierChanges = false
        appSettings.excludedBundleIdentifiers = Set(values)
        excludedBundleIdentifiersText = values.joined(separator: "\n")
        onExcludedBundleIdentifiersChanged?(Set(values))
    }

    func restoreDefaultExcludedBundleIdentifiers() {
        exclusionCommitTask?.cancel()
        hasPendingExcludedBundleIdentifierChanges = false
        appSettings.restoreDefaultExcludedApplications()
        excludedBundleIdentifiersText = appSettings.excludedBundleIdentifiers
            .sorted()
            .joined(separator: "\n")
        onExcludedBundleIdentifiersChanged?(appSettings.excludedBundleIdentifiers)
    }

    func retryGlobalHotKey() {
        onRetryGlobalHotKey?()
    }

    var hotKeyDisplayText: String { globalHotKey.displayText }

    var hotKeyStatusText: String {
        if isRecordingHotKey {
            return hotKeyErrorDescription ?? "请按新的组合键，按 Esc 取消"
        }
        return hotKeyErrorDescription ?? "\(hotKeyDisplayText) 已就绪"
    }

    func beginHotKeyRecording() {
        hotKeyErrorDescription = nil
        isRecordingHotKey = true
    }

    func cancelHotKeyRecording() {
        hotKeyErrorDescription = nil
        isRecordingHotKey = false
    }

    func captureHotKey(_ hotKey: GlobalHotKey) {
        do {
            let validated = try hotKey.validated()
            let shouldRetry = hotKeyErrorDescription != nil
            isRecordingHotKey = false
            hotKeyErrorDescription = nil
            if validated != globalHotKey || shouldRetry {
                onGlobalHotKeyChanged?(validated)
            }
        } catch {
            hotKeyErrorDescription = error.localizedDescription
            isRecordingHotKey = true
        }
    }

    func restoreDefaultHotKey() {
        onGlobalHotKeyChanged?(.defaultValue)
    }

    func synchronizeGlobalHotKey(_ hotKey: GlobalHotKey) {
        guard globalHotKey != hotKey else { return }
        globalHotKey = hotKey
    }

    func synchronizeFromAppSettings() {
        synchronizeRecordingEnabled(appSettings.isMonitoringEnabled)
        synchronizeAutomaticPasteEnabled(appSettings.automaticallyPasteAfterSelection)
        synchronizeMaximumItemCount(appSettings.maximumHistoryItems)
        synchronizeCapturesImages(appSettings.capturesImages)
        synchronizeRetentionDays(appSettings.historyRetentionDays)
        synchronizeGlobalHotKey(appSettings.globalHotKey)
        if !hasPendingExcludedBundleIdentifierChanges {
            let excluded = appSettings.excludedBundleIdentifiers.sorted()
            let text = excluded.joined(separator: "\n")
            if excludedBundleIdentifiersText != text {
                excludedBundleIdentifiersText = text
            }
        }
    }

    /// Applies the value carried by `AppSettings.$isMonitoringEnabled` without
    /// feeding it back into AppSettings while that `@Published` setter is still
    /// delivering its `willSet` notification.
    func synchronizeRecordingEnabled(_ enabled: Bool) {
        guard recordingEnabled != enabled else { return }
        applyStateFromAppSettings { recordingEnabled = enabled }
    }

    func synchronizeAutomaticPasteEnabled(_ enabled: Bool) {
        guard automaticPasteEnabled != enabled else { return }
        applyStateFromAppSettings { automaticPasteEnabled = enabled }
    }

    func synchronizeCapturesImages(_ enabled: Bool) {
        guard capturesImages != enabled else { return }
        applyStateFromAppSettings { capturesImages = enabled }
    }

    func synchronizeRetentionDays(_ days: Int) {
        guard retentionDays != days else { return }
        applyStateFromAppSettings { retentionDays = days }
    }

    func synchronizeMaximumItemCount(_ count: Int) {
        guard maximumItemCount != count else { return }
        applyStateFromAppSettings { maximumItemCount = count }
    }

    func refreshAccessibilityStatus() {
        isAccessibilityTrusted = CGPreflightPostEventAccess()
    }

    func requestAccessibilityAccess() {
        _ = CGRequestPostEventAccess()
        refreshAccessibilityStatus()
    }

    func openPrivacySettings() {
        guard let url = URL(
            string: "x-apple.systempreferences:com.apple.settings.PrivacySecurity"
        ) else { return }
        NSWorkspace.shared.open(url)
    }

    private func scheduleExcludedBundleIdentifiersCommit() {
        exclusionCommitTask?.cancel()
        exclusionCommitTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled else { return }
            self?.commitExcludedBundleIdentifiers()
        }
    }

    private func applyStateFromAppSettings(_ update: () -> Void) {
        isApplyingStateFromAppSettings = true
        defer { isApplyingStateFromAppSettings = false }
        update()
    }
}
