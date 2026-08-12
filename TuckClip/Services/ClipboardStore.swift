import Combine
import Foundation

enum ClipSelectionDirection: Int, Sendable {
    case previous = -1
    case next = 1
}

/// Main-thread state and policy for the clipboard history UI.
@MainActor
final class ClipboardStore: ObservableObject {
    @Published private(set) var items: [ClipItem]
    @Published var searchQuery: String {
        didSet { ensureSelectionIsVisible() }
    }
    @Published var selectedKind: ClipKind? {
        didSet { ensureSelectionIsVisible() }
    }
    @Published var selectedID: UUID?
    @Published private(set) var persistenceErrorDescription: String?
    @Published private(set) var isReadOnlyDueToLoadFailure: Bool

    private(set) var retentionDays: Int
    private(set) var maximumItemCount: Int

    private let repository: HistoryRepository
    private let now: () -> Date

    var filteredItems: [ClipItem] {
        filteredItems(from: items)
    }

    init(
        repository: HistoryRepository = HistoryRepository(),
        retentionDays: Int = 30,
        maximumItemCount: Int = 500,
        now: @escaping () -> Date = Date.init
    ) {
        self.repository = repository
        self.retentionDays = max(0, retentionDays)
        self.maximumItemCount = max(1, maximumItemCount)
        self.now = now
        self.items = []
        self.searchQuery = ""
        self.selectedKind = nil
        self.selectedID = nil
        self.persistenceErrorDescription = nil
        self.isReadOnlyDueToLoadFailure = false

        do {
            let loadedItems = Self.sorted(try repository.load())
            items = loadedItems

            let limitedItems = Self.applyingLimits(
                to: loadedItems,
                retentionDays: self.retentionDays,
                maximumItemCount: self.maximumItemCount,
                now: now()
            )

            if limitedItems != loadedItems {
                // If this write fails, `commit` leaves the successfully decoded
                // in-memory snapshot untouched and skips blob cleanup.
                _ = commit(limitedItems, preferredSelection: loadedItems.first?.id)
            } else {
                cleanupOrphanedImages()
            }
        } catch {
            // A corrupt, incompatible or temporarily unreadable history may still
            // be recoverable. Never replace it with an empty snapshot, and never
            // infer that its image blobs are orphans.
            items = []
            selectedID = nil
            isReadOnlyDueToLoadFailure = true
            persistenceErrorDescription = "无法读取剪贴板历史。为保护现有数据，TuckClip 已进入只读模式：\(error.localizedDescription)"
        }

        selectedID = repairedSelection(in: items, preferred: selectedID)
    }

