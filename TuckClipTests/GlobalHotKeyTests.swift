import Carbon.HIToolbox
import Foundation
import XCTest
@testable import TuckClip

@MainActor
final class GlobalHotKeyTests: XCTestCase {
    func testRegistrationFailureKeepsPreviousHotKeyActive() throws {
        let system = HotKeySystemSpy()
        let manager = HotKeyManager(system: system)
        try manager.register(.defaultValue)

        let replacement = GlobalHotKey(
            keyCode: UInt32(kVK_ANSI_X),
            modifiers: UInt32(controlKey | shiftKey)
        )
        system.nextRegistrationError = .registrationFailed(OSStatus(eventHotKeyExistsErr))

        XCTAssertThrowsError(try manager.register(replacement))
        XCTAssertTrue(manager.isRegistered)
        XCTAssertEqual(manager.activeHotKey, .defaultValue)
        XCTAssertEqual(system.cancelledIdentifiers, [])

        try manager.register(replacement)
        XCTAssertEqual(manager.activeHotKey, replacement)
        XCTAssertEqual(system.cancelledIdentifiers, [1])
        manager.shutdown()
    }

    func testHotKeyPersistsAndInvalidStoredValueFallsBackToDefault() throws {
        let suiteName = "TuckClipHotKeySettingsTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let settings = AppSettings(defaults: defaults)
        let custom = GlobalHotKey(
            keyCode: UInt32(kVK_ANSI_B),
            modifiers: UInt32(optionKey | shiftKey)
        )
        settings.setHotKey(custom)

        XCTAssertEqual(AppSettings(defaults: defaults).globalHotKey, custom)

        defaults.set(Int(kVK_ANSI_C), forKey: "settings.hotKeyCode")
        defaults.set(0, forKey: "settings.hotKeyModifiers")
        XCTAssertEqual(AppSettings(defaults: defaults).globalHotKey, .defaultValue)
    }

    func testControlOptionVIsAcceptedDisplayedAndPersisted() throws {
        let suiteName = "TuckClipControlOptionHotKeyTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let hotKey = try GlobalHotKey(
            keyCode: UInt32(kVK_ANSI_V),
            modifiers: UInt32(controlKey | optionKey)
        ).validated()
        let settings = AppSettings(defaults: defaults)
        settings.setHotKey(hotKey)

        XCTAssertEqual(hotKey.displayText, "⌃⌥V")
        XCTAssertEqual(AppSettings(defaults: defaults).globalHotKey, hotKey)
    }

    func testRecorderMapsMacControlAndOptionToHotKeyModifiers() {
        let modifiers = HotKeyCaptureView.CaptureView.carbonModifiers(
            from: [.control, .option]
        )

        XCTAssertEqual(modifiers, UInt32(controlKey | optionKey))
    }

    func testRecorderRejectsIncompleteGestureAndDoesNotOpenPanelForCurrentHotKey() throws {
        let suiteName = "TuckClipHotKeyRecorderTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipHotKeyRecorderTests-\(UUID().uuidString)")
        defer {
            defaults.removePersistentDomain(forName: suiteName)
            try? FileManager.default.removeItem(at: root)
        }

        let system = HotKeySystemSpy()
        let manager = HotKeyManager(system: system)
        try manager.register(.defaultValue)
        let coordinator = TuckClipAppCoordinator(
            appSettings: AppSettings(defaults: defaults),
            repository: HistoryRepository(rootDirectory: root),
            hotKeyManager: manager
        )

        coordinator.uiSettings.beginHotKeyRecording()
        coordinator.uiSettings.captureHotKey(GlobalHotKey(
            keyCode: UInt32(kVK_ANSI_X),
            modifiers: 0
        ))
        XCTAssertTrue(coordinator.uiSettings.isRecordingHotKey)
        XCTAssertNotNil(coordinator.uiSettings.hotKeyErrorDescription)

        coordinator.uiSettings.beginHotKeyRecording()
        manager.receiveHotKey(identifier: 1)

        XCTAssertFalse(coordinator.uiSettings.isRecordingHotKey)
        XCTAssertFalse(coordinator.panelController.isVisible)
    }
}

@MainActor
private final class HotKeySystemSpy: HotKeySystemControlling {
    var nextRegistrationError: HotKeyRegistrationError?
    private(set) var cancelledIdentifiers: [UInt32] = []
    private(set) var handlerInstalled = false

    func installHandlerIfNeeded(for manager: HotKeyManager) throws {
        _ = manager
        handlerInstalled = true
    }

    func register(
        _ hotKey: GlobalHotKey,
        identifier: UInt32
    ) throws -> HotKeyRegistrationToken {
        _ = try hotKey.validated()
        if let nextRegistrationError {
            self.nextRegistrationError = nil
            throw nextRegistrationError
        }
        return HotKeyRegistrationToken { [weak self] in
            self?.cancelledIdentifiers.append(identifier)
        }
    }

    func removeHandler() {
        handlerInstalled = false
    }
}
