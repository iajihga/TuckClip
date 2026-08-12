import AppKit
import SwiftUI

struct ClipCardView: View {
    let item: ClipDisplayItem
    let shortcutIndex: Int?
    let isSelected: Bool
    let onPaste: (Bool) -> Void
    let onTogglePin: () -> Void
    let onDelete: () -> Void

    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @StateObject private var thumbnailLoader = ClipThumbnailLoader()

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                kindBadge
                Spacer(minLength: 8)
                if let shortcutIndex {
                    Text("⌘\(shortcutIndex)")
                        .font(.caption2.monospaced())
                        .foregroundStyle(.white.opacity(0.48))
                }
                if item.isPinned {
                    Image(systemName: "pin.fill")
                        .font(.caption)
                        .foregroundStyle(TuckClipTheme.cyan)
                        .accessibilityLabel("已置顶")
                }
            }

            preview
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)

            Divider()
                .overlay(.white.opacity(0.10))

            HStack(spacing: 8) {
                SourceAppIcon(bundleIdentifier: item.sourceBundleIdentifier)
                Text(item.sourceName)
                    .lineLimit(1)
                Spacer(minLength: 4)
                Text(item.capturedAt, style: .relative)
                    .lineLimit(1)
            }
            .font(.caption)
            .foregroundStyle(.white.opacity(0.58))
        }
        .padding(15)
        .frame(width: 248, height: 178)
        .background(cardBackground)
        .contentShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(selectionBorder)
        .shadow(
            color: isSelected ? TuckClipTheme.cyan.opacity(0.22) : .black.opacity(0.20),
            radius: isSelected ? 18 : 8,
            y: 5
        )
        .scaleEffect(isSelected && !reduceMotion ? 1.015 : 1)
        .animation(reduceMotion ? nil : .snappy(duration: 0.20), value: isSelected)
        .task(id: item.thumbnailURL) {
            guard item.kind == .image else { return }
            await thumbnailLoader.load(from: item.thumbnailURL)
        }
        // A clipboard picker's primary pointer action is paste. Keyboard arrow
        // navigation still changes selection without invoking this closure.
        .onTapGesture { onPaste(false) }
        .contextMenu {
            Button("粘贴") { onPaste(false) }
            if item.kind == .text || item.kind == .link {
                Button("以纯文本粘贴") { onPaste(true) }
            }
            Divider()
            Button(item.isPinned ? "取消置顶" : "置顶") { onTogglePin() }
            Button("删除", role: .destructive) { onDelete() }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(item.accessibilitySummary)
        .accessibilityAddTraits(isSelected ? [.isSelected] : [])
        .accessibilityAction(named: "粘贴") { onPaste(false) }
        .accessibilityAction(named: item.isPinned ? "取消置顶" : "置顶") { onTogglePin() }
        .accessibilityAction(named: "删除") { onDelete() }
    }

    private var kindBadge: some View {
        Label(item.kind.title, systemImage: item.kind.symbolName)
            .font(.caption.weight(.semibold))
            .foregroundStyle(item.kind.tint)
            .padding(.horizontal, 9)
            .padding(.vertical, 5)
            .background(item.kind.tint.opacity(0.12), in: Capsule())
    }

    @ViewBuilder
    private var preview: some View {
        if item.kind == .image,
           let image = thumbnailLoader.image {
            Image(decorative: image, scale: 1, orientation: .up)
                .resizable()
                .scaledToFit()
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        } else if item.kind == .image {
            VStack(spacing: 7) {
                if thumbnailLoader.state == .loading {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Image(systemName: thumbnailLoader.state == .unavailable
                        ? "photo.badge.exclamationmark"
                        : "photo")
                        .font(.title2)
                }
                Text(thumbnailLoader.state == .unavailable ? "图片文件不可用" : "正在载入预览")
                    .font(.caption)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .foregroundStyle(.white.opacity(0.52))
        } else {
            VStack(alignment: .leading, spacing: 6) {
                Text(item.title.isEmpty ? "无标题内容" : item.title)
                    .font(item.kind == .text ? .body : .headline)
                    .fontWeight(item.kind == .text ? .regular : .semibold)
                    .foregroundStyle(.white.opacity(0.94))
                    .lineLimit(item.detail.isEmpty ? 4 : 2)
                    .textSelection(.disabled)

                if !item.detail.isEmpty {
                    Text(item.detail)
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.52))
                        .lineLimit(2)
                }
            }
        }
    }

    private var cardBackground: some View {
        RoundedRectangle(cornerRadius: 20, style: .continuous)
            .fill(
                LinearGradient(
                    colors: [
                        Color.white.opacity(isSelected ? 0.13 : 0.09),
                        item.kind.tint.opacity(isSelected ? 0.10 : 0.035)
                    ],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
            )
    }

    private var selectionBorder: some View {
        RoundedRectangle(cornerRadius: 20, style: .continuous)
            .strokeBorder(
                isSelected ? AnyShapeStyle(TuckClipTheme.accentGradient) : AnyShapeStyle(.white.opacity(0.08)),
                lineWidth: isSelected ? 2 : 1
            )
    }
}

private struct SourceAppIcon: View {
    let bundleIdentifier: String?

    var body: some View {
        Group {
            if let icon {
                Image(nsImage: icon)
                    .resizable()
                    .scaledToFit()
            } else {
                Image(systemName: "app.dashed")
                    .resizable()
                    .scaledToFit()
                    .foregroundStyle(.white.opacity(0.50))
            }
        }
        .frame(width: 16, height: 16)
        .accessibilityHidden(true)
    }

    private var icon: NSImage? {
        guard let bundleIdentifier,
              let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleIdentifier) else {
            return nil
        }
        return NSWorkspace.shared.icon(forFile: url.path)
    }
}
