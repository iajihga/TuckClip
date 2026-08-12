import Foundation

/// A normalized, transient snapshot produced by the pasteboard monitor.
///
/// Images are kept in memory only until ``ClipboardStore`` persists them. Files
/// remain references to their original paths and are never copied into TuckClip.
struct ClipboardCapture: Equatable, Sendable {
    let kind: ClipKind
    let plainText: String?
    let filePaths: [String]
    let imageData: Data?
    let sourceAppName: String?
    let sourceBundleIdentifier: String?
    let timestamp: Date
    let fingerprint: String

    init(
        kind: ClipKind,
        plainText: String? = nil,
        filePaths: [String] = [],
        imageData: Data? = nil,
        sourceAppName: String? = nil,
        sourceBundleIdentifier: String? = nil,
        timestamp: Date = .now,
        fingerprint: String
    ) {
        self.kind = kind
        self.plainText = plainText
        self.filePaths = filePaths
        self.imageData = imageData
        self.sourceAppName = sourceAppName
        self.sourceBundleIdentifier = sourceBundleIdentifier
        self.timestamp = timestamp
        self.fingerprint = fingerprint
    }
}
