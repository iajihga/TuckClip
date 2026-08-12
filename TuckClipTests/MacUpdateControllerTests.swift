import XCTest
@testable import TuckClip

final class MacUpdateControllerTests: XCTestCase {
    func testFeedURLsAreArchitectureSpecificAndStableOnly() {
        XCTAssertEqual(
            MacUpdateController.feedURLString(architecture: "arm64"),
            "https://github.com/mzopedia/TuckClip/releases/latest/download/"
                + "TuckClip-macOS-arm64-appcast.xml"
        )
        XCTAssertEqual(
            MacUpdateController.feedURLString(architecture: "x86_64"),
            "https://github.com/mzopedia/TuckClip/releases/latest/download/"
                + "TuckClip-macOS-x86_64-appcast.xml"
        )
        XCTAssertNil(MacUpdateController.feedURLString(architecture: "unknown"))
    }

    func testHostBundleEnablesChecksButRequiresConfirmationBeforeInstalling() {
        XCTAssertEqual(
            Bundle.main.object(forInfoDictionaryKey: "SUEnableAutomaticChecks") as? Bool,
            true
        )
        XCTAssertEqual(
            Bundle.main.object(forInfoDictionaryKey: "SUAutomaticallyUpdate") as? Bool,
            false
        )
        XCTAssertEqual(
            Bundle.main.object(forInfoDictionaryKey: "SUPublicEDKey") as? String,
            "3zAtyNWP/bkX73Zf1HOfB6swfz5FCFgWrJFPqin+vyA="
        )
    }
}
