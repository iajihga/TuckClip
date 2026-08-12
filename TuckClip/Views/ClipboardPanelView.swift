import SwiftUI

extension Notification.Name {
    static let tuckClipFocusSearch = Notification.Name("TuckClip.focusSearch")
}

struct ClipboardPanelView: View {
    @ObservedObject var viewModel: ClipboardPanelViewModel
    @ObservedObject var settings: UISettingsStore
    let dismiss: () -> Void

    @FocusState private var searchIsFocused: Bool
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency

    var body: some View {
        VStack(spacing: 0) {
            header
            filterBar
            content
            footer
        }
        .background(background)
        .clipShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 28, style: .continuous)
                .strokeBorder(.white.opacity(0.12), lineWidth: 1)
        }
        .shadow(color: .black.opacity(0.48), radius: 30, y: 12)
        .padding(20)
        .onAppear { focusSearch() }
        .onReceive(NotificationCenter.default.publisher(for: .tuckClipFocusSearch)) { _ in
            focusSearch()
        }
        .onChange(of: viewModel.searchText) { _, _ in
            viewModel.ensureSelection()
        }
        .onChange(of: viewModel.selectedFilter) { _, _ in
            viewModel.ensureSelection()
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            ZStack {
                RoundedRectangle(cornerRadius: 11, style: .continuous)
                    .fill(TuckClipTheme.accentGradient)
                Image(systemName: "square.on.square.intersection.dashed")
                    .font(.system(size: 17, weight: .bold))
                    .foregroundStyle(.white)
            }
            .frame(width: 38, height: 38)
            .accessibilityHidden(true)

            HStack(spacing: 9) {
                Image(systemName: "magnifyingglass")
                    .foregroundStyle(.white.opacity(0.48))
                TextField(settings.localized("搜索内容或来源应用"), text: $viewModel.searchText)
                    .textFieldStyle(.plain)
                    .font(.system(size: 15, weight: .medium))
                    .foregroundStyle(.white)
                    .focused($searchIsFocused)
                    .accessibilityLabel(settings.localized("搜索剪贴板历史"))
                if !viewModel.searchText.isEmpty {
                    Button {
                        viewModel.searchText = ""
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(.white.opacity(0.42))
                    .accessibilityLabel(settings.localized("清除搜索"))
                }
            }
            .padding(.horizontal, 13)
            .frame(height: 38)
            .background(.white.opacity(0.075), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(.white.opacity(searchIsFocused ? 0.20 : 0.08), lineWidth: 1)
            }

            captureState

            Button(action: dismiss) {
                Image(systemName: "xmark")
                    .font(.system(size: 12, weight: .bold))
                    .frame(width: 28, height: 28)
                    .background(.white.opacity(0.08), in: Circle())
            }
            .buttonStyle(.plain)
            .foregroundStyle(.white.opacity(0.66))
            .keyboardShortcut(.cancelAction)
            .accessibilityLabel(settings.localized("关闭 TuckClip"))
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 12)
    }

    private var captureState: some View {
        Group {
            if let notice = viewModel.notice {
                HStack(spacing: 7) {
                    Image(systemName: notice.kind == .error ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                    Text(notice.message)
                        .lineLimit(1)
                }
                .foregroundStyle(notice.kind == .error ? Color.orange : TuckClipTheme.cyan)
            } else {
                HStack(spacing: 7) {
                    Circle()
                        .fill(captureStateColor)
                        .frame(width: 7, height: 7)
                        .shadow(
                            color: settings.recordingEnabled
                                && !settings.isStorageReadOnly
                                && settings.storageErrorDescription == nil
                                && settings.isPasteboardAccessReady
                                ? TuckClipTheme.cyan.opacity(0.65)
                                : .clear,
                            radius: 5
                        )
                    Text(settings.recordingStatusTitle)
                }
                .foregroundStyle(.white.opacity(0.60))
            }
        }
        .font(.caption.weight(.medium))
        .accessibilityElement(children: .combine)
        .animation(.easeOut(duration: 0.15), value: viewModel.notice)
    }

    private var captureStateColor: Color {
        guard settings.recordingEnabled else { return .secondary }
        if settings.isStorageReadOnly || settings.storageErrorDescription != nil {
            return .orange
        }
        return settings.isPasteboardAccessReady ? TuckClipTheme.cyan : .orange
    }

    private var filterBar: some View {
        ScrollView(.horizontal) {
            HStack(spacing: 7) {
                ForEach(ClipTypeFilter.allCases) { filter in
                    Button {
                        viewModel.selectedFilter = filter
                    } label: {
                        Label(filter.title(language: settings.appLanguage), systemImage: filter.symbolName)
                            .font(.caption.weight(.semibold))
                            .padding(.horizontal, 11)
                            .padding(.vertical, 7)
                            .foregroundStyle(
                                viewModel.selectedFilter == filter
                                    ? Color.white
                                    : Color.white.opacity(0.52)
                            )
                            .background {
                                Capsule()
                                    .fill(
                                        viewModel.selectedFilter == filter
                                            ? TuckClipTheme.indigo.opacity(0.72)
                                            : Color.white.opacity(0.055)
                                    )
                            }
                            .overlay {
                                Capsule()
                                    .strokeBorder(
                                        viewModel.selectedFilter == filter
                                            ? TuckClipTheme.cyan.opacity(0.44)
                                            : Color.white.opacity(0.06),
                                        lineWidth: 1
                                    )
                            }
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(viewModel.selectedFilter == filter ? .isSelected : [])
                }
            }
            .padding(.horizontal, 20)
        }
        .scrollIndicators(.hidden)
        .padding(.bottom, 12)
    }

    @ViewBuilder
    private var content: some View {
        if viewModel.filteredItems.isEmpty {
            VStack(spacing: 10) {
                Image(systemName: emptySymbol)
                    .font(.system(size: 28, weight: .medium))
                    .foregroundStyle(TuckClipTheme.accentGradient)
                Text(emptyTitle)
                    .font(.headline)
                    .foregroundStyle(.white.opacity(0.86))
                Text(emptyDetail)
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.46))
            }
            .frame(maxWidth: .infinity, minHeight: 178)
            .accessibilityElement(children: .combine)
        } else {
            ScrollViewReader { proxy in
                ScrollView(.horizontal) {
                    LazyHStack(spacing: 12) {
                        ForEach(Array(viewModel.filteredItems.enumerated()), id: \.element.id) { index, item in
                            ClipCardView(
                                item: item,
                                shortcutIndex: index < 9 ? index + 1 : nil,
                                isSelected: viewModel.selectedID == item.id,
                                onPaste: { plainText in
                                    viewModel.activate(item, asPlainText: plainText)
                                },
                                onTogglePin: { viewModel.togglePin(item) },
                                onDelete: { viewModel.delete(item) },
                                language: settings.appLanguage
                            )
                            .id(item.id)
                        }
                    }
                    .padding(.horizontal, 20)
                    .padding(.vertical, 8)
                }
                .scrollIndicators(.hidden)
                .onChange(of: viewModel.selectedID) { _, selectedID in
                    guard let selectedID else { return }
                    withAnimation(.easeOut(duration: 0.16)) {
                        proxy.scrollTo(selectedID, anchor: .center)
                    }
                }
            }
        }
    }

    private var footer: some View {
        HStack(spacing: 14) {
            Text(settings.localizedFormat("%d 项", viewModel.filteredItems.count))
                .monospacedDigit()

            Spacer()

            KeyboardHint(keys: "↑ ↓", action: settings.localized("选择"))
            KeyboardHint(keys: "↩", action: settings.localized("粘贴"))
            KeyboardHint(keys: "⌘↩", action: settings.localized("纯文本"))
            KeyboardHint(keys: "⌘D", action: settings.localized("置顶"))
        }
        .font(.caption2)
        .foregroundStyle(.white.opacity(0.44))
        .padding(.horizontal, 20)
        .padding(.top, 10)
        .padding(.bottom, 16)
    }

    @ViewBuilder
    private var background: some View {
        if reduceTransparency {
            TuckClipTheme.panelGradient
        } else {
            ZStack {
                Rectangle().fill(.ultraThickMaterial)
                TuckClipTheme.panelGradient.opacity(0.90)
                Circle()
                    .fill(TuckClipTheme.cyan.opacity(0.12))
                    .frame(width: 320, height: 320)
                    .blur(radius: 80)
                    .offset(x: 340, y: -150)
                Circle()
                    .fill(TuckClipTheme.indigo.opacity(0.14))
                    .frame(width: 360, height: 360)
                    .blur(radius: 90)
                    .offset(x: -380, y: 150)
            }
        }
    }

    private var emptySymbol: String {
        if settings.isStorageReadOnly {
            return "externaldrive.badge.exclamationmark"
        }
        if !settings.isPasteboardAccessReady {
            return "exclamationmark.triangle"
        }
        return viewModel.searchText.isEmpty ? "rectangle.stack.badge.plus" : "magnifyingglass"
    }

    private var emptyTitle: String {
        if settings.isStorageReadOnly {
            return settings.localized("历史已进入只读保护")
        }
        if !settings.isPasteboardAccessReady {
            return settings.localized("需要允许剪贴板访问")
        }
        return settings.localized(
            viewModel.searchText.isEmpty ? "等待你的下一次复制" : "没有找到匹配内容"
        )
    }

    private var emptyDetail: String {
        if settings.isStorageReadOnly {
            return settings.localized("原历史未被覆盖；请在设置的“存储”页定位文件并备份")
        }
        if !settings.isPasteboardAccessReady {
            return settings.localized("请在系统设置中允许后再复制")
        }
        if !viewModel.searchText.isEmpty {
            return settings.localized("试试缩短关键词或切换类型筛选")
        }
        if !settings.recordingEnabled {
            return settings.localizedFormat(
                "在设置或菜单栏中恢复记录；以后按 %@ 或点菜单栏图标回来",
                settings.hotKeyDisplayText
            )
        }
        return settings.localizedFormat(
            "复制文本、链接、图片或文件；以后按 %@ 或点菜单栏图标回来",
            settings.hotKeyDisplayText
        )
    }

    private func focusSearch() {
        DispatchQueue.main.async {
            searchIsFocused = true
        }
    }
}

private struct KeyboardHint: View {
    let keys: String
    let action: String

    var body: some View {
        HStack(spacing: 5) {
            Text(keys)
                .font(.caption2.monospaced().weight(.semibold))
                .padding(.horizontal, 5)
                .padding(.vertical, 2)
                .background(.white.opacity(0.07), in: RoundedRectangle(cornerRadius: 4))
            Text(action)
        }
        .accessibilityElement(children: .combine)
    }
}
