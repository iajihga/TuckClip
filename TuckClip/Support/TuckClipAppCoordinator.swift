import Combine
import Foundation

@MainActor
final class TuckClipAppCoordinator {
    let appSettings: AppSettings
    let uiSettings: UISettingsStore
    let clipboardStore: ClipboardStore
    let panelViewModel: ClipboardPanelViewModel
    let monitor: ClipboardMonitor
    let pasteService: PasteService
    let hotKeyManager: HotKeyManager
    let bridge: SystemClipboardUIBridge
    let panelController: ClipboardPanelController
    let settingsPresenter: SettingsWindowPresenter

    private(set) var isRuntimeStarted = false
    private var cancellables: Set<AnyCancellable> = []
    private var retentionTimerCancellable: AnyCancellable?

    init(
        appSettings: AppSettings,
        repository: HistoryRepository = HistoryRepository(),
        hotKeyManager injectedHotKeyManager: HotKeyManager? = nil
    ) {
        self.appSettings = appSettings

        let uiSettings = UISettingsStore(appSettings: appSettings)
        let clipboardStore = ClipboardStore(
            repository: repository,
            retentionDays: appSettings.historyRetentionDays,
            maximumItemCount: appSettings.maximumHistoryItems
        )
        let panelViewModel = ClipboardPanelViewModel()
        let monitor = ClipboardMonitor(settings: appSettings)
        let pasteService = PasteService(monitor: monitor, settings: appSettings)
        let hotKeyManager = injectedHotKeyManager ?? HotKeyManager()
        let bridge = SystemClipboardUIBridge(
            store: clipboardStore,
            monitor: monitor,
            pasteService: pasteService
        )
        let panelController = ClipboardPanelController(
            viewModel: panelViewModel,
            settings: uiSettings
        )

        self.uiSettings = uiSettings
        self.clipboardStore = clipboardStore
        self.panelViewModel = panelViewModel
        self.monitor = monitor
        self.pasteService = pasteService
        self.hotKeyManager = hotKeyManager
        self.bridge = bridge
        self.panelController = panelController
        settingsPresenter = SettingsWindowPresenter(
            settings: uiSettings,
            panelViewModel: panelViewModel
        )

        panelViewModel.bridge = bridge
        bridge.onItemsChanged = { [weak panelViewModel] items in
            panelViewModel?.replaceItems(items)
        }
        bridge.onPasteResult = { [weak panelViewModel] result in
            panelViewModel?.showPasteResult(result)
        }
        bridge.dismissPanel = { [weak panelController] in
            panelController?.hideForPaste()
        }
        panelController.onWillShow = { [weak pasteService, weak bridge, weak uiSettings] in
            pasteService?.rememberFrontmostApplication()
            bridge?.refresh()
            uiSettings?.synchronizeFromAppSettings()
            uiSettings?.refreshAccessibilityStatus()
        }
        panelController.onCancel = { [weak pasteService] in
            pasteService?.restoreRememberedApplicationFocus()
        }
        hotKeyManager.onPressed = { [weak panelController, weak uiSettings, weak appSettings] in
            if uiSettings?.isRecordingHotKey == true, let appSettings {
                uiSettings?.captureHotKey(appSettings.globalHotKey)
            } else {
                panelController?.toggle()
            }
        }
        monitor.onCapture = { [weak clipboardStore, weak uiSettings] capture in
            guard uiSettings?.capturesImages == true || capture.kind != .image else { return }
            clipboardStore?.ingest(capture)
        }

        bindSettings()
        bridge.refresh()
    }

    func startRuntime() {
        guard !isRuntimeStarted, !RuntimeEnvironment.isRunningTests else { return }
        isRuntimeStarted = true

        registerHotKey()

        if uiSettings.recordingEnabled && !clipboardStore.isReadOnlyDueToLoadFailure {
            monitor.start()
        }
        retentionTimerCancellable = Timer.publish(
            every: 3_600,
            on: .main,
            in: .common
        )
        .autoconnect()
        .sink { [weak clipboardStore] _ in
            clipboardStore?.applyLimits()
        }
        uiSettings.pasteboardAccessStatus = monitor.accessStatus
        bridge.refresh()
    }

    func stopRuntime() {
        guard isRuntimeStarted else { return }
        panelController.hide(animated: false, restorePreviousApplication: false)
        hotKeyManager.shutdown()
        monitor.stop()
        retentionTimerCancellable?.cancel()
        retentionTimerCancellable = nil
        isRuntimeStarted = false
    }

    func togglePanel() {
        panelController.toggle()
    }

    func toggleRecording() {
        uiSettings.recordingEnabled.toggle()
    }

    func showSettings(tab: TuckClipSettingsTab? = nil) {
        settingsPresenter.show(tab: tab)
    }

