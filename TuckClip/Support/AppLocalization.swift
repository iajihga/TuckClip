import Foundation

enum AppLanguage: String, CaseIterable, Identifiable, Sendable {
    case system
    case simplifiedChinese = "zh-Hans"
    case english = "en"

    nonisolated var id: String { rawValue }

    nonisolated func resolved(
        preferredLanguages: [String] = Locale.preferredLanguages
    ) -> AppLanguage {
        guard self == .system else { return self }
        return preferredLanguages.first?.lowercased().hasPrefix("zh") == true
            ? .simplifiedChinese
            : .english
    }

    nonisolated func displayName(in language: AppLanguage) -> String {
        switch self {
        case .system:
            L10n.text("跟随系统", language: language)
        case .simplifiedChinese:
            L10n.text("简体中文", language: language)
        case .english:
            "English"
        }
    }
}

/// Runtime localization shared by SwiftUI, AppKit menus and service errors.
///
/// Source-language text is used as the stable lookup key. This keeps call sites
/// readable and lets a missing translation fail safely to the complete Chinese
/// source string instead of showing an opaque identifier.
enum L10n {
    nonisolated static let settingsKey = "settings.appLanguage"

    nonisolated static func persistedLanguage(
        defaults: UserDefaults = .standard
    ) -> AppLanguage {
        guard let rawValue = defaults.string(forKey: settingsKey),
              let language = AppLanguage(rawValue: rawValue) else {
            return .system
        }
        return language
    }

    nonisolated static func text(
        _ source: String,
        language: AppLanguage? = nil
    ) -> String {
        let preference = language ?? persistedLanguage()
        switch preference.resolved() {
        case .simplifiedChinese:
            return source
        case .english:
            return english[source] ?? source
        case .system:
            return source
        }
    }

    nonisolated static func format(
        _ source: String,
        language: AppLanguage? = nil,
        _ arguments: CVarArg...
    ) -> String {
        let preference = language ?? persistedLanguage()
        let format = text(source, language: preference)
        let localeIdentifier = preference.resolved() == .simplifiedChinese
            ? "zh-Hans"
            : "en"
        return String(
            format: format,
            locale: Locale(identifier: localeIdentifier),
            arguments: arguments
        )
    }

