import Foundation
import XCTest
@testable import TuckClip

@MainActor
final class ClipboardStoreTests: XCTestCase {
    private let referenceDate = Date(timeIntervalSince1970: 1_800_000_000)

    func testDuplicateCaptureMergesAndPersistsLatestMetadata() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)

        let original = store.ingest(capture(
            text: "重复内容",
            fingerprint: "same",
            secondsFromNow: -60,
            appName: "Safari"
        ))
        _ = store.ingest(capture(
            text: "另一条",
            fingerprint: "other",
            secondsFromNow: -30
        ))
        let merged = store.ingest(capture(
            text: "重复内容",
            fingerprint: "same",
            secondsFromNow: 0,
            appName: "备忘录"
        ))

        XCTAssertEqual(store.items.count, 2)
        XCTAssertEqual(merged?.id, original?.id)
        XCTAssertEqual(merged?.copyCount, 2)
        XCTAssertEqual(merged?.sourceAppName, "备忘录")
        XCTAssertEqual(merged?.updatedAt, referenceDate)
        XCTAssertEqual(store.items.first?.id, original?.id)

        let reloaded = ClipboardStore(
            repository: repository,
            retentionDays: 0,
            maximumItemCount: 20,
            now: { self.referenceDate }
        )
        XCTAssertEqual(reloaded.items.count, 2)
        XCTAssertEqual(
            reloaded.items.first(where: { $0.fingerprint == "same" })?.copyCount,
            2
        )
    }

    func testImageDuplicateReusesOneBlobAndDeleteCleansIt() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        let data = Data([0x89, 0x50, 0x4E, 0x47, 1, 2, 3])
        let firstCapture = ClipboardCapture(
            kind: .image,
            imageData: data,
            timestamp: referenceDate,
            fingerprint: "image"
        )

        let first = try XCTUnwrap(store.ingest(firstCapture))
        _ = store.ingest(firstCapture)

        XCTAssertEqual(store.items.count, 1)
        XCTAssertEqual(store.items.first?.copyCount, 2)
        XCTAssertNotNil(store.imageURL(for: first))
        XCTAssertEqual(
            try FileManager.default.contentsOfDirectory(
                at: repository.imagesDirectoryURL,
                includingPropertiesForKeys: nil
            ).count,
            1
        )

        store.delete(id: first.id)

        XCTAssertTrue(store.items.isEmpty)
        XCTAssertNil(store.imageURL(for: first))
        XCTAssertEqual(
            try FileManager.default.contentsOfDirectory(
                at: repository.imagesDirectoryURL,
                includingPropertiesForKeys: nil
            ).count,
            0
        )
    }

    func testClearUnpinnedKeepsPinnedItemAcrossRestart() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        let pinned = try XCTUnwrap(store.ingest(capture(
            text: "长期保留",
            fingerprint: "keep",
            secondsFromNow: -20
        )))
        _ = store.ingest(capture(
            text: "普通历史",
            fingerprint: "discard",
            secondsFromNow: -10
        ))

        store.togglePin(id: pinned.id)
        store.clearUnpinned()

        XCTAssertEqual(store.items.map(\.id), [pinned.id])
        XCTAssertTrue(store.items[0].isPinned)

        let reloaded = makeStore(repository: repository)
        XCTAssertEqual(reloaded.items.map(\.id), [pinned.id])
        XCTAssertTrue(reloaded.items[0].isPinned)
    }

    func testClearAllRemovesPinnedItemsAndImageBlobsInOneOperation() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        let pinned = try XCTUnwrap(store.ingest(capture(
            text: "也要删除的置顶内容",
            fingerprint: "pinned",
            secondsFromNow: 0
        )))
        store.togglePin(id: pinned.id)

        let image = try XCTUnwrap(store.ingest(ClipboardCapture(
            kind: .image,
            imageData: Data([0x89, 0x50, 0x4E, 0x47, 1]),
            timestamp: referenceDate,
            fingerprint: "image-to-delete"
        )))
        XCTAssertNotNil(store.imageURL(for: image))

        store.clearAll()

        XCTAssertTrue(store.items.isEmpty)
        XCTAssertNil(store.selectedID)
        XCTAssertNil(store.imageURL(for: image))
        XCTAssertEqual(try repository.load(), [])
    }

    func testCorruptHistoryEntersReadOnlyModeWithoutOverwritingDataOrCleaningImages() throws {
        let repository = try makeRepository()
        let imageFileName = try repository.saveImage(
            Data([0x89, 0x50, 0x4E, 0x47, 1]),
            fileName: "recoverable.png"
        )
        let corruptHistory = Data("{ this is not valid TuckClip history".utf8)
        try corruptHistory.write(to: repository.historyURL, options: .atomic)

        let store = ClipboardStore(
            repository: repository,
            retentionDays: 1,
            maximumItemCount: 1,
            now: { self.referenceDate }
        )

        XCTAssertTrue(store.isReadOnlyDueToLoadFailure)
        let readErrorPrefix = L10n.text(
            "无法读取剪贴板历史。为保护现有数据，TuckClip 已进入只读模式：%@"
        ).replacingOccurrences(of: "%@", with: "")
        XCTAssertTrue(store.persistenceErrorDescription?.hasPrefix(readErrorPrefix) == true)
        XCTAssertTrue(store.items.isEmpty)

        XCTAssertNil(store.ingest(capture(
            text: "不得覆盖损坏历史",
            fingerprint: "protected",
            secondsFromNow: 0
        )))
        store.clearAll()
        store.applyLimits(now: referenceDate)

        XCTAssertEqual(try Data(contentsOf: repository.historyURL), corruptHistory)
        XCTAssertNotNil(repository.imageURL(forFileName: imageFileName))
    }

    func testFailedMetadataSaveRollsBackMemoryAndDefersImageCleanup() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        let image = try XCTUnwrap(store.ingest(ClipboardCapture(
            kind: .image,
            imageData: Data([0x89, 0x50, 0x4E, 0x47, 1, 2]),
            timestamp: referenceDate,
            fingerprint: "durable-image"
        )))
        let originalItems = store.items
        let backupURL = repository.rootDirectory
            .appendingPathComponent("history-backup.json", isDirectory: false)
        let fileManager = FileManager.default

        try fileManager.moveItem(at: repository.historyURL, to: backupURL)
        try fileManager.createDirectory(
            at: repository.historyURL,
            withIntermediateDirectories: false
        )
        defer {
            var isDirectory: ObjCBool = false
            if fileManager.fileExists(
                atPath: repository.historyURL.path,
                isDirectory: &isDirectory
            ), isDirectory.boolValue {
                try? fileManager.removeItem(at: repository.historyURL)
            }
            if fileManager.fileExists(atPath: backupURL.path),
               !fileManager.fileExists(atPath: repository.historyURL.path) {
                try? fileManager.moveItem(at: backupURL, to: repository.historyURL)
            }
        }

        store.clearAll()

        XCTAssertEqual(store.items, originalItems)
        XCTAssertEqual(store.selectedID, image.id)
        let saveErrorPrefix = L10n.text(
            "无法保存剪贴板历史：%@"
        ).replacingOccurrences(of: "%@", with: "")
        XCTAssertTrue(store.persistenceErrorDescription?.hasPrefix(saveErrorPrefix) == true)
        XCTAssertNotNil(store.imageURL(for: image))

        try fileManager.removeItem(at: repository.historyURL)
        try fileManager.moveItem(at: backupURL, to: repository.historyURL)
        XCTAssertEqual(try repository.load(), originalItems)
    }

    func testRetentionAndCountLimitsNeverRemovePinnedItems() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository, maximumItemCount: 20)

        let oldPinned = try XCTUnwrap(store.ingest(capture(
            text: "很早以前但已置顶",
            fingerprint: "old-pinned",
            secondsFromNow: -10 * 86_400
        )))
        store.togglePin(id: oldPinned.id)
        _ = store.ingest(capture(
            text: "很早以前",
            fingerprint: "old",
            secondsFromNow: -10 * 86_400 + 1
        ))
        _ = store.ingest(capture(
            text: "较新",
            fingerprint: "recent-1",
            secondsFromNow: -200
        ))
        let newest = try XCTUnwrap(store.ingest(capture(
            text: "最新",
            fingerprint: "recent-2",
            secondsFromNow: -100
        )))

        store.applyLimits(
            retentionDays: 1,
            maximumItemCount: 2,
            now: referenceDate
        )

        XCTAssertEqual(Set(store.items.map(\.id)), Set([oldPinned.id, newest.id]))
        XCTAssertTrue(store.items.contains(where: { $0.id == oldPinned.id && $0.isPinned }))
        XCTAssertFalse(store.items.contains(where: { $0.fingerprint == "old" }))
    }

    func testPinnedItemsMayExceedCountLimit() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository, maximumItemCount: 10)

        for index in 0..<3 {
            let item = try XCTUnwrap(store.ingest(capture(
                text: "置顶 \(index)",
                fingerprint: "pin-\(index)",
                secondsFromNow: TimeInterval(index)
            )))
            store.togglePin(id: item.id)
        }
        _ = store.ingest(capture(
            text: "非置顶",
            fingerprint: "ordinary",
            secondsFromNow: 10
        ))

        store.applyLimits(retentionDays: 0, maximumItemCount: 2, now: referenceDate)

        XCTAssertEqual(store.items.count, 3)
        XCTAssertTrue(store.items.allSatisfy(\.isPinned))
    }

    func testUnpinImmediatelyReappliesRetentionAndCountLimits() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository, maximumItemCount: 20)
        let expired = try XCTUnwrap(store.ingest(capture(
            text: "已经过期但暂时置顶",
            fingerprint: "expired-pinned",
            secondsFromNow: -10 * 86_400
        )))
        store.togglePin(id: expired.id)

        store.applyLimits(
            retentionDays: 1,
            maximumItemCount: 20,
            now: referenceDate
        )
        XCTAssertEqual(store.items.map(\.id), [expired.id])

        store.togglePin(id: expired.id)

        XCTAssertTrue(store.items.isEmpty)
        XCTAssertEqual(try repository.load(), [])

        let countLimitedStore = makeStore(
            repository: try makeRepository(),
            maximumItemCount: 20
        )
        var pinnedIDs: [UUID] = []
        for index in 0..<3 {
            let item = try XCTUnwrap(countLimitedStore.ingest(capture(
                text: "计数置顶 \(index)",
                fingerprint: "count-pinned-\(index)",
                secondsFromNow: TimeInterval(index)
            )))
            countLimitedStore.togglePin(id: item.id)
            pinnedIDs.append(item.id)
        }
        XCTAssertEqual(countLimitedStore.items.count, 3)
        countLimitedStore.applyLimits(
            retentionDays: 0,
            maximumItemCount: 2,
            now: referenceDate
        )
        XCTAssertEqual(countLimitedStore.items.count, 3)

        countLimitedStore.togglePin(id: pinnedIDs[0])

        XCTAssertEqual(countLimitedStore.items.count, 2)
        XCTAssertFalse(countLimitedStore.items.contains(where: { $0.id == pinnedIDs[0] }))
        XCTAssertTrue(countLimitedStore.items.allSatisfy(\.isPinned))
    }

    func testSearchIsCaseDiacriticWidthAndChineseInsensitive() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        _ = store.ingest(ClipboardCapture(
            kind: .text,
            plainText: "TuckClip 管理中文剪贴板，也保存 Café 笔记",
            sourceAppName: "微信读书",
            sourceBundleIdentifier: "com.example.reader",
            timestamp: referenceDate,
            fingerprint: "searchable"
        ))
        _ = store.ingest(capture(
            text: "unrelated",
            fingerprint: "other",
            secondsFromNow: -1
        ))

        store.searchQuery = "剪贴板"
        XCTAssertEqual(store.filteredItems.map(\.fingerprint), ["searchable"])

        store.searchQuery = "TUCKCLIP cafe"
        XCTAssertEqual(store.filteredItems.map(\.fingerprint), ["searchable"])

        store.searchQuery = "ｔｕｃｋｃｌｉｐ"
        XCTAssertEqual(store.filteredItems.map(\.fingerprint), ["searchable"])

        store.searchQuery = "微信"
        XCTAssertEqual(store.filteredItems.map(\.fingerprint), ["searchable"])
    }

    func testKindFilterSelectionAndWrappedMovementUseVisibleItems() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        _ = store.ingest(capture(
            text: "文本一",
            fingerprint: "text-1",
            secondsFromNow: -2
        ))
        _ = store.ingest(ClipboardCapture(
            kind: .link,
            plainText: "https://example.com",
            timestamp: referenceDate.addingTimeInterval(-1),
            fingerprint: "link"
        ))
        _ = store.ingest(capture(
            text: "文本二",
            fingerprint: "text-2",
            secondsFromNow: 0
        ))

        store.selectedKind = .text
        XCTAssertEqual(store.filteredItems.map(\.fingerprint), ["text-2", "text-1"])
        XCTAssertEqual(store.selectedID, store.filteredItems.first?.id)

        store.moveSelection(.next)
        XCTAssertEqual(store.selectedID, store.filteredItems.last?.id)
        store.moveSelection(.next)
        XCTAssertEqual(store.selectedID, store.filteredItems.first?.id)
        store.moveSelection(.previous)
        XCTAssertEqual(store.selectedID, store.filteredItems.last?.id)
    }

    func testInvalidCaptureIsIgnoredAndNotPersisted() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)

        XCTAssertNil(store.ingest(ClipboardCapture(
            kind: .text,
            plainText: nil,
            timestamp: referenceDate,
            fingerprint: "empty"
        )))
        XCTAssertNil(store.ingest(ClipboardCapture(
            kind: .files,
            timestamp: referenceDate,
            fingerprint: "no-files"
        )))
        XCTAssertTrue(store.items.isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: repository.historyURL.path))
    }

    func testOversizedTextIsIgnoredBeforeJSONPersistence() throws {
        let repository = try makeRepository()
        let store = makeStore(repository: repository)
        let oversizedText = String(
            repeating: "x",
            count: AppSettings.maximumTextCaptureSizeBytes + 1
        )

        XCTAssertNil(store.ingest(ClipboardCapture(
            kind: .text,
            plainText: oversizedText,
            timestamp: referenceDate,
            fingerprint: "oversized-text"
        )))
        XCTAssertTrue(store.items.isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: repository.historyURL.path))
    }

    private func makeStore(
        repository: HistoryRepository,
        maximumItemCount: Int = 20
    ) -> ClipboardStore {
        ClipboardStore(
            repository: repository,
            retentionDays: 0,
            maximumItemCount: maximumItemCount,
            now: { self.referenceDate }
        )
    }

    private func capture(
        text: String,
        fingerprint: String,
        secondsFromNow: TimeInterval,
        appName: String? = nil
    ) -> ClipboardCapture {
        ClipboardCapture(
            kind: .text,
            plainText: text,
            sourceAppName: appName,
            timestamp: referenceDate.addingTimeInterval(secondsFromNow),
            fingerprint: fingerprint
        )
    }

    private func makeRepository() throws -> HistoryRepository {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("TuckClipStoreTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(
            at: root,
            withIntermediateDirectories: true
        )
        addTeardownBlock {
            try? FileManager.default.removeItem(at: root)
        }
        return HistoryRepository(rootDirectory: root)
    }
}
