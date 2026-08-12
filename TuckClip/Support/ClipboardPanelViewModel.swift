import AppKit
import Combine
import Foundation

enum ClipDisplayKind: String, CaseIterable, Identifiable, Codable {
    case text
    case link
    case image
    case files

    var id: Self { self }

    func title(language: AppLanguage? = nil) -> String {
        switch self {
        case .text: L10n.text("文本", language: language)
        case .link: L10n.text("链接", language: language)
        case .image: L10n.text("图片", language: language)
        case .files: L10n.text("文件", language: language)
        }
    }

    var title: String { title() }

    var symbolName: String {
        switch self {
        case .text: "text.alignleft"
        case .link: "link"
        case .image: "photo"
        case .files: "doc.on.doc"
        }
    }
}

enum ClipTypeFilter: String, CaseIterable, Identifiable {
    case all
    case text
    case link
    case image
    case files

    var id: Self { self }

    func title(language: AppLanguage? = nil) -> String {
        switch self {
        case .all: L10n.text("全部", language: language)
        case .text: L10n.text("文本", language: language)
        case .link: L10n.text("链接", language: language)
        case .image: L10n.text("图片", language: language)
        case .files: L10n.text("文件", language: language)
        }
    }

    var title: String { title() }

    var symbolName: String {
        switch self {
        case .all: "square.grid.2x2"
        case .text: ClipDisplayKind.text.symbolName
        case .link: ClipDisplayKind.link.symbolName
        case .image: ClipDisplayKind.image.symbolName
        case .files: ClipDisplayKind.files.symbolName
        }
    }

    func includes(_ item: ClipDisplayItem) -> Bool {
        switch self {
        case .all: true
        case .text: item.kind == .text
        case .link: item.kind == .link
        case .image: item.kind == .image
        case .files: item.kind == .files
        }
    }
}

/// A deliberately small UI boundary. Core persistence models are adapted to this
/// value type in AppDelegate, so the panel does not need to know database details.
struct ClipDisplayItem: Identifiable, Hashable {
    let id: UUID
    var kind: ClipDisplayKind
    var title: String
    var detail: String
    var searchableContent: String
    var sourceName: String
    var sourceBundleIdentifier: String?
    var capturedAt: Date
    var isPinned: Bool
    var thumbnailData: Data?
    var thumbnailURL: URL?

    init(
        id: UUID,
        kind: ClipDisplayKind,
        title: String,
        detail: String = "",
        searchableContent: String = "",
        sourceName: String,
        sourceBundleIdentifier: String? = nil,
        capturedAt: Date,
        isPinned: Bool,
        thumbnailData: Data? = nil,
        thumbnailURL: URL? = nil
    ) {
        self.id = id
        self.kind = kind
        self.title = title
        self.detail = detail
        self.searchableContent = searchableContent
        self.sourceName = sourceName
        self.sourceBundleIdentifier = sourceBundleIdentifier
        self.capturedAt = capturedAt
        self.isPinned = isPinned
        self.thumbnailData = thumbnailData
        self.thumbnailURL = thumbnailURL
    }

    func accessibilitySummary(language: AppLanguage? = nil) -> String {
        let pinState = isPinned ? L10n.text("，已置顶", language: language) : ""
        return L10n.format(
            "%@，%@，来源 %@%@",
            language: language,
            kind.title(language: language),
            title,
            sourceName,
            pinState
        )
    }

    var accessibilitySummary: String { accessibilitySummary() }
}

struct PanelNotice: Identifiable, Equatable {
    enum Kind: Equatable {
        case copied
        case error
    }

    let id = UUID()
    let kind: Kind
    let message: String
}

@MainActor
protocol ClipboardUIBridge: AnyObject {
    var onItemsChanged: (([ClipDisplayItem]) -> Void)? { get set }

    func startCapture()
    func stopCapture()
    func refresh()
    func beginPanelSession()
    func cancelPendingPaste()
    func paste(itemID: UUID, asPlainText: Bool)
    func togglePin(itemID: UUID)
    func delete(itemID: UUID)
    func clearUnpinned()
    func clearAll()
}

/// Used until the concrete history service is installed. Keeping the fallback
/// inert makes previews and test hosts safe: it never touches NSPasteboard.
@MainActor
final class EmptyClipboardUIBridge: ClipboardUIBridge {
    var onItemsChanged: (([ClipDisplayItem]) -> Void)?

    func startCapture() {}
    func stopCapture() {}
    func refresh() { onItemsChanged?([]) }
    func beginPanelSession() {}
    func cancelPendingPaste() {}
    func paste(itemID: UUID, asPlainText: Bool) {}
    func togglePin(itemID: UUID) {}
    func delete(itemID: UUID) {}
    func clearUnpinned() { onItemsChanged?([]) }
    func clearAll() { onItemsChanged?([]) }
}

