import AppKit
import Foundation
import XCTest
@testable import TuckClip

@MainActor
final class CoordinatorIntegrationTests: XCTestCase {
    func testRecordingToggleDoesNotRevertDuringSettingsPublication() async throws {
        let suiteName = "TuckClipRecordingToggleTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipRecordingToggleTests-\(UUID().uuidString)", isDirectory: true)
        defer {
            defaults.removePersistentDomain(forName: suiteName)
            try? FileManager.default.removeItem(at: root)
        }

        let appSettings = AppSettings(defaults: defaults)
        let coordinator = TuckClipAppCoordinator(
            appSettings: appSettings,
            repository: HistoryRepository(rootDirectory: root)
        )
        coordinator.monitor.start()
        defer { coordinator.monitor.stop() }
        XCTAssertTrue(coordinator.uiSettings.recordingEnabled)
        XCTAssertTrue(appSettings.isMonitoringEnabled)
        XCTAssertTrue(coordinator.monitor.isRunning)

        coordinator.toggleRecording()
        XCTAssertFalse(coordinator.uiSettings.recordingEnabled)
        XCTAssertFalse(appSettings.isMonitoringEnabled)
        XCTAssertFalse(coordinator.monitor.isRunning)
        XCTAssertEqual(
            coordinator.uiSettings.recordingStatusTitle,
            coordinator.uiSettings.localized("已暂停")
        )

        // Also verify the values stay aligned after synchronous Combine delivery
        // and a main-actor turn, instead of the old setting being written back.
        await Task.yield()
        XCTAssertFalse(coordinator.uiSettings.recordingEnabled)
        XCTAssertFalse(appSettings.isMonitoringEnabled)

        coordinator.toggleRecording()
        XCTAssertTrue(coordinator.uiSettings.recordingEnabled)
        XCTAssertTrue(appSettings.isMonitoringEnabled)

