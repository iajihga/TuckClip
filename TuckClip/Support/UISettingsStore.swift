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
    @Published var appLanguage: AppLanguage {
        didSet {
            if !isApplyingStateFromAppSettings,
               appSettings.appLanguage != appLanguage {
                appSettings.appLanguage = appLanguage
            }
            onLanguageChanged?(appLanguage)
        }
    }
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
    var onLanguageChanged: ((AppLanguage) -> Void)?

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
        appLanguage = appSettings.appLanguage

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

    func localized(_ source: String) -> String {
        L10n.text(source, language: appLanguage)
    }

    func localizedFormat(_ source: String, _ arguments: CVarArg...) -> String {
        let format = L10n.text(source, language: appLanguage)
        let localeIdentifier = appLanguage.resolved() == .simplifiedChinese
            ? "zh-Hans"
            : "en"
        return String(
            format: format,
            locale: Locale(identifier: localeIdentifier),
            arguments: arguments
        )
    }

    var clipboardAccessSummary: String {
        guard recordingEnabled else { return localized("可访问 · 记录已暂停") }
        switch pasteboardAccessStatus {
        case .unavailable:
            return localized("可访问 · 此系统无需单独授权")
        case .notDetermined:
            return localized("尚未确定 · 复制后由 macOS 询问")
        case .ask:
            return localized("每次询问 · 建议在系统设置中始终允许")
        case .alwaysAllow:
            return localized("始终允许 · 正在记录")
        case .alwaysDeny:
            return localized("已拒绝 · 无法记录")
        }
    }

    var isPasteboardAccessReady: Bool {
        pasteboardAccessStatus != .alwaysDeny
    }

    var recordingStatusTitle: String {
        recordingStatusTitle(for: recordingEnabled)
    }

    func recordingStatusTitle(
        for enabled: Bool,
        language: AppLanguage? = nil
    ) -> String {
        let preference = language ?? appLanguage
        guard enabled else { return L10n.text("已暂停", language: preference) }
        if isStorageReadOnly { return L10n.text("存储受保护", language: preference) }
        if storageErrorDescription != nil { return L10n.text("存储失败", language: preference) }
        return L10n.text(
            isPasteboardAccessReady ? "记录中" : "权限受限",
            language: preference
        )
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
            return hotKeyErrorDescription ?? localized("请按新的组合键，按 Esc 取消")
        }
        return hotKeyErrorDescription ?? localizedFormat("%@ 已就绪", hotKeyDisplayText)
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
        synchronizeAppLanguage(appSettings.appLanguage)
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

    func synchronizeAppLanguage(_ language: AppLanguage) {
        guard appLanguage != language else { return }
        applyStateFromAppSettings { appLanguage = language }
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