    /// Adds a capture or merges it with an existing item that has the same
    /// normalized fingerprint and kind. Returns `nil` for empty/invalid input,
    /// protected read-only storage, or a failed persistence operation.
    @discardableResult
    func ingest(_ capture: ClipboardCapture) -> ClipItem? {
        guard canMutatePersistedHistory(), isValid(capture) else {
            return nil
        }

        if let duplicateIndex = items.firstIndex(where: {
            $0.kind == capture.kind && $0.fingerprint == capture.fingerprint
        }) {
            var item = items[duplicateIndex]
            item.updatedAt = max(item.updatedAt, capture.timestamp)
            item.plainText = capture.plainText ?? item.plainText
            if !capture.filePaths.isEmpty {
                item.filePaths = capture.filePaths
            }
            item.sourceAppName = capture.sourceAppName ?? item.sourceAppName
            item.sourceBundleIdentifier = capture.sourceBundleIdentifier ?? item.sourceBundleIdentifier
            if item.copyCount < Int.max {
                item.copyCount += 1
            }

            var newlySavedImageFileName: String?
            if capture.kind == .image,
               repository.imageURL(forFileName: item.imageFileName) == nil,
               let imageData = capture.imageData {
                do {
                    let fileName = try repository.saveImage(imageData)
                    item.imageFileName = fileName
                    newlySavedImageFileName = fileName
                } catch {
                    persistenceErrorDescription = "无法保存剪贴板图片：\(error.localizedDescription)"
                    return nil
                }
            }

            var proposedItems = items
            proposedItems[duplicateIndex] = item
            proposedItems = limited(proposedItems, now: now())

            guard commit(proposedItems, preferredSelection: item.id) else {
                rollbackUncommittedImage(named: newlySavedImageFileName)
                return nil
            }
            return items.first(where: { $0.id == item.id })
        }

        var imageFileName: String?
        if capture.kind == .image, let imageData = capture.imageData {
            do {
                imageFileName = try repository.saveImage(imageData)
            } catch {
                persistenceErrorDescription = "无法保存剪贴板图片：\(error.localizedDescription)"
                return nil
            }
        }

        let item = ClipItem(
            kind: capture.kind,
            plainText: capture.plainText,
            filePaths: capture.filePaths,
            imageFileName: imageFileName,
            createdAt: capture.timestamp,
            sourceAppName: capture.sourceAppName,
            sourceBundleIdentifier: capture.sourceBundleIdentifier,
            fingerprint: capture.fingerprint
        )

        var proposedItems = items
        proposedItems.append(item)
        proposedItems = limited(proposedItems, now: now())

        guard commit(proposedItems, preferredSelection: item.id) else {
            rollbackUncommittedImage(named: imageFileName)
            return nil
        }
        return items.first(where: { $0.id == item.id })
    }

    func togglePin(id: UUID) {
        guard canMutatePersistedHistory(),
              let index = items.firstIndex(where: { $0.id == id }) else {
            return
        }

        var proposedItems = items
        proposedItems[index].isPinned.toggle()

        // In particular, unpinning an expired or over-limit item must remove it
        // immediately instead of retaining it until another copy or app restart.
        proposedItems = limited(proposedItems, now: now())
        _ = commit(proposedItems, preferredSelection: selectedID)
    }

    func togglePin(_ item: ClipItem) {
        togglePin(id: item.id)
    }

    func delete(id: UUID) {
        guard canMutatePersistedHistory(),
              let index = items.firstIndex(where: { $0.id == id }) else {
            return
        }

        let visibleBeforeDeletion = filteredItems
        let selectedIndex = visibleBeforeDeletion.firstIndex(where: { $0.id == selectedID })
        var proposedItems = items
        proposedItems.remove(at: index)
        proposedItems = Self.sorted(proposedItems)

        let preferredSelection: UUID?
        if selectedID == id {
            let remainingVisibleItems = filteredItems(from: proposedItems)
            if remainingVisibleItems.isEmpty {
                preferredSelection = nil
            } else {
                preferredSelection = remainingVisibleItems[
                    min(selectedIndex ?? 0, remainingVisibleItems.count - 1)
                ].id
            }
        } else {
            preferredSelection = selectedID
        }

        _ = commit(proposedItems, preferredSelection: preferredSelection)
    }

    func delete(_ item: ClipItem) {
        delete(id: item.id)
    }

    /// Deletes ordinary history while retaining every pinned item and its blob.
    func clearUnpinned() {
        guard canMutatePersistedHistory() else { return }
        let proposedItems = Self.sorted(items.filter(\.isPinned))
        _ = commit(proposedItems, preferredSelection: selectedID)
    }

    /// Removes every record, then removes image blobs only after the empty
    /// metadata snapshot has committed successfully.
    func clearAll() {
        guard canMutatePersistedHistory() else { return }
        _ = commit([], preferredSelection: nil)
    }

    /// Applies the current limits. A retention value of zero disables age-based
    /// pruning. Pinned items are exempt from automatic removal.
    func applyLimits(now date: Date? = nil) {
        guard canMutatePersistedHistory() else { return }
        let proposedItems = limited(items, now: date ?? now())
        guard proposedItems != items else { return }
        _ = commit(proposedItems, preferredSelection: selectedID)
    }

