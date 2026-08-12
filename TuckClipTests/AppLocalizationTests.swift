import Foundation
import XCTest
@testable import TuckClip

@MainActor
final class AppLocalizationTests: XCTestCase {
    func testExplicitLanguagesTranslateAndFallBackSafely() {
        XCTAssertEqual(L10n.text("设置…", language: .english), "Settings…")
        XCTAssertEqual(L10n.text("设置…", language: .simplifiedChinese), "设置…")
        XCTAssertEqual(L10n.text("未收录文案", language: .english), "未收录文案")
        XCTAssertEqual(
            L10n.format("%d 项", language: .english, 7),
            "7 items"
        )
    }

    func testSystemLanguageResolutionUsesPreferredLanguage() {
        XCTAssertEqual(
            AppLanguage.system.resolved(preferredLanguages: ["zh-Hans-CN"]),
            .simplifiedChinese
        )
        XCTAssertEqual(
            AppLanguage.system.resolved(preferredLanguages: ["en-US"]),
            .english
        )
        XCTAssertEqual(AppLanguage.english.resolved(preferredLanguages: ["zh-CN"]), .english)
    }

    func testPermissionsGuideAppearsOnlyOnceWhenAutomaticPasteNeedsAccess() {
        XCTAssertTrue(AppDelegate.shouldShowPermissionsGuide(
            automaticPasteEnabled: true,
            isAccessibilityTrusted: false,
            hasShownPermissionsGuide: false
        ))
        XCTAssertFalse(AppDelegate.shouldShowPermissionsGuide(
            automaticPasteEnabled: false,
            isAccessibilityTrusted: false,
            hasShownPermissionsGuide: false
        ))
        XCTAssertFalse(AppDelegate.shouldShowPermissionsGuide(
            automaticPasteEnabled: true,
            isAccessibilityTrusted: true,
            hasShownPermissionsGuide: false
        ))
        XCTAssertFalse(AppDelegate.shouldShowPermissionsGuide(
            automaticPasteEnabled: true,
            isAccessibilityTrusted: false,
            hasShownPermissionsGuide: true
        ))
    }

    func testPermissionsGuideEnglishCopyIsComplete() {
        XCTAssertEqual(L10n.text("首次设置", language: .english), "First-time setup")
        XCTAssertEqual(
            L10n.text("打开辅助功能设置", language: .english),
            "Open Accessibility Settings"
        )
    }

    func testLanguagePersistsAndSynchronizesWithoutResettingOtherSettings() throws {
        let suiteName = "TuckClipLanguageSettingsTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suiteName))
        defer { defaults.removePersistentDomain(forName: suiteName) }

        let appSettings = AppSettings(defaults: defaults)
        appSettings.isMonitoringEnabled = false
        let uiSettings = UISettingsStore(appSettings: appSettings)
        uiSettings.appLanguage = .english

        XCTAssertEqual(appSettings.appLanguage, .english)
        XCTAssertFalse(appSettings.isMonitoringEnabled)
        XCTAssertEqual(uiSettings.localized("设置…"), "Settings…")

        let reloaded = AppSettings(defaults: defaults)
        XCTAssertEqual(reloaded.appLanguage, .english)
        XCTAssertFalse(reloaded.isMonitoringEnabled)
    }
}
