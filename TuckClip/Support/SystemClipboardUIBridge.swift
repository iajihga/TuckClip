import Combine
import Foundation

@MainActor
final class SystemClipboardUIBridge: ClipboardUIBridge {
    var onItemsChanged: (([ClipDisplayItem]) -> Void)? {
        didSet { publishItems(store.items) }
    }

    var dismissPanel: (() -> Void)?
    var onPasteResult: ((PasteResult) -> Void)?

    private let store: ClipboardStore
    private let monitor: ClipboardMonitor
    private let pasteService: PasteService
    private var cancellables: Set<AnyCancellable> = []
    private var panelSessionID = UUID()
    private var activePasteRequestID: UUID?
    private var pasteTask: Task<Void, Never>?

    init(
        store: ClipboardStore,
        monitor: ClipboardMonitor,
        pasteService: PasteService
    ) {
        self.store = store
        self.monitor = monitor
        self.pasteService = pasteService

        store.$items
            .sink { [weak self] items in
                self?.publishItems(items)
            }
            .store(in: &cancellables)
    }

    func startCapture() {
        monitor.start()
    }

    func stopCapture() {
        monitor.stop()
    }

    func refresh() {
        publishItems(store.items)
    }

    func beginPanelSession() {
        panelSessionID = UUID()
        cancelActivePaste()
    }

    func cancelPendingPaste() {
        panelSessionID = UUID()
        cancelActivePaste()
    }

    func paste(itemID: UUID, asPlainText: Bool) {
        // A paste owns the shared system clipboard until its optional Command-V
        // has been posted. A newer request supersedes and cancels the old one so
        // only one generation can dismiss the panel or synthesize a shortcut.
        guard let item = store.items.first(where: { $0.id == itemID }) else { return }
        cancelActivePaste()
        let sessionID = panelSessionID
        let requestID = UUID()
        activePasteRequestID = requestID
        let targetSnapshot = pasteService.captureTargetSnapshot()

        // The first version persists only normalized text, so text/link values
        // are already plain. For binary values Command-Return intentionally
        // behaves like Return rather than manufacturing a lossy representation.
        let imageURL = item.kind == .image ? store.imageURL(for: item) : nil
        pasteTask = Task { [weak self] in
            defer { self?.finishPasteRequest(requestID) }
            let imageData: Data?
            if let imageURL {
                imageData = await Task.detached(priority: .userInitiated) {
                    try? Data(contentsOf: imageURL)
                }.value
            } else {
                imageData = nil
            }

            guard let self,
                  isCurrent(sessionID: sessionID, requestID: requestID),
                  !Task.isCancelled else { return }
            let result = await pasteService.paste(
                item,
                imageData: imageData,
                asPlainText: asPlainText,
                // Selecting a history item is an explicit user action, so the
                // first automatic-paste attempt may ask macOS for event-posting
                // access. A declined request still safely falls back to copy-only.
                requestPermissionIfNeeded: true,
                targetSnapshot: targetSnapshot,
                beforeSendingPaste: { [weak self] in
                    guard let self,
                          isCurrent(sessionID: sessionID, requestID: requestID),
                          !Task.isCancelled else { return false }
                    dismissPanel?()
                    return true
                }
            )
            guard isCurrent(sessionID: sessionID, requestID: requestID),
                  !Task.isCancelled,
                  result != .cancelled else { return }
            onPasteResult?(result)
        }
    }

    func togglePin(itemID: UUID) {
        store.togglePin(id: itemID)
    }

    func delete(itemID: UUID) {
        store.delete(id: itemID)
    }

    func clearUnpinned() {
        store.clearUnpinned()
    }

    func clearAll() {
        store.clearAll()
    }

    private func publishItems(_ items: [ClipItem]) {
        onItemsChanged?(items.map(makeDisplayItem))
    }

    private func cancelActivePaste() {
        activePasteRequestID = nil
        pasteTask?.cancel()
        pasteTask = nil
    }

    private func isCurrent(sessionID: UUID, requestID: UUID) -> Bool {
        panelSessionID == sessionID && activePasteRequestID == requestID
    }

    private func finishPasteRequest(_ requestID: UUID) {
        guard activePasteRequestID == requestID else { return }
        activePasteRequestID = nil
        pasteTask = nil
    }

    private func makeDisplayItem(from item: ClipItem) -> ClipDisplayItem {
        let title: String
        let detail: String
        let searchableContent: String

        switch item.kind {
        case .text:
            let text = item.plainText?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            title = text.isEmpty ? "空文本" : Self.preview(text)
            detail = item.copyCount > 1 ? "复制 \(item.copyCount) 次" : ""
            searchableContent = text
        case .link:
            let text = item.plainText?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            title = URL(string: text)?.host ?? (text.isEmpty ? "链接" : Self.preview(text))
            detail = Self.preview(text)
            searchableContent = text
        case .image:
            title = "图片"
            detail = item.copyCount > 1 ? "复制 \(item.copyCount) 次" : "本地图片"
            searchableContent = ""
        case .files:
            let names = item.filePaths.map { URL(fileURLWithPath: $0).lastPathComponent }
            title = Self.preview(names.first ?? "文件", maximumCharacters: 160)
            if names.count > 1 {
                detail = Self.preview(
                    "\(names.count) 个文件 · \(names.dropFirst().prefix(2).joined(separator: "、"))"
                )
            } else {
                detail = Self.preview(item.filePaths.first ?? "")
            }
            searchableContent = item.filePaths.joined(separator: "\n")
        }

        return ClipDisplayItem(
            id: item.id,
            kind: ClipDisplayKind(rawValue: item.kind.rawValue) ?? .text,
            title: title,
            detail: detail,
            searchableContent: searchableContent,
            sourceName: item.sourceAppName ?? "未知应用",
            sourceBundleIdentifier: item.sourceBundleIdentifier,
            capturedAt: item.updatedAt,
            isPinned: item.isPinned,
            thumbnailURL: store.imageURL(for: item)
        )
    }

    private static func preview(_ text: String, maximumCharacters: Int = 700) -> String {
        guard let cutoff = text.index(
            text.startIndex,
            offsetBy: maximumCharacters,
            limitedBy: text.endIndex
        ), cutoff != text.endIndex else {
            return text
        }
        return String(text[..<cutoff]) + "…"
    }
}