    func applyLimits(
        retentionDays: Int,
        maximumItemCount: Int,
        now date: Date? = nil
    ) {
        self.retentionDays = max(0, retentionDays)
        self.maximumItemCount = max(1, maximumItemCount)
        applyLimits(now: date)
    }

    func moveSelection(_ direction: ClipSelectionDirection) {
        moveSelection(by: direction.rawValue)
    }

    func moveSelection(_ offset: Int) {
        moveSelection(by: offset)
    }

    /// Moves through the visible result set and wraps at either end.
    func moveSelection(by offset: Int) {
        let visibleItems = filteredItems
        guard !visibleItems.isEmpty else {
            selectedID = nil
            return
        }

        guard let selectedID,
              let currentIndex = visibleItems.firstIndex(where: { $0.id == selectedID }) else {
            self.selectedID = offset < 0 ? visibleItems.last?.id : visibleItems.first?.id
            return
        }

        let count = visibleItems.count
        let remainder = (currentIndex + offset) % count
        let nextIndex = remainder >= 0 ? remainder : remainder + count
        self.selectedID = visibleItems[nextIndex].id
    }

    func imageURL(for item: ClipItem) -> URL? {
        repository.imageURL(forFileName: item.imageFileName)
    }

    func imageURL(for itemID: UUID) -> URL? {
        guard let item = items.first(where: { $0.id == itemID }) else {
            return nil
        }
        return imageURL(for: item)
    }

    private func canMutatePersistedHistory() -> Bool {
        guard !isReadOnlyDueToLoadFailure else {
            if persistenceErrorDescription == nil {
                persistenceErrorDescription = "无法加载剪贴板历史，当前处于只读保护模式。"
            }
            republishCurrentItems()
            return false
        }
        return true
    }

    private func isValid(_ capture: ClipboardCapture) -> Bool {
        guard !capture.fingerprint.isEmpty else {
            return false
        }

        switch capture.kind {
        case .text, .link:
            guard let plainText = capture.plainText, !plainText.isEmpty else {
                return false
            }
            return plainText.lengthOfBytes(using: .utf8)
                <= AppSettings.maximumTextCaptureSizeBytes
        case .image:
            guard let imageData = capture.imageData, !imageData.isEmpty else {
                return false
            }
            return imageData.count <= AppSettings.maximumCaptureSizeBytes
        case .files:
            guard !capture.filePaths.isEmpty else { return false }
            var byteCount = 0
            for path in capture.filePaths {
                let pathByteCount = path.lengthOfBytes(using: .utf8) + 1
                guard pathByteCount <= AppSettings.maximumCaptureSizeBytes - byteCount else {
                    return false
                }
                byteCount += pathByteCount
            }
            return true
        }
    }

    private func limited(_ sourceItems: [ClipItem], now date: Date) -> [ClipItem] {
        Self.applyingLimits(
            to: sourceItems,
            retentionDays: retentionDays,
            maximumItemCount: maximumItemCount,
            now: date
        )
    }

    private static func applyingLimits(
        to sourceItems: [ClipItem],
        retentionDays: Int,
        maximumItemCount: Int,
        now date: Date
    ) -> [ClipItem] {
        var result = sorted(sourceItems)

        if retentionDays > 0,
           let cutoff = Calendar(identifier: .gregorian).date(
               byAdding: .day,
               value: -retentionDays,
               to: date
           ) {
            result.removeAll { !$0.isPinned && $0.updatedAt <= cutoff }
        }

        let pinnedCount = result.lazy.filter(\.isPinned).count
        let allowedUnpinnedCount = max(0, max(1, maximumItemCount) - pinnedCount)
        var seenUnpinned = 0

        result.removeAll { item in
            guard !item.isPinned else {
                return false
            }
            seenUnpinned += 1
            return seenUnpinned > allowedUnpinnedCount
        }
        return result
    }

