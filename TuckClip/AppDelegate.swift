import AppKit
import Combine

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let hasLaunchedBeforeKey = "TuckClip.hasLaunchedBefore"
    private static let automaticTerminationReason = "TuckClip 正在记录剪贴板历史"

    lazy var coordinator: TuckClipAppCoordinator = {
        guard RuntimeEnvironment.isRunningTests else {
            return TuckClipAppCoordinator(appSettings: .shared)
        }

        let processID = ProcessInfo.processInfo.processIdentifier
        let suiteName = "io.github.iajihga.TuckClip.TestHost.\(processID)"
        let defaults = UserDefaults(suiteName: suiteName) ?? .standard
        let rootDirectory = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClip-TestHost-\(processID)", isDirectory: true)
        return TuckClipAppCoordinator(
            appSettings: AppSettings(defaults: defaults),
            repository: HistoryRepository(rootDirectory: rootDirectory)
        )
    }()

    private var statusItem: NSStatusItem?
    private var recordingMenuItem: NSMenuItem?
    private var openMenuItem: NSMenuItem?
    private var hotKeyWarningMenuItem: NSMenuItem?
    private var retryHotKeyMenuItem: NSMenuItem?
    private var storageWarningMenuItem: NSMenuItem?
    private var clearMenuItem: NSMenuItem?
    private var settingsMenuItem: NSMenuItem?
    private var quitMenuItem: NSMenuItem?
    private var cancellables: Set<AnyCancellable> = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard !RuntimeEnvironment.isRunningTests else { return }

        ProcessInfo.processInfo.disableAutomaticTermination(Self.automaticTerminationReason)
        ProcessInfo.processInfo.disableSuddenTermination()
        NSApp.setActivationPolicy(.accessory)
        installStatusItem()
        coordinator.startRuntime()
        observeStatus()

        let isFirstLaunch = !UserDefaults.standard.bool(forKey: Self.hasLaunchedBeforeKey)
        if isFirstLaunch {
            UserDefaults.standard.set(true, forKey: Self.hasLaunchedBeforeKey)
        }

#if DEBUG
        let shouldShowInitialPanel = true
#else
        let shouldShowInitialPanel = isFirstLaunch