        appSettings.isMonitoringEnabled = false
        XCTAssertFalse(coordinator.uiSettings.recordingEnabled)
        XCTAssertFalse(appSettings.isMonitoringEnabled)
    }

    func testUIAndAppSettingsStaySynchronizedAcrossWillSetPublications() async throws {
        let suiteName = "TuckClipSettingsSynchronizationTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipSettingsSynchronizationTests-\(UUID().uuidString)", isDirectory: true)
        defer {
            defaults.removePersistentDomain(forName: suiteName)
            try? FileManager.default.removeItem(at: root)
        }

        let appSettings = AppSettings(defaults: defaults)
        let coordinator = TuckClipAppCoordinator(
            appSettings: appSettings,
            repository: HistoryRepository(rootDirectory: root)
        )
        let uiSettings = coordinator.uiSettings

        uiSettings.automaticPasteEnabled = false
        uiSettings.capturesImages = false
        uiSettings.retentionDays = 7
        uiSettings.maximumItemCount = 123

        XCTAssertFalse(uiSettings.automaticPasteEnabled)
        XCTAssertFalse(appSettings.automaticallyPasteAfterSelection)
        XCTAssertFalse(uiSettings.capturesImages)
        XCTAssertFalse(appSettings.capturesImages)
        XCTAssertEqual(uiSettings.retentionDays, 7)
        XCTAssertEqual(appSettings.historyRetentionDays, 7)
        XCTAssertEqual(uiSettings.maximumItemCount, 123)
        XCTAssertEqual(appSettings.maximumHistoryItems, 123)

        await Task.yield()
        XCTAssertFalse(uiSettings.automaticPasteEnabled)
        XCTAssertFalse(uiSettings.capturesImages)
        XCTAssertEqual(uiSettings.retentionDays, 7)
        XCTAssertEqual(uiSettings.maximumItemCount, 123)

        appSettings.automaticallyPasteAfterSelection = true
        appSettings.capturesImages = true
        appSettings.historyRetentionDays = 14
        appSettings.maximumHistoryItems = 321

        XCTAssertTrue(uiSettings.automaticPasteEnabled)
        XCTAssertTrue(uiSettings.capturesImages)
        XCTAssertEqual(uiSettings.retentionDays, 14)
        XCTAssertEqual(uiSettings.maximumItemCount, 321)
    }

    func testPersistedItemsArePublishedIntoPanelViewModel() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipCoordinatorTests-\(UUID().uuidString)", isDirectory: true)
        let repository = HistoryRepository(rootDirectory: root)
        let item = ClipItem(
            kind: .text,
            plainText: "桥接层应显示这条历史",
            fingerprint: "coordinator-bridge"
        )
        try repository.save([item])

        let suiteName = "TuckClipCoordinatorTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let coordinator = TuckClipAppCoordinator(
            appSettings: AppSettings(defaults: defaults),
            repository: repository
        )

        XCTAssertEqual(coordinator.panelViewModel.items.map(\.id), [item.id])
        XCTAssertEqual(coordinator.panelViewModel.items.first?.title, item.plainText)

        defaults.removePersistentDomain(forName: suiteName)
        try? FileManager.default.removeItem(at: root)
    }

    func testPasteFallbackKeepsPanelAvailableAndExplainsManualPaste() async throws {
        let suiteName = "TuckClipPasteFallbackTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let settings = AppSettings(defaults: defaults)
        settings.automaticallyPasteAfterSelection = false
        let pasteboard = NSPasteboard(
            name: NSPasteboard.Name("io.github.iajihga.TuckClipPasteFallbackTests.\(UUID().uuidString)")
        )
        defer { pasteboard.releaseGlobally() }

        let service = PasteService(
            writer: PasteboardWriter(pasteboard: pasteboard),
            settings: settings
        )
        let item = ClipItem(
            kind: .text,
            plainText: "仅复制也要告诉用户",
            fingerprint: "copy-only-feedback"
        )
        var didRequestPanelDismissal = false

        let result = await service.paste(
            item,
            requestPermissionIfNeeded: false,
            beforeSendingPaste: {
                didRequestPanelDismissal = true
                return true
            }
        )

        XCTAssertEqual(result, .copiedOnly(.automaticPasteDisabled))
        XCTAssertFalse(didRequestPanelDismissal)

        let viewModel = ClipboardPanelViewModel()
        viewModel.showPasteResult(result)
        XCTAssertEqual(viewModel.notice?.kind, .copied)
        XCTAssertEqual(viewModel.notice?.message, L10n.text("已复制到剪贴板"))
    }

    func testExcludedApplicationsAutosaveWithoutBeingClobberedBySynchronization() async throws {
        let suiteName = "TuckClipExcludedAppsTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let appSettings = AppSettings(defaults: defaults)
        let uiSettings = UISettingsStore(appSettings: appSettings)
        uiSettings.excludedBundleIdentifiersText = "com.example.SecretApp"

        // Other settings can publish while the user is still typing; the
        // pending privacy edit must remain authoritative until debounce fires.
        uiSettings.synchronizeFromAppSettings()
        XCTAssertEqual(
            uiSettings.excludedBundleIdentifiersText,
            "com.example.SecretApp"
        )

        try await Task.sleep(for: .milliseconds(650))
        XCTAssertEqual(
            appSettings.excludedBundleIdentifiers,
            Set(["com.example.SecretApp"])
        )
    }

    func testLongTextUsesBoundedCardPreviewButRemainsSearchable() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipPreviewTests-\(UUID().uuidString)", isDirectory: true)
        let repository = HistoryRepository(rootDirectory: root)
        let text = String(repeating: "前", count: 900) + "深处关键词"
        try repository.save([ClipItem(
            kind: .text,
            plainText: text,
            fingerprint: "long-preview"
        )])

        let suiteName = "TuckClipPreviewTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer {
            defaults.removePersistentDomain(forName: suiteName)
            try? FileManager.default.removeItem(at: root)
        }

        let coordinator = TuckClipAppCoordinator(
            appSettings: AppSettings(defaults: defaults),
            repository: repository
        )
        let displayItem = try XCTUnwrap(coordinator.panelViewModel.items.first)
        XCTAssertLessThanOrEqual(displayItem.title.count, 701)

        coordinator.panelViewModel.searchText = "深处关键词"
        XCTAssertEqual(coordinator.panelViewModel.filteredItems.map(\.id), [displayItem.id])
    }

    func testSelectedItemFallsBackAfterSearchChangesVisibleResults() {
        let viewModel = ClipboardPanelViewModel()
        let first = ClipDisplayItem(
            id: UUID(),
            kind: .text,
            title: "第一条",
            sourceName: "测试",
            capturedAt: .now,
            isPinned: false
        )
        let second = ClipDisplayItem(
            id: UUID(),
            kind: .text,
            title: "第二条",
            sourceName: "测试",
            capturedAt: .now,
            isPinned: false
        )

        viewModel.replaceItems([first, second])
        viewModel.select(first.id)
        viewModel.searchText = "第二"

        XCTAssertEqual(viewModel.selectedItem?.id, second.id)
    }

    func testPasteTargetSnapshotDoesNotChangeWhenPanelTargetChanges() throws {
        let candidates = NSWorkspace.shared.runningApplications.filter {
            $0.processIdentifier != ProcessInfo.processInfo.processIdentifier
                && $0.bundleIdentifier != Bundle.main.bundleIdentifier
                && !$0.isTerminated
        }
        guard candidates.count >= 2 else {
            throw XCTSkip("Two external running applications are needed for target snapshot validation.")
        }

        let service = PasteService()
        service.rememberTargetApplication(candidates[0])
        let frozenTarget = service.captureTargetSnapshot()
        service.rememberTargetApplication(candidates[1])

        XCTAssertEqual(frozenTarget.processIdentifier, candidates[0].processIdentifier)
        XCTAssertEqual(
            service.captureTargetSnapshot().processIdentifier,
            candidates[1].processIdentifier
        )
    }

    func testPrimaryCardActionSelectsAndRequestsPaste() {
        let viewModel = ClipboardPanelViewModel()
        let bridge = ClipboardUIBridgeSpy()
        let first = ClipDisplayItem(
            id: UUID(),
            kind: .text,
            title: "第一条",
            sourceName: "测试",
            capturedAt: .now,
            isPinned: false
        )
        let second = ClipDisplayItem(
            id: UUID(),
            kind: .text,
            title: "第二条",
            sourceName: "测试",
            capturedAt: .now,
            isPinned: false
        )
        viewModel.bridge = bridge
        viewModel.replaceItems([first, second])

        viewModel.activate(second)

        XCTAssertEqual(viewModel.selectedID, second.id)
        XCTAssertEqual(
            bridge.pasteRequests,
            [.init(itemID: second.id, asPlainText: false)]
        )
    }

    func testPasteRequestsPermissionThenActivatesBeforePosting() async throws {
        let harness = try makePasteHarness(
            hasAccess: false,
            requestResult: true,
            activationResult: true,
            postResult: true
        )
        defer { harness.cleanUp() }
        harness.systemController.resetCalls()

        let result = await harness.service.paste(
            harness.item,
            requestPermissionIfNeeded: true,
            targetSnapshot: currentApplicationSnapshot(),
            beforeSendingPaste: {
                harness.systemController.record("dismiss")
                return true
            }
        )

        XCTAssertEqual(result, .pasted)
        XCTAssertEqual(harness.pasteboard.string(forType: .string), harness.item.plainText)
        XCTAssertEqual(
            harness.systemController.calls,
            [
                "preflight",
                "request",
                "activate:\(ProcessInfo.processInfo.processIdentifier)",
                "dismiss",
                "post:\(ProcessInfo.processInfo.processIdentifier)"
            ]
        )
    }

    func testPasteKeepsPanelVisibleAndDoesNotPostWhenTargetActivationFails() async throws {
        let harness = try makePasteHarness(
            hasAccess: true,
            requestResult: true,
            activationResult: false,
            postResult: true
        )
        defer { harness.cleanUp() }
        harness.systemController.resetCalls()

        var didDismiss = false
        let result = await harness.service.paste(
            harness.item,
            targetSnapshot: currentApplicationSnapshot(),
            beforeSendingPaste: {
                didDismiss = true
                return true
            }
        )

        XCTAssertEqual(result, .copiedOnly(.targetActivationFailed))
        XCTAssertEqual(harness.pasteboard.string(forType: .string), harness.item.plainText)
        XCTAssertFalse(didDismiss)
        XCTAssertEqual(
            harness.systemController.calls,
            [
                "preflight",
                "activate:\(ProcessInfo.processInfo.processIdentifier)"
            ]
        )
    }

    func testPasteFallsBackToCopyWhenPermissionRequestIsDeclined() async throws {
        let harness = try makePasteHarness(
            hasAccess: false,
            requestResult: false,
            activationResult: true,
            postResult: true
        )
        defer { harness.cleanUp() }
        harness.systemController.resetCalls()

        var didDismiss = false
        let result = await harness.service.paste(
            harness.item,
            requestPermissionIfNeeded: true,
            targetSnapshot: currentApplicationSnapshot(),
            beforeSendingPaste: {
                didDismiss = true
                return true
            }
        )

        XCTAssertEqual(result, .copiedOnly(.eventPostingPermissionDenied))
        XCTAssertEqual(harness.pasteboard.string(forType: .string), harness.item.plainText)
        XCTAssertFalse(didDismiss)
        XCTAssertEqual(harness.systemController.calls, ["preflight", "request"])
    }

    func testPasteDoesNotPostIfClipboardChangesWhileTargetActivates() async throws {
        let harness = try makePasteHarness(
            hasAccess: true,
            requestResult: true,
            activationResult: true,
            postResult: true
        )
        defer { harness.cleanUp() }
        harness.systemController.onActivate = {
            harness.pasteboard.clearContents()
            harness.pasteboard.setString("外部应用的新内容", forType: .string)
        }
        harness.systemController.resetCalls()

        var didDismiss = false
        let result = await harness.service.paste(
            harness.item,
            targetSnapshot: currentApplicationSnapshot(),
            beforeSendingPaste: {
                didDismiss = true
                return true
            }
        )

        XCTAssertEqual(result, .copiedOnly(.clipboardContentsChanged))
        XCTAssertFalse(didDismiss)
        XCTAssertTrue(harness.systemController.postedProcessIdentifiers.isEmpty)
        XCTAssertFalse(harness.systemController.calls.contains {
            $0.hasPrefix("post:")
        })
        XCTAssertEqual(
            harness.pasteboard.string(forType: .string),
            "外部应用的新内容"
        )
    }

    func testEventPosterRechecksClipboardAfterPanelDismissalBoundary() async throws {
        let harness = try makePasteHarness(
            hasAccess: true,
            requestResult: true,
            activationResult: true,
            postResult: true
        )
        defer { harness.cleanUp() }
        harness.systemController.resetCalls()

        let result = await harness.service.paste(
            harness.item,
            targetSnapshot: currentApplicationSnapshot(),
            beforeSendingPaste: {
                harness.pasteboard.clearContents()
                harness.pasteboard.setString("关闭面板时发生覆盖", forType: .string)
                return true
            }
        )

        XCTAssertEqual(result, .copiedOnly(.clipboardContentsChanged))
        XCTAssertTrue(harness.systemController.postedProcessIdentifiers.isEmpty)
        XCTAssertTrue(harness.systemController.calls.contains {
            $0.hasPrefix("post:")
        })
    }

    func testClosingAndReopeningPanelCancelsOldPasteSession() async throws {
        guard let targetApplication = NSWorkspace.shared.runningApplications.first(where: {
            $0.activationPolicy == .regular
                && $0.processIdentifier != ProcessInfo.processInfo.processIdentifier
                && !$0.isTerminated
        }) else {
            throw XCTSkip("An external regular application is needed for target snapshot validation.")
        }

        let suiteName = "TuckClipBridgePasteSessionTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipBridgePasteSessionTests-\(UUID().uuidString)")
        let pasteboard = NSPasteboard(
            name: .init("io.github.iajihga.TuckClipBridgePasteSessionTests.\(UUID().uuidString)")
        )
        defer {
            defaults.removePersistentDomain(forName: suiteName)
            pasteboard.releaseGlobally()
            try? FileManager.default.removeItem(at: root)
        }

        let first = ClipItem(
            kind: .text,
            plainText: "旧面板请求",
            fingerprint: "old-panel-request"
        )
        let second = ClipItem(
            kind: .text,
            plainText: "新面板请求",
            fingerprint: "new-panel-request"
        )
        let repository = HistoryRepository(rootDirectory: root)
        try repository.save([first, second])
        let settings = AppSettings(defaults: defaults)
        settings.automaticallyPasteAfterSelection = true
        let store = ClipboardStore(repository: repository)
        let controller = PasteSystemControllerSpy(
            hasAccess: true,
            requestResult: true,
            activationResult: true,
            postResult: true
        )
        controller.activationDelays = [.milliseconds(400), .zero]
        controller.currentPasteboardChangeCount = { pasteboard.changeCount }
        let pasteService = PasteService(
            writer: PasteboardWriter(pasteboard: pasteboard),
            settings: settings,
            systemController: controller,
            currentPasteboardChangeCount: { pasteboard.changeCount }
        )
        pasteService.rememberTargetApplication(targetApplication)
        let bridge = SystemClipboardUIBridge(
            store: store,
            monitor: ClipboardMonitor(settings: settings),
            pasteService: pasteService
        )
        var dismissCount = 0
        var results: [PasteResult] = []
        bridge.dismissPanel = { dismissCount += 1 }
        bridge.onPasteResult = { results.append($0) }

        bridge.beginPanelSession()
        bridge.paste(itemID: first.id, asPlainText: false)
        let didStartOldRequest = await waitUntil {
            controller.calls.contains { $0.hasPrefix("activate:") }
        }
        XCTAssertTrue(didStartOldRequest)

        // Escape/close invalidates the old generation; reopening creates another
        // session whose request is the only one allowed to dismiss or post.
        bridge.cancelPendingPaste()
        bridge.beginPanelSession()
        bridge.paste(itemID: second.id, asPlainText: false)

        let didFinishNewRequest = await waitUntil { results.count == 1 }
        XCTAssertTrue(didFinishNewRequest)
        try await Task.sleep(for: .milliseconds(80))
        XCTAssertEqual(results, [.pasted])
        XCTAssertEqual(dismissCount, 1)
        XCTAssertEqual(controller.postedProcessIdentifiers.count, 1)
        XCTAssertEqual(pasteboard.string(forType: .string), second.plainText)
    }

    private func makePasteHarness(
        hasAccess: Bool,
        requestResult: Bool,
        activationResult: Bool,
        postResult: Bool
    ) throws -> PasteHarness {
        let suiteName = "TuckClipPasteSystemTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let settings = AppSettings(defaults: defaults)
        settings.automaticallyPasteAfterSelection = true
        let pasteboard = NSPasteboard(
            name: .init("io.github.iajihga.TuckClipPasteSystemTests.\(UUID().uuidString)")
        )
        let systemController = PasteSystemControllerSpy(
            hasAccess: hasAccess,
            requestResult: requestResult,
            activationResult: activationResult,
            postResult: postResult
        )
        systemController.currentPasteboardChangeCount = { pasteboard.changeCount }
        let service = PasteService(
            writer: PasteboardWriter(pasteboard: pasteboard),
            settings: settings,
            systemController: systemController,
            currentPasteboardChangeCount: { pasteboard.changeCount }
        )
        return PasteHarness(
            suiteName: suiteName,
            defaults: defaults,
            pasteboard: pasteboard,
            item: ClipItem(
                kind: .text,
                plainText: "自动粘贴链路测试",
                fingerprint: "paste-system-\(UUID().uuidString)"
            ),
            service: service,
            systemController: systemController
        )
    }

    private func currentApplicationSnapshot() -> PasteTargetSnapshot {
        PasteTargetSnapshot(
            processIdentifier: ProcessInfo.processInfo.processIdentifier,
            bundleIdentifier: Bundle.main.bundleIdentifier,
            launchDate: NSRunningApplication.current.launchDate
        )
    }

    private func waitUntil(
        timeout: Duration = .seconds(1),
        condition: @escaping @MainActor () -> Bool
    ) async -> Bool {
        let clock = ContinuousClock()
        let deadline = clock.now + timeout
        while clock.now < deadline {
            if condition() { return true }
            try? await Task.sleep(for: .milliseconds(10))
        }
        return condition()
    }
}

