import Foundation

/// Synchronous, local-only persistence for clipboard metadata and image blobs.
///
/// The repository intentionally contains no policy: pinning, deduplication and
/// retention belong to ``ClipboardStore``. A custom root makes every file-system
/// operation deterministic in tests.
final class HistoryRepository {
    enum RepositoryError: LocalizedError {
        case emptyImageData
        case invalidImageFileName(String)

        var errorDescription: String? {
            switch self {
            case .emptyImageData:
                return "The image data is empty."
            case .invalidImageFileName(let name):
                return "The image file name is invalid: \(name)"
            }
        }
    }

    let rootDirectory: URL
    let historyURL: URL
    let imagesDirectoryURL: URL

    private let fileManager: FileManager

    init(rootDirectory: URL? = nil, fileManager: FileManager = .default) {
        self.fileManager = fileManager

        let baseDirectory = rootDirectory ?? Self.defaultRootDirectory(using: fileManager)
        self.rootDirectory = baseDirectory.standardizedFileURL
        self.historyURL = self.rootDirectory.appendingPathComponent("history.json", isDirectory: false)
        self.imagesDirectoryURL = self.rootDirectory.appendingPathComponent("Images", isDirectory: true)
    }

    /// Compatibility aliases that read naturally at call sites.
    var historyFileURL: URL { historyURL }
    var imagesDirectory: URL { imagesDirectoryURL }

    func load() throws -> [ClipItem] {
        guard fileManager.fileExists(atPath: historyURL.path) else {
            return []
        }

        let data = try Data(contentsOf: historyURL)
        return try Self.makeDecoder().decode([ClipItem].self, from: data)
    }

    func loadItems() throws -> [ClipItem] {
        try load()
    }

    /// Encodes the complete metadata snapshot and atomically replaces the JSON.
    func save(_ items: [ClipItem]) throws {
        try ensureRootDirectoryExists()
        let data = try Self.makeEncoder().encode(items)
        try data.write(to: historyURL, options: .atomic)
    }

    func saveItems(_ items: [ClipItem]) throws {
        try save(items)
    }

    /// Stores one image and returns only its safe relative file name.
    @discardableResult
    func saveImage(_ data: Data, fileName requestedFileName: String? = nil) throws -> String {
        guard !data.isEmpty else {
            throw RepositoryError.emptyImageData
        }

        try ensureImagesDirectoryExists()

        let fileName = requestedFileName ?? "\(UUID().uuidString.lowercased()).\(Self.imageFileExtension(for: data))"
        guard Self.isSafeFileName(fileName) else {
            throw RepositoryError.invalidImageFileName(fileName)
        }

        let destination = imagesDirectoryURL.appendingPathComponent(fileName, isDirectory: false)
        try data.write(to: destination, options: .atomic)
        return fileName
    }

    func imageURL(forFileName fileName: String?) -> URL? {
        guard let fileName, Self.isSafeFileName(fileName) else {
            return nil
        }

        let url = imagesDirectoryURL.appendingPathComponent(fileName, isDirectory: false)
        guard fileManager.fileExists(atPath: url.path) else {
            return nil
        }
        return url
    }

    func imageURL(fileName: String?) -> URL? {
        imageURL(forFileName: fileName)
    }

    func deleteImage(named fileName: String?) throws {
        guard let fileName, Self.isSafeFileName(fileName) else {
            return
        }

        let url = imagesDirectoryURL.appendingPathComponent(fileName, isDirectory: false)
        guard fileManager.fileExists(atPath: url.path) else {
            return
        }
        try fileManager.removeItem(at: url)
    }

    /// Removes image blobs no longer referenced by the committed metadata.
    @discardableResult
    func cleanupOrphanedImages(referencedFileNames: Set<String>) throws -> Int {
        guard fileManager.fileExists(atPath: imagesDirectoryURL.path) else {
            return 0
        }

        let files = try fileManager.contentsOfDirectory(
            at: imagesDirectoryURL,
            includingPropertiesForKeys: [.isRegularFileKey],
            options: [.skipsHiddenFiles]
        )

        var removedCount = 0
        for file in files {
            let values = try file.resourceValues(forKeys: [.isRegularFileKey])
            guard values.isRegularFile == true,
                  !referencedFileNames.contains(file.lastPathComponent) else {
                continue
            }
            try fileManager.removeItem(at: file)
            removedCount += 1
        }
        return removedCount
    }

    @discardableResult
    func cleanupOrphanedImages(referencedBy items: [ClipItem]) throws -> Int {
        let names = Set(items.compactMap(\.imageFileName))
        return try cleanupOrphanedImages(referencedFileNames: names)
    }

    private static func defaultRootDirectory(using fileManager: FileManager) -> URL {
        let applicationSupport = fileManager.urls(
            for: .applicationSupportDirectory,
            in: .userDomainMask
        ).first ?? fileManager.temporaryDirectory

        return applicationSupport.appendingPathComponent("TuckClip", isDirectory: true)
    }

    private func ensureRootDirectoryExists() throws {
        try fileManager.createDirectory(
            at: rootDirectory,
            withIntermediateDirectories: true,
            attributes: nil
        )
        var directoryURL = rootDirectory
        var resourceValues = URLResourceValues()
        resourceValues.isExcludedFromBackup = true
        try? directoryURL.setResourceValues(resourceValues)
    }

    private func ensureImagesDirectoryExists() throws {
        try fileManager.createDirectory(
            at: imagesDirectoryURL,
            withIntermediateDirectories: true,
            attributes: nil
        )
    }

    private static func makeEncoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .millisecondsSince1970
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }

    private static func makeDecoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .millisecondsSince1970
        return decoder
    }

    private static func isSafeFileName(_ fileName: String) -> Bool {
        guard !fileName.isEmpty,
              fileName != ".",
              fileName != "..",
              !fileName.contains("/"),
              !fileName.contains(":") else {
            return false
        }
        return URL(fileURLWithPath: fileName).lastPathComponent == fileName
    }

    private static func imageFileExtension(for data: Data) -> String {
        let bytes = [UInt8](data.prefix(12))

        if bytes.starts(with: [0x89, 0x50, 0x4E, 0x47]) {
            return "png"
        }
        if bytes.starts(with: [0xFF, 0xD8, 0xFF]) {
            return "jpg"
        }
        if bytes.starts(with: [0x47, 0x49, 0x46, 0x38]) {
            return "gif"
        }
        if bytes.starts(with: [0x49, 0x49, 0x2A, 0x00]) ||
            bytes.starts(with: [0x4D, 0x4D, 0x00, 0x2A]) {
            return "tiff"
        }
        return "img"
    }
}