#endif
        if shouldShowInitialPanel {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self] in
                self?.coordinator.togglePanel()
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        guard !RuntimeEnvironment.isRunningTests else { return }
        coordinator.stopRuntime()
        ProcessInfo.processInfo.enableSuddenTermination()
        ProcessInfo.processInfo.enableAutomaticTermination(Self.automaticTerminationReason)
    }

    func menuWillOpen(_ menu: NSMenu) {
        updateStatusPresentation()
    }

    @objc private func showPanel(_ sender: Any?) {
        coordinator.togglePanel()
    }

    @objc private func toggleRecording(_ sender: Any?) {
        coordinator.toggleRecording()
    }

    @objc private func clearUnpinned(_ sender: Any?) {
        let settings = coordinator.uiSettings
        let alert = NSAlert()
        alert.messageText = settings.localized("清除未置顶历史？")
        alert.informativeText = settings.localized("置顶内容会保留，其他本地剪贴板记录将被永久删除。")
        alert.alertStyle = .warning
        alert.addButton(withTitle: settings.localized("清除"))
        alert.addButton(withTitle: settings.localized("取消"))
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn {
            coordinator.panelViewModel.clearUnpinned()
        }
    }

    @objc private func showSettings(_ sender: Any?) {
        coordinator.showSettings()
    }

    @objc private func retryHotKey(_ sender: Any?) {
        coordinator.registerHotKey()
        updateStatusPresentation()
    }

    @objc private func quit(_ sender: Any?) {
        NSApp.terminate(nil)
    }

    private func installStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.autosaveName = "TuckClip.StatusItem"
        item.button?.imagePosition = .imageOnly

        let menu = NSMenu(title: "TuckClip")
        menu.delegate = self

        let openItem = NSMenuItem(
            title: "打开 TuckClip",
            action: #selector(showPanel(_:)),
            keyEquivalent: ""
        )
        openItem.target = self
        menu.addItem(openItem)
        self.openMenuItem = openItem

        let recordingItem = NSMenuItem(
            title: "暂停记录",
            action: #selector(toggleRecording(_:)),
            keyEquivalent: ""
        )
        recordingItem.target = self
        menu.addItem(recordingItem)
        recordingMenuItem = recordingItem

        let hotKeyWarningItem = NSMenuItem(
            title: "⌥⌘V 暂不可用",
            action: nil,
            keyEquivalent: ""
        )
        hotKeyWarningItem.isEnabled = false
        hotKeyWarningItem.isHidden = true
        menu.addItem(hotKeyWarningItem)
        hotKeyWarningMenuItem = hotKeyWarningItem

        let retryHotKeyItem = NSMenuItem(
            title: "重新注册快捷键",
            action: #selector(retryHotKey(_:)),
            keyEquivalent: ""
        )
        retryHotKeyItem.target = self
        retryHotKeyItem.isHidden = true
        menu.addItem(retryHotKeyItem)
        retryHotKeyMenuItem = retryHotKeyItem

        let storageWarningItem = NSMenuItem(
            title: "存储异常，查看设置…",
            action: #selector(showSettings(_:)),
            keyEquivalent: ""
        )
        storageWarningItem.target = self
        storageWarningItem.isHidden = true
        menu.addItem(storageWarningItem)
        storageWarningMenuItem = storageWarningItem

        menu.addItem(.separator())

        let clearItem = NSMenuItem(
            title: "清除未置顶历史…",
            action: #selector(clearUnpinned(_:)),
            keyEquivalent: ""
        )
        clearItem.target = self
        menu.addItem(clearItem)
        clearMenuItem = clearItem

        let settingsItem = NSMenuItem(
            title: "设置…",
            action: #selector(showSettings(_:)),
            keyEquivalent: ","
        )
        settingsItem.keyEquivalentModifierMask = [.command]
        settingsItem.target = self
        menu.addItem(settingsItem)
        settingsMenuItem = settingsItem

        menu.addItem(.separator())

        let quitItem = NSMenuItem(
            title: "退出 TuckClip",
            action: #selector(quit(_:)),
            keyEquivalent: "q"
        )
        quitItem.keyEquivalentModifierMask = [.command]
        quitItem.target = self
        menu.addItem(quitItem)
        quitMenuItem = quitItem

        item.menu = menu
        statusItem = item
        updateStatusPresentation()
    }

    private func observeStatus() {
        coordinator.uiSettings.$recordingEnabled
            .removeDuplicates()
            .sink { [weak self] enabled in
                // @Published emits from willSet. Use the carried value so the
                // status icon and menu title change in the same menu action.
                self?.updateStatusPresentation(recordingEnabled: enabled)
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$pasteboardAccessStatus
            .removeDuplicates()
            .sink { [weak self] _ in
                self?.updateStatusPresentation()
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$hotKeyErrorDescription
            .removeDuplicates()
            .sink { [weak self] _ in
                self?.updateStatusPresentation()
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$globalHotKey
            .removeDuplicates()
            .sink { [weak self] hotKey in
                self?.updateStatusPresentation(hotKey: hotKey)
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$storageErrorDescription
            .removeDuplicates()
            .sink { [weak self] _ in
                self?.updateStatusPresentation()
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$isStorageReadOnly
            .removeDuplicates()
            .sink { [weak self] _ in
                self?.updateStatusPresentation()
            }
            .store(in: &cancellables)

        coordinator.uiSettings.$appLanguage
            .removeDuplicates()
            .sink { [weak self] language in
                self?.updateStatusPresentation(language: language)
            }
            .store(in: &cancellables)
    }

    private func updateStatusPresentation(
        recordingEnabled: Bool? = nil,
        hotKey: GlobalHotKey? = nil,
        language: AppLanguage? = nil
    ) {
        let settings = coordinator.uiSettings
        let preference = language ?? settings.appLanguage
        let localized: (String) -> String = {
            L10n.text($0, language: preference)
        }
        let isRecording = recordingEnabled ?? settings.recordingEnabled
        let isAccessReady = settings.isPasteboardAccessReady
        let symbolName: String
        if settings.isStorageReadOnly
            || settings.storageErrorDescription != nil
            || (isRecording && !isAccessReady) {
            symbolName = "exclamationmark.triangle"
        } else {
            symbolName = isRecording ? "square.on.square" : "pause.circle"
        }
        let image = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)
        image?.isTemplate = true
        statusItem?.button?.image = image
        let recordingStatusTitle = settings.recordingStatusTitle(
            for: isRecording,
            language: preference
        )
        statusItem?.button?.toolTip = "TuckClip · \(recordingStatusTitle)"
        recordingMenuItem?.title = localized(
            isRecording ? "暂停记录" : "恢复记录"
        )
        recordingMenuItem?.state = isRecording ? .on : .off
        openMenuItem?.title = L10n.format(
            "打开 TuckClip（%@）",
            language: preference,
            (hotKey ?? settings.globalHotKey).displayText
        )
        retryHotKeyMenuItem?.title = localized("重新注册快捷键")
        clearMenuItem?.title = localized("清除未置顶历史…")
        settingsMenuItem?.title = localized("设置…")
        quitMenuItem?.title = localized("退出 TuckClip")

        if let error = settings.hotKeyErrorDescription {
            hotKeyWarningMenuItem?.title = L10n.format(
                "快捷键不可用：%@",
                language: preference,
                error
            )
            hotKeyWarningMenuItem?.isHidden = false
            retryHotKeyMenuItem?.isHidden = false
        } else {
            hotKeyWarningMenuItem?.isHidden = true
            retryHotKeyMenuItem?.isHidden = true
        }

        storageWarningMenuItem?.title = settings.isStorageReadOnly
            ? localized("历史已受保护，查看设置…")
            : localized("最近一次保存失败，查看设置…")
        storageWarningMenuItem?.isHidden = settings.storageErrorDescription == nil
    }
}
