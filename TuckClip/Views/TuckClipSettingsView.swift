import AppKit
import SwiftUI

struct TuckClipSettingsView: View {
    @ObservedObject var settings: UISettingsStore
    @ObservedObject var panelViewModel: ClipboardPanelViewModel

    var body: some View {
        TabView(selection: $settings.selectedSettingsTab) {
            RecordingSettingsPane(settings: settings)
                .tabItem { Label(settings.localized("记录"), systemImage: "doc.on.clipboard") }
                .tag(TuckClipSettingsTab.recording)

            PrivacySettingsPane(settings: settings)
                .tabItem { Label(settings.localized("隐私"), systemImage: "hand.raised") }
                .tag(TuckClipSettingsTab.privacy)

            StorageSettingsPane(settings: settings, panelViewModel: panelViewModel)
                .tabItem { Label(settings.localized("存储"), systemImage: "internaldrive") }
                .tag(TuckClipSettingsTab.storage)
        }
        .tint(TuckClipTheme.indigo)
        .frame(width: 640, height: 440)
    }
}

private struct RecordingSettingsPane: View {
    @ObservedObject var settings: UISettingsStore

    var body: some View {
        Form {
            Section(settings.localized("快捷键")) {
                HStack(alignment: .center, spacing: 12) {
                    SettingsLabel(
                        title: settings.localized("唤起 TuckClip"),
                        detail: settings.hotKeyStatusText,
                        symbol: "keyboard"
                    )

                    Spacer()

                    Button(
                        settings.isRecordingHotKey
                            ? settings.localized("请按组合键…")
                            : settings.hotKeyDisplayText
                    ) {
                        settings.beginHotKeyRecording()
                    }
                    .buttonStyle(.bordered)

                    Button(settings.localized("恢复默认")) {
                        settings.restoreDefaultHotKey()
                    }
                    .disabled(settings.globalHotKey == .defaultValue)
                }
                .background {
                    if settings.isRecordingHotKey {
                        HotKeyCaptureView(
                            onCapture: settings.captureHotKey,
                            onCancel: settings.cancelHotKeyRecording
                        )
                        .frame(width: 1, height: 1)
                        .opacity(0.01)
                    }
                }

                if let error = settings.hotKeyErrorDescription {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                }
            }

            Section {
                Toggle(isOn: $settings.recordingEnabled) {
                    SettingsLabel(
                        title: settings.localized("记录剪贴板历史"),
                        detail: settings.localized("关闭后不会读取新的剪贴板内容"),
                        symbol: "waveform.path.ecg"
                    )
                }

                Toggle(isOn: $settings.automaticPasteEnabled) {
                    SettingsLabel(
                        title: settings.localized("选择后自动粘贴"),
                        detail: settings.localized("需要辅助功能权限；未授权时只恢复到系统剪贴板"),
                        symbol: "arrow.turn.down.right"
                    )
                }

                Toggle(isOn: $settings.capturesImages) {
                    SettingsLabel(
                        title: settings.localized("捕获图片"),
                        detail: settings.localized("图片占用空间较多，可随时关闭"),
                        symbol: "photo"
                    )
                }
            } header: {
                Text(settings.localized("记录行为"))
            }

            Section(settings.localized("容量")) {
                Picker(settings.localized("保留期"), selection: $settings.retentionDays) {
                    Text(settings.localized("1 天")).tag(1)
                    Text(settings.localized("7 天")).tag(7)
                    Text(settings.localized("30 天")).tag(30)
                    Text(settings.localized("90 天")).tag(90)
                    Text(settings.localized("1 年")).tag(365)
                }

                Picker(settings.localized("最大条数"), selection: $settings.maximumItemCount) {
                    Text("100").tag(100)
                    Text("500").tag(500)
                    Text("1,000").tag(1_000)
                    Text("5,000").tag(5_000)
                    Text("10,000").tag(10_000)
                }
            }

            Section(settings.localized("语言")) {
                Picker(settings.localized("界面语言"), selection: $settings.appLanguage) {
                    ForEach(AppLanguage.allCases) { language in
                        Text(language.displayName(in: settings.appLanguage)).tag(language)
                    }
                }
                Text(settings.localized("语言切换会立即应用"))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .padding(14)
    }
}

private struct PrivacySettingsPane: View {
    @ObservedObject var settings: UISettingsStore
    @Environment(\.scenePhase) private var scenePhase

    var body: some View {
        Form {
            if !settings.isAccessibilityTrusted {
                Section {
                    Label {
                        VStack(alignment: .leading, spacing: 5) {
                            Text(settings.localized("启用选择后自动粘贴"))
                                .font(.callout.weight(.semibold))
                            Text(settings.localized("1. 点击“请求权限”，让 macOS 登记 TuckClip。"))
                            Text(settings.localized("2. 打开辅助功能设置，并开启 TuckClip。"))
                            Text(settings.localized("3. 返回 TuckClip；授权状态会自动刷新。"))
                        }
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    } icon: {
                        Image(systemName: "hand.raised.fill")
                            .foregroundStyle(TuckClipTheme.indigo)
                    }

                    HStack {
                        Button(settings.localized("请求权限")) {
                            settings.requestAccessibilityAccess()
                        }
                        .buttonStyle(.borderedProminent)

                        Button(settings.localized("打开辅助功能设置")) {
                            settings.openAccessibilitySettings()
                        }
                    }
                } header: {
                    Text(settings.localized("首次设置"))
                }
            }

            Section(settings.localized("访问状态")) {
                HStack(alignment: .center, spacing: 12) {
                    StatusRow(
                        title: settings.localized("剪贴板"),
                        detail: settings.clipboardAccessSummary,
                        isReady: settings.isPasteboardAccessReady,
                        symbol: "clipboard"
                    )

                    Spacer()

                    if !settings.isPasteboardAccessReady
                        || settings.pasteboardAccessStatus.needsAlwaysAllowEducation {
                        Button(settings.localized("打开系统设置")) {
                            settings.openPrivacySettings()
                        }
                    }
                }

                HStack(alignment: .center, spacing: 12) {
                    StatusRow(
                        title: settings.localized("辅助功能"),
                        detail: settings.localized(
                            settings.isAccessibilityTrusted ? "已允许自动粘贴" : "未授权 · 当前仅复制"
                        ),
                        isReady: settings.isAccessibilityTrusted,
                        symbol: "accessibility"
                    )

                    Spacer()

                    if settings.isAccessibilityTrusted {
                        Button(settings.localized("刷新")) {
                            settings.refreshAccessibilityStatus()
                        }
                    } else {
                        Button(settings.localized("打开辅助功能设置")) {
                            settings.openAccessibilitySettings()
                        }
                    }
                }
            }

            Section {
                TextEditor(text: $settings.excludedBundleIdentifiersText)
                    .font(.system(.body, design: .monospaced))
                    .frame(minHeight: 118)
                    .padding(6)
                    .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 8))
                    .accessibilityLabel(settings.localized("排除的应用 Bundle ID，每行一个"))

                HStack {
                    Text(settings.localized("每行一个 Bundle ID；编辑后会自动保存。"))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button(settings.localized("恢复默认")) {
                        settings.restoreDefaultExcludedBundleIdentifiers()
                    }
                }
            } header: {
                Text(settings.localized("排除应用"))
            }
        }
        .formStyle(.grouped)
        .padding(14)
        .onAppear { settings.refreshAccessibilityStatus() }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active {
                settings.refreshAccessibilityStatus()
            }
        }
        .onDisappear {
            settings.cancelHotKeyRecording()
        }
    }
}

