import Foundation

/// The durable metadata for one unique clipboard value.
///
/// Large image bytes live beside the JSON database and are referenced through
/// `imageFileName`. Finder items deliberately retain only their original paths.
struct ClipItem: Codable, Equatable, Identifiable, Sendable {
    let id: UUID
    var kind: ClipKind
    var plainText: String?
    var filePaths: [String]
    var imageFileName: String?
    let createdAt: Date
    var updatedAt: Date
    var sourceAppName: String?
    var sourceBundleIdentifier: String?
    let fingerprint: String
    var isPinned: Bool
    var copyCount: Int

    init(
        id: UUID = UUID(),
        kind: ClipKind,
        plainText: String? = nil,
        filePaths: [String] = [],
        imageFileName: String? = nil,
        createdAt: Date = .now,
        updatedAt: Date? = nil,
        sourceAppName: String? = nil,
        sourceBundleIdentifier: String? = nil,
        fingerprint: String,
        isPinned: Bool = false,
        copyCount: Int = 1
    ) {
        self.id = id
        self.kind = kind
        self.plainText = plainText
        self.filePaths = filePaths
        self.imageFileName = imageFileName
        self.createdAt = createdAt
        self.updatedAt = updatedAt ?? createdAt
        self.sourceAppName = sourceAppName
        self.sourceBundleIdentifier = sourceBundleIdentifier
        self.fingerprint = fingerprint
        self.isPinned = isPinned
        self.copyCount = max(1, copyCount)
    }
}
