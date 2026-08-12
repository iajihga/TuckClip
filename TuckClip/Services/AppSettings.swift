import Carbon.HIToolbox
import Combine
import Foundation

/// User-adjustable settings shared by the menu bar app and the system services.
///
/// Clipboard contents never leave the process. Only small preferences are stored
/// in `UserDefaults`; history and image data are managed by `ClipboardStore`.
@MainActor
final class AppSettings: ObservableObject {
    static let shared = AppSettings()

    /// A hard upper bound for one normalized clipboard payload (25 MiB).
    nonisolated static let maximumCaptureSizeBytes = 25 * 1024 * 1024

    /// Text lives inside the JSON metadata snapshot and is encoded on each
    /// commit, so it uses a much smaller bound than binary image blobs.
    nonisolated static let maximumTextCaptureSizeBytes = 128 * 1024

    /// Carbon's virtual key code for V and its Command + Option modifiers.
    nonisolated static let defaultHotKeyCode = GlobalHotKey.defaultValue.keyCode
    nonisolated static let defaultHotKeyModifiers = GlobalHotKey.defaultValue.modifiers

    nonisolated static let defaultExcludedBundleIdentifiers: Set<String> = [
        "com.1password.1password",
        "com.agilebits.onepassword4",
        "com.agilebits.onepassword7",
        "com.apple.Passwords",
        "com.bitwarden.desktop",
        "com.dashlane.Dashlane",
        "com.enpass.Enpass",
        "com.lastpass.LastPass",
        "org.keepassxc.keepassxc"
    ]

    @Published var isMonitoringEnabled: Bool {
        didSet { defaults.set(isMonitoringEnabled, forKey: Key.isMonitoringEnabled) }
    }

    @Published var pollingInterval: TimeInterval {
        didSet { defaults.set(pollingInterval, forKey: Key.pollingInterval) }
    }

    @Published var maximumHistoryItems: Int {
        didSet { defaults.set(maximumHistoryItems, forKey: Key.maximumHistoryItems) }
    }

    @Published var historyRetentionDays: Int {
        didSet { defaults.set(historyRetentionDays, forKey: Key.historyRetentionDays) }
    }

    @Published var capturesImages: Bool {
        didSet { defaults.set(capturesImages, forKey: Key.capturesImages) }
    }

    @Published var excludedBundleIdentifiers: Set<String> {
        didSet {
            defaults.set(
                excludedBundleIdentifiers.sorted(),
                forKey: Key.excludedBundleIdentifiers
            )
        }
    }

    @Published var hotKeyCode: UInt32 {
        didSet { defaults.set(Int(hotKeyCode), forKey: Key.hotKeyCode) }
    }

    @Published var hotKeyModifiers: UInt32 {
        didSet { defaults.set(Int(hotKeyModifiers), forKey: Key.hotKeyModifiers) }
    }

    @Published var automaticallyPasteAfterSelection: Bool {
        didSet {
            defaults.set(
                automaticallyPasteAfterSelection,
                forKey: Key.automaticallyPasteAfterSelection
            )
        }
    }

    @Published var appLanguage: AppLanguage {
        didSet { defaults.set(appLanguage.rawValue, forKey: L10n.settingsKey) }
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults

        isMonitoringEnabled = Self.bool(
            in: defaults,
            key: Key.isMonitoringEnabled,
            fallback: true
        )
        pollingInterval = Self.double(
            in: defaults,
            key: Key.pollingInterval,
            fallback: 0.35
        ).clamped(to: 0.1 ... 2.0)
        maximumHistoryItems = Self.integer(
            in: defaults,
            key: Key.maximumHistoryItems,
            fallback: 500
        ).clamped(to: 50 ... 10_000)
        historyRetentionDays = Self.integer(
            in: defaults,
            key: Key.historyRetentionDays,
            fallback: 30
        ).clamped(to: 0 ... 3_650)
        capturesImages = Self.bool(
            in: defaults,
            key: Key.capturesImages,
            fallback: true
        )
        if let savedBundleIdentifiers = defaults.stringArray(
            forKey: Key.excludedBundleIdentifiers
        ) {
            excludedBundleIdentifiers = Set(savedBundleIdentifiers)
        } else {
            excludedBundleIdentifiers = Self.defaultExcludedBundleIdentifiers
        }
        hotKeyCode = UInt32(
            Self.integer(
                in: defaults,
                key: Key.hotKeyCode,
                fallback: Int(Self.defaultHotKeyCode)
            )
        )
        hotKeyModifiers = UInt32(
            Self.integer(
                in: defaults,
                key: Key.hotKeyModifiers,
                fallback: Int(Self.defaultHotKeyModifiers)
            )
        )
        automaticallyPasteAfterSelection = Self.bool(
            in: defaults,
            key: Key.automaticallyPasteAfterSelection,
            fallback: true
        )
        appLanguage = defaults.string(forKey: L10n.settingsKey)
            .flatMap(AppLanguage.init(rawValue:)) ?? .system
        if (try? GlobalHotKey(
            keyCode: hotKeyCode,
            modifiers: hotKeyModifiers
        ).validated()) == nil {
            hotKeyCode = Self.defaultHotKeyCode
            hotKeyModifiers = Self.defaultHotKeyModifiers
        }
    }

    func restoreDefaultExcludedApplications() {
        excludedBundleIdentifiers = Self.defaultExcludedBundleIdentifiers
    }

    func restoreDefaultHotKey() {
        setHotKey(.defaultValue)
    }

    var globalHotKey: GlobalHotKey {
        GlobalHotKey(keyCode: hotKeyCode, modifiers: hotKeyModifiers)
    }

    func setHotKey(_ hotKey: GlobalHotKey) {
        guard let validated = try? hotKey.validated() else { return }
        hotKeyCode = validated.keyCode
        hotKeyModifiers = validated.modifiers
    }

    private enum Key {
        // These keys intentionally match UISettingsStore so the service and UI
        // observe the same persisted preferences.
        static let isMonitoringEnabled = "TuckClip.recordingEnabled"
        static let pollingInterval = "settings.pollingInterval"
        static let maximumHistoryItems = "TuckClip.maximumItemCount"
        static let historyRetentionDays = "TuckClip.retentionDays"
        static let capturesImages = "TuckClip.capturesImages"
        static let excludedBundleIdentifiers = "TuckClip.excludedBundleIdentifiers"
        static let hotKeyCode = "settings.hotKeyCode"
        static let hotKeyModifiers = "settings.hotKeyModifiers"
        static let automaticallyPasteAfterSelection = "TuckClip.automaticPasteEnabled"
    }

    private static func bool(
        in defaults: UserDefaults,
        key: String,
        fallback: Bool
    ) -> Bool {
        guard defaults.object(forKey: key) != nil else { return fallback }
        return defaults.bool(forKey: key)
    }

    private static func integer(
        in defaults: UserDefaults,
        key: String,
        fallback: Int
    ) -> Int {
        guard defaults.object(forKey: key) != nil else { return fallback }
        return defaults.integer(forKey: key)
    }

    private static func double(
        in defaults: UserDefaults,
        key: String,
        fallback: Double
    ) -> Double {
        guard defaults.object(forKey: key) != nil else { return fallback }
        return defaults.double(forKey: key)
    }
}

private extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
