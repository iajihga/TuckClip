import SwiftUI

enum TuckClipTheme {
    static let indigo = Color(red: 0.37, green: 0.35, blue: 0.98)
    static let cyan = Color(red: 0.19, green: 0.82, blue: 0.92)
    static let deepBlue = Color(red: 0.035, green: 0.055, blue: 0.13)
    static let panelBlue = Color(red: 0.07, green: 0.10, blue: 0.22)

    static let accentGradient = LinearGradient(
        colors: [indigo, Color(red: 0.25, green: 0.53, blue: 1.0), cyan],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )

    static let panelGradient = LinearGradient(
        colors: [
            deepBlue.opacity(0.98),
            Color(red: 0.08, green: 0.08, blue: 0.23).opacity(0.97),
            panelBlue.opacity(0.98)
        ],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )
}

extension ClipDisplayKind {
    var tint: Color {
        switch self {
        case .text: TuckClipTheme.cyan
        case .link: Color(red: 0.40, green: 0.55, blue: 1.0)
        case .image: Color(red: 0.64, green: 0.45, blue: 1.0)
        case .files: Color(red: 0.32, green: 0.76, blue: 0.78)
        }
    }
}