    /// Commits metadata before publishing it. A failed save therefore leaves the
    /// observable in-memory snapshot and selection unchanged. Blob cleanup runs
    /// only after the new metadata is durable.
    @discardableResult
    private func commit(
        _ proposedItems: [ClipItem],
        preferredSelection: UUID?
    ) -> Bool {
        guard canMutatePersistedHistory() else { return false }
        let committedItems = Self.sorted(proposedItems)

        do {
            try repository.save(committedItems)
        } catch {
            persistenceErrorDescription = "无法保存剪贴板历史：\(error.localizedDescription)"
            // The panel view model performs optimistic local edits. Re-emitting
            // the unchanged durable snapshot rolls those edits back as well.
            republishCurrentItems()
            return false
        }

        items = committedItems
        selectedID = repairedSelection(in: committedItems, preferred: preferredSelection)

        do {
            try repository.cleanupOrphanedImages(referencedBy: committedItems)
            persistenceErrorDescription = nil
        } catch {
            // Metadata is already committed and must not be rolled back to items
            // that may reference blobs cleanup removed before encountering error.
            // A later successful commit or app launch retries orphan cleanup.
            persistenceErrorDescription = "历史已保存，但无法清理无引用图片：\(error.localizedDescription)"
        }
        return true
    }

    private func rollbackUncommittedImage(named fileName: String?) {
        guard let fileName else { return }
        do {
            try repository.deleteImage(named: fileName)
        } catch {
            let originalError = persistenceErrorDescription.map { "\($0)；" } ?? ""
            persistenceErrorDescription = "\(originalError)未提交的图片无法清理：\(error.localizedDescription)"
        }
    }

    private func republishCurrentItems() {
        let currentItems = items
        items = currentItems
        selectedID = repairedSelection(in: currentItems, preferred: selectedID)
    }

    private func ensureSelectionIsVisible() {
        selectedID = repairedSelection(in: items, preferred: selectedID)
    }

    private func repairedSelection(
        in sourceItems: [ClipItem],
        preferred: UUID?
    ) -> UUID? {
        let visibleItems = filteredItems(from: sourceItems)
        guard !visibleItems.isEmpty else { return nil }
        if let preferred,
           visibleItems.contains(where: { $0.id == preferred }) {
            return preferred
        }
        return visibleItems.first?.id
    }

    private func filteredItems(from sourceItems: [ClipItem]) -> [ClipItem] {
        let queryTokens = Self.searchTokens(from: searchQuery)

        return sourceItems.filter { item in
            guard selectedKind == nil || item.kind == selectedKind else {
                return false
            }
            guard !queryTokens.isEmpty else {
                return true
            }

            let searchableText = Self.normalizedSearchText(
                [
                    item.plainText,
                    item.sourceAppName,
                    item.sourceBundleIdentifier,
                    item.filePaths.joined(separator: "\n")
                ]
                .compactMap { $0 }
                .joined(separator: "\n")
            )

            return queryTokens.allSatisfy(searchableText.contains)
        }
    }

    private func cleanupOrphanedImages() {
        guard !isReadOnlyDueToLoadFailure else { return }
        do {
            try repository.cleanupOrphanedImages(referencedBy: items)
            persistenceErrorDescription = nil
        } catch {
            persistenceErrorDescription = "无法清理无引用图片：\(error.localizedDescription)"
        }
    }

    private static func sorted(_ items: [ClipItem]) -> [ClipItem] {
        items.sorted { lhs, rhs in
            if lhs.updatedAt != rhs.updatedAt {
                return lhs.updatedAt > rhs.updatedAt
            }
            if lhs.createdAt != rhs.createdAt {
                return lhs.createdAt > rhs.createdAt
            }
            return lhs.id.uuidString < rhs.id.uuidString
        }
    }

    private static func searchTokens(from query: String) -> [String] {
        normalizedSearchText(query)
            .split(whereSeparator: { $0.isWhitespace })
            .map(String.init)
    }

    private static func normalizedSearchText(_ text: String) -> String {
        text.folding(
            options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive],
            locale: Locale(identifier: "zh_Hans_CN")
        )
    }
}