private struct StorageSettingsPane: View {
    @ObservedObject var settings: UISettingsStore
    @ObservedObject var panelViewModel: ClipboardPanelViewModel

    @State private var pendingClearAction: ClearAction?

    var body: some View {
        Form {
            Section(settings.localized("本地数据")) {
                if let storageError = settings.storageErrorDescription {
                    Label {
                        VStack(alignment: .leading, spacing: 3) {
                            Text(settings.localized(
                                settings.isStorageReadOnly ? "历史已进入只读保护" : "最近一次保存失败"
                            ))
                                .font(.callout.weight(.semibold))
                            Text(storageError)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .textSelection(.enabled)
                        }
                    } icon: {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                    }
                }

                LabeledContent(settings.localized("位置")) {
                    Text(settings.dataDirectory.path)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .multilineTextAlignment(.trailing)
                        .textSelection(.enabled)
                }

                HStack {
                    Text(settings.localized("历史数据库与图片仅保存在这台 Mac 上。"))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button(settings.localized("在 Finder 中显示")) {
                        revealDataDirectory()
                    }
                }
            }

            Section {
                HStack {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(settings.localized("清除未置顶历史"))
                        Text(settings.localized("保留你主动置顶的常用片段"))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(settings.localized("清除…")) { pendingClearAction = .unpinned }
                        .disabled(settings.isStorageReadOnly)
                }

                HStack {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(settings.localized("清除所有本地数据"))
                        Text(settings.localized("包括置顶项；此操作不可撤销"))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button(settings.localized("全部清除…"), role: .destructive) { pendingClearAction = .all }
                        .disabled(settings.isStorageReadOnly)
                }
            } header: {
                Text(settings.localized("清理"))
            }

            Section {
                Label {
                    Text(settings.localized("TuckClip 不包含账号、云同步、遥测或网络上传。"))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } icon: {
                    Image(systemName: "checkmark.shield.fill")
                        .foregroundStyle(TuckClipTheme.cyan)
                }
            }
        }
        .formStyle(.grouped)
        .padding(14)
        .alert(item: $pendingClearAction) { action in
            Alert(
                title: Text(settings.localized(action.title)),
                message: Text(settings.localized(action.message)),
                primaryButton: .destructive(Text(settings.localized("清除"))) {
                    switch action {
                    case .unpinned: panelViewModel.clearUnpinned()
                    case .all: panelViewModel.clearAll()
                    }
                },
                secondaryButton: .cancel()
            )
        }
    }

    private func revealDataDirectory() {
        let url = settings.dataDirectory
        if FileManager.default.fileExists(atPath: url.path) {
            NSWorkspace.shared.activateFileViewerSelecting([url])
        } else {
            NSWorkspace.shared.open(url.deletingLastPathComponent())
        }
    }
}

private struct SettingsLabel: View {
    let title: String
    let detail: String
    let symbol: String

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(TuckClipTheme.indigo)
                .frame(width: 24)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

private struct StatusRow: View {
    let title: String
    let detail: String
    let isReady: Bool
    let symbol: String

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(isReady ? TuckClipTheme.cyan : Color.secondary)
                .frame(width: 24)
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(detail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

private enum ClearAction: String, Identifiable {
    case unpinned
    case all

    var id: Self { self }

    var title: String {
        switch self {
        case .unpinned: "清除未置顶历史？"
        case .all: "清除所有本地数据？"
        }
    }

    var message: String {
        switch self {
        case .unpinned: "所有未置顶的文本、链接、图片和文件记录都会被删除。"
        case .all: "所有历史与置顶项都会被永久删除，无法撤销。"
        }
    }
}