    @discardableResult
    func registerHotKey() -> Bool {
        let hotKey = appSettings.globalHotKey
        do {
            try hotKeyManager.register(hotKey)
            uiSettings.synchronizeGlobalHotKey(hotKey)
            uiSettings.hotKeyErrorDescription = nil
            return true
        } catch {
            uiSettings.hotKeyErrorDescription = "\(hotKey.displayText)：\(error.localizedDescription)"
            return false
        }
    }

    private func bindSettings() {
        uiSettings.onRetryGlobalHotKey = { [weak self] in
            self?.registerHotKey()
        }

        uiSettings.onGlobalHotKeyChanged = { [weak self] hotKey in
            guard let self else { return }
            do {
                // HotKeyManager registers the replacement before releasing the
                // current key. Persist only after the replacement is active.
                try hotKeyManager.register(hotKey)
                appSettings.setHotKey(hotKey)
                uiSettings.synchronizeGlobalHotKey(hotKey)
                uiSettings.hotKeyErrorDescription = nil
            } catch {
                uiSettings.synchronizeGlobalHotKey(appSettings.globalHotKey)
                uiSettings.hotKeyErrorDescription = "\(hotKey.displayText)：\(error.localizedDescription)"
            }
        }

        uiSettings.onLanguageChanged = { [weak bridge, weak panelViewModel] _ in
            bridge?.refresh()
            panelViewModel?.refreshLocalization()
        }

        uiSettings.onRecordingChanged = { [weak self] enabled in
            guard let self else { return }
            if !enabled {
                // Pausing is always safe and should synchronously stop any live
                // timer, even during startup/shutdown boundary conditions.
                monitor.stop()
            } else if isRuntimeStarted && !clipboardStore.isReadOnlyDueToLoadFailure {
                monitor.start()
            }
        }

        uiSettings.onRetentionChanged = { [weak self] retentionDays in
            guard let self else { return }
            clipboardStore.applyLimits(
                retentionDays: retentionDays,
                maximumItemCount: uiSettings.maximumItemCount
            )
        }

        uiSettings.onMaximumItemCountChanged = { [weak self] maximumItemCount in
            guard let self else { return }
            clipboardStore.applyLimits(
                retentionDays: uiSettings.retentionDays,
                maximumItemCount: maximumItemCount
            )
        }

        monitor.$accessStatus
            .removeDuplicates()
            .sink { [weak uiSettings] status in
                uiSettings?.pasteboardAccessStatus = status
            }
            .store(in: &cancellables)

        clipboardStore.$persistenceErrorDescription
            .removeDuplicates()
            .sink { [weak uiSettings] errorDescription in
                uiSettings?.storageErrorDescription = errorDescription
            }
            .store(in: &cancellables)

        clipboardStore.$isReadOnlyDueToLoadFailure
            .removeDuplicates()
            .sink { [weak uiSettings] isReadOnly in
                uiSettings?.isStorageReadOnly = isReadOnly
            }
            .store(in: &cancellables)

        appSettings.$isMonitoringEnabled
            .removeDuplicates()
            .sink { [weak uiSettings] enabled in
                // @Published delivers `enabled` before AppSettings' stored value
                // changes. Applying the emitted value avoids restoring the old
                // recording state during a menu-bar pause action.
                uiSettings?.synchronizeRecordingEnabled(enabled)
            }
            .store(in: &cancellables)

        appSettings.$automaticallyPasteAfterSelection
            .removeDuplicates()
            .sink { [weak uiSettings] enabled in
                uiSettings?.synchronizeAutomaticPasteEnabled(enabled)
            }
            .store(in: &cancellables)

        appSettings.$maximumHistoryItems
            .removeDuplicates()
            .sink { [weak uiSettings] maximumItemCount in
                uiSettings?.synchronizeMaximumItemCount(maximumItemCount)
            }
            .store(in: &cancellables)

        appSettings.$historyRetentionDays
            .removeDuplicates()
            .sink { [weak uiSettings] retentionDays in
                uiSettings?.synchronizeRetentionDays(retentionDays)
            }
            .store(in: &cancellables)

        appSettings.$capturesImages
            .removeDuplicates()
            .sink { [weak uiSettings] enabled in
                uiSettings?.synchronizeCapturesImages(enabled)
            }
            .store(in: &cancellables)

        appSettings.$excludedBundleIdentifiers
            .removeDuplicates()
            .sink { [weak uiSettings] _ in
                uiSettings?.synchronizeFromAppSettings()
            }
            .store(in: &cancellables)

        appSettings.$appLanguage
            .removeDuplicates()
            .sink { [weak uiSettings] language in
                uiSettings?.synchronizeAppLanguage(language)
            }
            .store(in: &cancellables)
    }
}