@MainActor
private final class PasteSystemControllerSpy: PasteSystemControlling {
    private var hasAccess: Bool
    private let requestResult: Bool
    private let activationResult: Bool
    private let postResult: PasteEventPostResult
    var onActivate: (() -> Void)?
    var currentPasteboardChangeCount: (() -> Int)?
    var activationDelays: [Duration] = []
    private(set) var calls: [String] = []
    private(set) var postedProcessIdentifiers: [pid_t] = []
    private var activationCallCount = 0

    init(
        hasAccess: Bool,
        requestResult: Bool,
        activationResult: Bool,
        postResult: Bool
    ) {
        self.hasAccess = hasAccess
        self.requestResult = requestResult
        self.activationResult = activationResult
        self.postResult = postResult ? .posted : .failed
    }

    func hasEventPostingAccess() -> Bool {
        calls.append("preflight")
        return hasAccess
    }

    func requestEventPostingAccess() -> Bool {
        calls.append("request")
        hasAccess = requestResult
        return requestResult
    }

    func activateTarget(processIdentifier: pid_t) async -> Bool {
        calls.append("activate:\(processIdentifier)")
        let callIndex = activationCallCount
        activationCallCount += 1
        if activationDelays.indices.contains(callIndex) {
            do {
                try await Task.sleep(for: activationDelays[callIndex])
            } catch {
                return false
            }
        }
        guard !Task.isCancelled else { return false }
        onActivate?()
        return activationResult
    }

