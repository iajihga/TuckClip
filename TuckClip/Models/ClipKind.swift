import Foundation

/// The portable content families TuckClip persists and can restore.
enum ClipKind: String, Codable, CaseIterable, Identifiable, Sendable {
    case text
    case link
    case image
    case files

    var id: Self { self }
}
