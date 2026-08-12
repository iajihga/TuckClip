import Foundation
import Sparkle

@MainActor
final class MacUpdateController: NSObject, SPUUpdaterDelegate {
    private nonisolated static let repositoryURL = "https://github.com/mzopedia/TuckClip"

    private lazy var controller = SPUStandardUpdaterController(
        startingUpdater: true,
        updaterDelegate: self,
        userDriverDelegate: nil
    )

    var canCheckForUpdates: Bool {
        controller.updater.canCheckForUpdates
    }

    func start() {
        _ = controller
    }

    func checkForUpdates(_ sender: Any?) {
        controller.checkForUpdates(sender)
    }

    func feedURLString(for updater: SPUUpdater) -> String? {
        Self.feedURLString(architecture: Self.currentArchitecture)
    }

    nonisolated static func feedURLString(architecture: String) -> String? {
        guard architecture == "arm64" || architecture == "x86_64" else {
            return nil
        }

        return "\(repositoryURL)/releases/latest/download/"
            + "TuckClip-macOS-\(architecture)-appcast.xml"
    }

    private nonisolated static var currentArchitecture: String {
#if arch(arm64)
        "arm64"
#elseif arch(x86_64)
        "x86_64"
#else
        "unsupported"
#endif
    }
}