    private nonisolated static let english: [String: String] = [
        "跟随系统": "Follow System",
        "简体中文": "Simplified Chinese",
        "语言": "Language",
        "界面语言": "Display language",
        "语言切换会立即应用": "Language changes apply immediately",
        "记录": "Capture",
        "隐私": "Privacy",
        "存储": "Storage",
        "快捷键": "Shortcut",
        "唤起 TuckClip": "Open TuckClip",
        "请按组合键…": "Press a shortcut…",
        "恢复默认": "Restore Default",
        "记录剪贴板历史": "Capture clipboard history",
        "关闭后不会读取新的剪贴板内容": "When off, new clipboard contents are not read",
        "选择后自动粘贴": "Paste automatically after selection",
        "需要辅助功能权限；未授权时只恢复到系统剪贴板": "Requires Accessibility permission; otherwise the item is only copied",
        "捕获图片": "Capture images",
        "图片占用空间较多，可随时关闭": "Images use more storage and can be disabled anytime",
        "记录行为": "Capture behavior",
        "容量": "Capacity",
        "保留期": "Retention",
        "1 天": "1 day",
        "7 天": "7 days",
        "30 天": "30 days",
        "90 天": "90 days",
        "1 年": "1 year",
        "最大条数": "Maximum items",
        "访问状态": "Access status",
        "首次设置": "First-time setup",
        "启用选择后自动粘贴": "Enable automatic paste after selection",
        "1. 点击“请求权限”，让 macOS 登记 TuckClip。": "1. Select Request Permission so macOS can register TuckClip.",
        "2. 打开辅助功能设置，并开启 TuckClip。": "2. Open Accessibility Settings and turn on TuckClip.",
        "3. 返回 TuckClip；授权状态会自动刷新。": "3. Return to TuckClip; the permission status refreshes automatically.",
        "剪贴板": "Clipboard",
        "打开系统设置": "Open System Settings",
        "打开辅助功能设置": "Open Accessibility Settings",
        "辅助功能": "Accessibility",
        "已允许自动粘贴": "Automatic paste is allowed",
        "未授权 · 当前仅复制": "Not allowed · copy only",
        "请求权限": "Request Permission",
        "刷新": "Refresh",
        "排除的应用 Bundle ID，每行一个": "Excluded app bundle IDs, one per line",
        "每行一个 Bundle ID；编辑后会自动保存。": "One bundle ID per line. Changes are saved automatically.",
        "排除应用": "Excluded apps",
        "本地数据": "Local data",
        "历史已进入只读保护": "History is protected as read-only",
        "最近一次保存失败": "The latest save failed",
        "位置": "Location",
        "历史数据库与图片仅保存在这台 Mac 上。": "History and images are stored only on this Mac.",
        "在 Finder 中显示": "Show in Finder",
        "清除未置顶历史": "Clear unpinned history",
        "保留你主动置顶的常用片段": "Keep snippets you pinned",
        "清除…": "Clear…",
        "清除所有本地数据": "Clear all local data",
        "包括置顶项；此操作不可撤销": "Includes pinned items; this cannot be undone",
        "全部清除…": "Clear All…",
        "清理": "Cleanup",
        "TuckClip 不包含账号、云同步、遥测或网络上传。": "TuckClip has no accounts, cloud sync, telemetry, or network uploads.",
        "清除": "Clear",
        "取消": "Cancel",
        "清除未置顶历史？": "Clear unpinned history?",
        "清除所有本地数据？": "Clear all local data?",
        "所有未置顶的文本、链接、图片和文件记录都会被删除。": "All unpinned text, links, images, and file entries will be deleted.",
        "所有历史与置顶项都会被永久删除，无法撤销。": "All history and pinned items will be permanently deleted. This cannot be undone.",
        "置顶内容会保留，其他本地剪贴板记录将被永久删除。": "Pinned items will be kept; all other local clipboard history will be permanently deleted.",
        "打开 TuckClip": "Open TuckClip",
        "打开 TuckClip（%@）": "Open TuckClip (%@)",
        "暂停记录": "Pause Capture",
        "恢复记录": "Resume Capture",
        "重新注册快捷键": "Register Shortcut Again",
        "存储异常，查看设置…": "Storage issue — open Settings…",
        "历史已受保护，查看设置…": "History protected — open Settings…",
        "最近一次保存失败，查看设置…": "Latest save failed — open Settings…",
        "清除未置顶历史…": "Clear Unpinned History…",
        "设置…": "Settings…",
        "检查更新…": "Check for Updates…",
        "退出 TuckClip": "Quit TuckClip",
        "快捷键不可用：%@": "Shortcut unavailable: %@",
        "TuckClip 设置": "TuckClip Settings",
        "搜索内容或来源应用": "Search content or source app",
        "搜索剪贴板历史": "Search clipboard history",
        "清除搜索": "Clear search",
        "关闭 TuckClip": "Close TuckClip",
        "%d 项": "%d items",
        "选择": "Select",
        "粘贴": "Paste",
        "纯文本": "Plain Text",
        "置顶": "Pin",
        "取消置顶": "Unpin",
        "删除": "Delete",
        "以纯文本粘贴": "Paste as Plain Text",
        "已置顶": "Pinned",
        "图片文件不可用": "Image file unavailable",
        "正在载入预览": "Loading preview",
        "无标题内容": "Untitled content",
        "需要允许剪贴板访问": "Clipboard access is required",
        "等待你的下一次复制": "Waiting for your next copy",
        "没有找到匹配内容": "No matching content",
        "原历史未被覆盖；请在设置的“存储”页定位文件并备份": "Existing history was not overwritten. Locate and back it up from Storage in Settings.",
        "请在系统设置中允许后再复制": "Allow access in System Settings, then copy again",
        "试试缩短关键词或切换类型筛选": "Try a shorter query or another content filter",
        "在设置或菜单栏中恢复记录；以后按 %@ 或点菜单栏图标回来": "Resume capture in Settings or the menu bar; return with %@ or the menu bar icon",
        "复制文本、链接、图片或文件；以后按 %@ 或点菜单栏图标回来": "Copy text, links, images, or files; return with %@ or the menu bar icon",
        "文本": "Text",
        "链接": "Link",
        "图片": "Image",
        "文件": "Files",
        "全部": "All",
        "内容": "Content",
        "未知应用": "Unknown App",
        "空文本": "Empty text",
        "复制 %d 次": "Copied %d times",
        "本地图片": "Local image",
        "%d 个文件 · %@": "%d files · %@",
        "、": ", ",
        "；": "; ",
        "，已置顶": ", pinned",
        "%@，%@，来源 %@%@": "%@, %@, from %@%@",
        "已复制到剪贴板": "Copied to clipboard",
        "已复制，请按 ⌘V；自动粘贴可在设置中授权": "Copied. Press ⌘V; automatic paste can be enabled in Settings",
        "已复制；没有可自动粘贴的目标": "Copied; no target is available for automatic paste",
        "已复制；无法切回目标应用，请按 ⌘V": "Copied; could not return to the target app. Press ⌘V",
        "剪贴板已被其他应用改写；为避免粘错，已取消自动粘贴": "The clipboard changed in another app, so automatic paste was cancelled",
        "已复制；自动粘贴失败，请按 ⌘V": "Copied; automatic paste failed. Press ⌘V",
        "写入系统剪贴板失败": "Could not write to the system clipboard",
        "可访问 · 记录已暂停": "Available · capture paused",
        "可访问 · 此系统无需单独授权": "Available · no separate permission required",
        "尚未确定 · 复制后由 macOS 询问": "Not determined · macOS will ask after you copy",
        "每次询问 · 建议在系统设置中始终允许": "Ask every time · Always Allow is recommended",
        "始终允许 · 正在记录": "Always Allow · capturing",
        "已拒绝 · 无法记录": "Denied · cannot capture",
        "已暂停": "Paused",
        "存储受保护": "Storage protected",
        "存储失败": "Storage failed",
        "记录中": "Capturing",
        "权限受限": "Permission limited",
        "请按新的组合键，按 Esc 取消": "Press a new shortcut; press Esc to cancel",
        "%@ 已就绪": "%@ is ready",
        "快捷键至少需要一个修饰键": "A shortcut needs at least one modifier key",
        "快捷键还需要一个非修饰键": "A shortcut also needs a non-modifier key",
        "快捷键包含不支持的修饰键": "The shortcut contains an unsupported modifier",
        "已被系统或其他应用占用": "Already used by the system or another app",
        "无法安装全局快捷键处理器（OSStatus %d）": "Could not install the global shortcut handler (OSStatus %d)",
        "无法注册全局快捷键（OSStatus %d）": "Could not register the global shortcut (OSStatus %d)",
        "这条记录已没有可复制的文本": "This item no longer contains copyable text",
        "这条记录的图片文件已丢失": "The image file for this item is missing",
        "这条记录已没有可复制的文件路径": "This item no longer contains copyable file paths",
        "这条记录超过对应类型的安全上限": "This item exceeds the safety limit for its type",
        "保存的图片无法转换为 PNG": "The saved image could not be converted to PNG",
        "macOS 拒绝写入系统剪贴板": "macOS refused access to the system clipboard",
        "无法读取剪贴板历史。为保护现有数据，TuckClip 已进入只读模式：%@": "Could not read clipboard history. TuckClip entered read-only mode to protect existing data: %@",
        "无法保存剪贴板图片：%@": "Could not save the clipboard image: %@",
        "无法加载剪贴板历史，当前处于只读保护模式。": "Could not load clipboard history. TuckClip is in read-only protection mode.",
        "无法保存剪贴板历史：%@": "Could not save clipboard history: %@",
        "历史已保存，但无法清理无引用图片：%@": "History was saved, but unreferenced images could not be cleaned up: %@",
        "%@未提交的图片无法清理：%@": "%@Uncommitted images could not be cleaned up: %@",
        "无法清理无引用图片：%@": "Could not clean up unreferenced images: %@"
    ]
}