@MainActor
final class ClipboardPanelViewModel: ObservableObject {
    @Published private(set) var items: [ClipDisplayItem] = []
    @Published private(set) var notice: PanelNotice?
    @Published var searchText = ""
    @Published var selectedFilter: ClipTypeFilter = .all
    @Published var selectedID: UUID?
    @Published private(set) var presentationGeneration = 0

    weak var bridge: ClipboardUIBridge?
    private var noticeDismissTask: Task<Void, Never>?

    var filteredItems: [ClipDisplayItem] {
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        return items.filter { item in
            guard selectedFilter.includes(item) else { return false }
            guard !query.isEmpty else { return true }
            return item.title.localizedCaseInsensitiveContains(query)
                || item.detail.localizedCaseInsensitiveContains(query)
                || item.searchableContent.localizedCaseInsensitiveContains(query)
                || item.sourceName.localizedCaseInsensitiveContains(query)
        }
    }

    var selectedItem: ClipDisplayItem? {
        guard let selectedID else { return filteredItems.first }
        return filteredItems.first { $0.id == selectedID } ?? filteredItems.first
    }

    func replaceItems(_ newItems: [ClipDisplayItem]) {
        items = newItems
        repairSelection()
    }

    func select(_ id: UUID) {
        selectedID = id
    }

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

        let nextIndex = min(max(currentIndex + offset, 0), visibleItems.count - 1)
        self.selectedID = visibleItems[nextIndex].id
    }

    func selectVisibleItem(at index: Int) {
        guard filteredItems.indices.contains(index) else { return }
        selectedID = filteredItems[index].id
    }

    func ensureSelection() {
        repairSelection()
    }

    /// Starts each visible panel presentation from the newest item in the
    /// current search/filter result. The generation also lets the view reset
    /// its horizontal scroll position when the selected item was already first.
    func prepareForPresentation() {
        selectedID = filteredItems.first?.id
        presentationGeneration &+= 1
    }

    func beginPanelSession() {
        bridge?.beginPanelSession()
    }

    func cancelPendingPaste() {
        bridge?.cancelPendingPaste()
    }

    func pasteSelected(asPlainText: Bool = false) {
        guard let item = selectedItem else { return }
        bridge?.paste(itemID: item.id, asPlainText: asPlainText)
    }

    /// Performs the panel's primary item action. Pointer clicks, Return and
    /// numbered shortcuts paste; arrow-key navigation calls `select` instead.
    func activate(_ item: ClipDisplayItem, asPlainText: Bool = false) {
        selectedID = item.id
        bridge?.paste(itemID: item.id, asPlainText: asPlainText)
    }

    func togglePinSelected() {
        guard let item = selectedItem else { return }
        togglePin(item)
    }

    func togglePin(_ item: ClipDisplayItem) {
        bridge?.togglePin(itemID: item.id)
    }

    func deleteSelected() {
        guard let item = selectedItem else { return }
        delete(item)
    }

    func delete(_ item: ClipDisplayItem) {
        bridge?.delete(itemID: item.id)
    }

    func clearUnpinned() {
        bridge?.clearUnpinned()
    }

    func clearAll() {
        bridge?.clearAll()
    }

    func showPasteResult(_ result: PasteResult) {
        switch result {
        case .pasted:
            noticeDismissTask?.cancel()
            notice = nil
            return
        case .cancelled:
            return
        case .copiedOnly(let reason):
            let message: String
            switch reason {
            case .automaticPasteDisabled:
                message = L10n.text("已复制到剪贴板")
            case .eventPostingPermissionDenied:
                message = L10n.text("已复制，请按 ⌘V；自动粘贴可在设置中授权")
            case .targetApplicationUnavailable:
                message = L10n.text("已复制；没有可自动粘贴的目标")
            case .targetActivationFailed:
                message = L10n.text("已复制；无法切回目标应用，请按 ⌘V")
            case .clipboardContentsChanged:
                message = L10n.text("剪贴板已被其他应用改写；为避免粘错，已取消自动粘贴")
            case .keyboardEventCreationFailed:
                message = L10n.text("已复制；自动粘贴失败，请按 ⌘V")
            }
            notice = PanelNotice(
                kind: reason == .clipboardContentsChanged ? .error : .copied,
                message: message
            )
        case .failed(let error):
            notice = PanelNotice(
                kind: .error,
                message: error.errorDescription ?? L10n.text("写入系统剪贴板失败")
            )
        }

        noticeDismissTask?.cancel()
        let noticeID = notice?.id
        noticeDismissTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(3.5))
            guard !Task.isCancelled, self?.notice?.id == noticeID else { return }
            self?.notice = nil
        }
    }

    func refreshLocalization() {
        objectWillChange.send()
    }

    private func repairSelection() {
        let visibleItems = filteredItems
        if let selectedID, visibleItems.contains(where: { $0.id == selectedID }) {
            return
        }
        selectedID = visibleItems.first?.id
    }
}
