import SwiftUI

@main
struct TuckClipApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        Settings {
            TuckClipSettingsView(
                settings: appDelegate.coordinator.uiSettings,
                panelViewModel: appDelegate.coordinator.panelViewModel
            )
        }
    }
}
