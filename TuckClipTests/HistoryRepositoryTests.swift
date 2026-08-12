import Foundation
import XCTest
@testable import TuckClip

final class HistoryRepositoryTests: XCTestCase {
    func testJSONRoundTripReplacesPreviousSnapshot() throws {
        let (repository, _) = try makeRepository()
        let firstDate = Date(timeIntervalSince1970: 1_700_000_000)
        let first = ClipItem(
            kind: .text,
            plainText: "第一条",
            createdAt: firstDate,
            sourceAppName: "备忘录",
            sourceBundleIdentifier: "com.apple.Notes",
            fingerprint: "first"
        )
        let second = ClipItem(
            kind: .files,
            filePaths: ["/tmp/报告.pdf"],
            createdAt: firstDate.addingTimeInterval(10),
            fingerprint: "second",
            isPinned: true,
            copyCount: 3
        )

        try repository.save([first])
        XCTAssertEqual(try repository.load(), [first])

        try repository.save([second])
        XCTAssertEqual(try repository.load(), [second])
        XCTAssertTrue(FileManager.default.fileExists(atPath: repository.historyURL.path))

        let json = try Data(contentsOf: repository.historyURL)
        XCTAssertNoThrow(try JSONSerialization.jsonObject(with: json))
    }

    func testMissingHistoryLoadsAsEmptyWithoutCreatingFiles() throws {
        let (repository, root) = try makeRepository(createRoot: false)

        XCTAssertEqual(try repository.load(), [])
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.path))
    }

    func testImageStorageAndOrphanCleanup() throws {
        let (repository, _) = try makeRepository()
        let pngHeader = Data([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])

        let keptName = try repository.saveImage(pngHeader, fileName: "kept.png")
        let orphanName = try repository.saveImage(pngHeader, fileName: "orphan.png")

        XCTAssertNotNil(repository.imageURL(forFileName: keptName))
        XCTAssertNotNil(repository.imageURL(forFileName: orphanName))
        XCTAssertNil(repository.imageURL(forFileName: "../history.json"))

        let removed = try repository.cleanupOrphanedImages(
            referencedFileNames: Set([keptName])
        )

        XCTAssertEqual(removed, 1)
        XCTAssertNotNil(repository.imageURL(forFileName: keptName))
        XCTAssertNil(repository.imageURL(forFileName: orphanName))
    }

    func testImageStorageRejectsEmptyDataAndUnsafeNames() throws {
        let (repository, _) = try makeRepository()

        XCTAssertThrowsError(try repository.saveImage(Data()))
        XCTAssertThrowsError(
            try repository.saveImage(Data([1]), fileName: "../outside.img")
        )
    }

    private func makeRepository(
        createRoot: Bool = true
    ) throws -> (HistoryRepository, URL) {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipTests-\(UUID().uuidString)", isDirectory: true)
        if createRoot {
            try FileManager.default.createDirectory(
                at: root,
                withIntermediateDirectories: true
            )
        }
        addTeardownBlock {
            try? FileManager.default.removeItem(at: root)
        }
        return (HistoryRepository(rootDirectory: root), root)
    }
}