    func postCommandV(
        to processIdentifier: pid_t,
        expectedPasteboardChangeCount: Int
    ) -> PasteEventPostResult {
        calls.append("post:\(processIdentifier)")
        if let currentPasteboardChangeCount,
           currentPasteboardChangeCount() != expectedPasteboardChangeCount {
            return .clipboardContentsChanged
        }
        if postResult == .posted {
            postedProcessIdentifiers.append(processIdentifier)
        }
        return postResult
    }

    func record(_ call: String) {
        calls.append(call)
    }

    func resetCalls() {
        calls.removeAll()
        postedProcessIdentifiers.removeAll()
    }
}

@MainActor
private final class ClipboardUIBridgeSpy: ClipboardUIBridge {
    struct PasteRequest: Equatable {
        let itemID: UUID
        let asPlainText: Bool
    }

    var onItemsChanged: (([ClipDisplayItem]) -> Void)?
    private(set) var pasteRequests: [PasteRequest] = []

    func startCapture() {}
    func stopCapture() {}
    func refresh() {}
    func beginPanelSession() {}
    func cancelPendingPaste() {}
    func paste(itemID: UUID, asPlainText: Bool) {
        pasteRequests.append(.init(itemID: itemID, asPlainText: asPlainText))
    }
    func togglePin(itemID: UUID) {}
    func delete(itemID: UUID) {}
    func clearUnpinned() {}
    func clearAll() {}
}

@MainActor
private struct PasteHarness {
    let suiteName: String
    let defaults: UserDefaults
    let pasteboard: NSPasteboard
    let item: ClipItem
    let service: PasteService
    let systemController: PasteSystemControllerSpy

    func cleanUp() {
        defaults.removePersistentDomain(forName: suiteName)
        pasteboard.releaseGlobally()
    }
}
