using System.Globalization;
using Avalonia;

namespace TuckClip.Windows.Services;

public enum AppLanguage
{
    System,
    SimplifiedChinese,
    English,
}

public sealed record AppLanguageOption(AppLanguage Value, string DisplayName);

public static class AppLocalization
{
    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["跟随系统"] = "Follow System",
            ["简体中文"] = "Simplified Chinese",
            ["语言"] = "Language",
            ["界面语言"] = "Display language",
            ["语言切换会立即应用"] = "Language changes apply immediately",
            ["完成"] = "Done",
            ["记录"] = "Capture",
            ["隐私"] = "Privacy",
            ["存储"] = "Storage",
            ["快捷键"] = "Shortcut",
            ["唤起 TuckClip"] = "Open TuckClip",
            ["恢复默认"] = "Restore Default",
            ["记录行为"] = "Capture behavior",
            ["记录剪贴板历史"] = "Capture clipboard history",
            ["关闭后不会读取新的剪贴板内容"] = "When off, new clipboard contents are not read",
            ["选择后自动粘贴"] = "Paste automatically after selection",
            ["关闭时仅把内容恢复到系统剪贴板"] = "When off, the item is only copied to the system clipboard",
            ["捕获图片"] = "Capture images",
            ["图片占用空间较多，可随时关闭"] = "Images use more storage and can be disabled anytime",
            ["容量"] = "Capacity",
            ["保留期"] = "Retention",
            ["到期记录会自动清理，置顶项除外"] = "Expired items are removed automatically, except pinned items",
            ["最大条数"] = "Maximum items",
            ["达到上限后优先清理最早的未置顶记录"] = "Oldest unpinned items are removed first at the limit",
            ["排除应用"] = "Excluded apps",
            ["不记录这些进程"] = "Do not capture these processes",
            ["每行填写一个进程名，例如 KeePassXC.exe。密码管理器建议始终排除。"] = "Enter one process name per line, for example KeePassXC.exe. Password managers should remain excluded.",
            ["保存排除列表"] = "Save Exclusions",
            ["TuckClip 不包含账号、云同步、遥测或网络上传。"] = "TuckClip has no accounts, cloud sync, telemetry, or network uploads.",
            ["本地数据"] = "Local data",
            ["存储状态异常"] = "Storage issue",
            ["数据目录"] = "Data directory",
            ["历史数据库与图片只保存在本机。"] = "History and images are stored only on this device.",
            ["在资源管理器中显示"] = "Show in File Explorer",
            ["清理"] = "Cleanup",
            ["清除未置顶历史"] = "Clear unpinned history",
            ["保留你主动置顶的常用片段"] = "Keep snippets you pinned",
            ["清除…"] = "Clear…",
            ["清除所有本地数据"] = "Clear all local data",
            ["包括置顶项；此操作不可撤销"] = "Includes pinned items; this cannot be undone",
            ["全部清除…"] = "Clear All…",
            ["取消"] = "Cancel",
            ["确认清除"] = "Confirm Clear",
            ["搜索内容或来源应用"] = "Search content or source app",
            ["清除搜索"] = "Clear search",
            ["设置"] = "Settings",
            ["隐藏 TuckClip"] = "Hide TuckClip",
            ["全部"] = "All",
            ["文本"] = "Text",
            ["链接"] = "Link",
            ["图片"] = "Image",
            ["文件"] = "Files",
            ["选择"] = "Select",
            ["粘贴"] = "Paste",
            ["纯文本"] = "Plain Text",
            ["置顶"] = "Pin",
            ["删除"] = "Delete",
            ["以纯文本粘贴"] = "Paste as Plain Text",
            ["已置顶"] = "Pinned",
            ["图片预览不可用"] = "Image preview unavailable",
            ["正在加载…"] = "Loading…",
            ["TuckClip 设置"] = "TuckClip Settings",
            ["剪贴板内容仅保存在这台电脑上"] = "Clipboard contents stay on this device",
            ["请按新的组合键…"] = "Press a new shortcut…",
            ["按 Esc 取消；快捷键至少包含一个修饰键。"] = "Press Esc to cancel; include at least one modifier key.",
            ["当前使用 {0}"] = "Currently using {0}",
            ["正在保存排除列表…"] = "Saving exclusions…",
            ["排除列表有尚未保存的修改"] = "Exclusions have unsaved changes",
            ["排除列表已生效"] = "Exclusions are active",
            ["正在记录新的剪贴板内容"] = "Capturing new clipboard contents",
            ["记录已暂停"] = "Capture is paused",
            ["确认清除未置顶历史？"] = "Clear unpinned history?",
            ["确认清除所有本地数据？"] = "Clear all local data?",
            ["文本、链接、图片和文件记录都会删除；置顶项会保留。"] = "Text, links, images, and file entries will be deleted; pinned items will remain.",
            ["包括置顶项在内的所有历史都会永久删除，此操作不可撤销。"] = "All history, including pinned items, will be permanently deleted. This cannot be undone.",
            ["记录中"] = "Capturing",
            ["已暂停"] = "Paused",
            ["需要剪贴板权限"] = "Clipboard permission required",
            ["存储受保护"] = "Storage protected",
            ["记录异常"] = "Capture error",
            ["状态未知"] = "Unknown status",
            ["{0} 项"] = "{0} items",
            ["没有找到匹配内容"] = "No matching content",
            ["等待你的下一次复制"] = "Waiting for your next copy",
            ["试试缩短关键词或切换类型筛选"] = "Try a shorter query or another content filter",
            ["复制文本、链接、图片或文件；按 {0} 随时回来"] = "Copy text, links, images, or files; return anytime with {0}",
            ["未知应用"] = "Unknown App",
            ["内容"] = "Content",
            ["无标题内容"] = "Untitled content",
            ["，已置顶"] = ", pinned",
            ["{0}，{1}，来源 {2}{3}"] = "{0}, {1}, from {2}{3}",
            ["刚刚"] = "Just now",
            ["{0} 分钟前"] = "{0} min ago",
            ["{0} 小时前"] = "{0} hr ago",
            ["{0} 天前"] = "{0} days ago",
            ["设置…"] = "Settings…",
            ["退出 TuckClip"] = "Quit TuckClip",
            ["暂停记录"] = "Pause Capture",
            ["继续记录"] = "Resume Capture",
            ["打开 TuckClip（{0}）"] = "Open TuckClip ({0})",
            ["TuckClip · 本地剪贴板历史"] = "TuckClip · Local clipboard history",
            ["这条记录已不存在。"] = "This item no longer exists.",
            ["置顶状态未能保存。"] = "Could not save the pinned state.",
            ["这条记录未能删除。"] = "Could not delete this item.",
            ["历史记录未能清除。"] = "Could not clear history.",
            ["快捷键服务尚未启动。"] = "The shortcut service has not started.",
            ["{0} 已被系统或其他应用占用。"] = "{0} is used by the system or another app.",
            ["快捷键必须包含 Ctrl、Alt、Shift 或 Win 中的至少一个修饰键。"] = "The shortcut must include Ctrl, Alt, Shift, or Win.",
            ["快捷键还需要一个非修饰键。"] = "The shortcut also needs a non-modifier key.",
            ["最多支持 256 个排除进程。"] = "At most 256 excluded processes are supported.",
            ["单个排除进程名不能超过 260 个字符。"] = "An excluded process name cannot exceed 260 characters.",
            ["最大条数必须在 1 到 10000 之间。"] = "Maximum items must be between 1 and 10000.",
            ["保留期必须在 0 到 3650 天之间。"] = "Retention must be between 0 and 3650 days.",
            ["设置值无效，请检查后重试。"] = "A setting value is invalid. Check it and try again.",
            ["设置读取失败，已暂停记录并关闭自动粘贴；原文件尚未被改写。"] = "Settings could not be read. Capture and automatic paste were disabled; the original file was not changed.",
            ["{0} 注册失败，请录入其他组合键。"] = "Could not register {0}; record another shortcut.",
            ["剪贴板监听未能启动"] = "Clipboard monitoring could not start",
            ["Win32 剪贴板监听在当前系统上不可用。"] = "Win32 clipboard monitoring is unavailable on this operating system.",
            ["TuckClip 无法创建 Win32 消息窗口。"] = "TuckClip could not create its Win32 message window.",
            ["剪贴板监听未能启动：{0}"] = "Clipboard monitoring could not start: {0}",
            ["{0} 注册失败：{1}"] = "Could not register {0}: {1}",
            ["无法接收另一个 TuckClip 进程的唤起请求。"] = "Requests from another TuckClip process cannot be received.",
            ["全局快捷键服务不可用。"] = "The global shortcut service is unavailable.",
            ["本地设置文件损坏或无效；原文件没有被改写。"] = "The local settings file is damaged or invalid; the original file was not changed.",
            ["系统剪贴板在写入确认期间又发生变化；为避免误粘贴，已停止，请重试。"] = "The clipboard changed while the write was being verified. Paste was stopped; please try again.",
            ["复制失败：{0}"] = "Copy failed: {0}",
            ["设置未能写入磁盘：{0}"] = "Settings could not be saved: {0}",
            ["历史文件损坏，已进入只读保护；原文件没有被覆盖。"] = "History is damaged and has been opened read-only; the original file was not overwritten.",
            ["无法打开数据目录。"] = "Could not open the data directory.",
            ["无法打开数据目录：{0}"] = "Could not open the data directory: {0}",
            ["剪贴板记录未能写入磁盘。"] = "Clipboard history could not be saved.",
            ["历史文件处于只读保护状态"] = "History is protected as read-only",
            ["读取剪贴板失败：{0}"] = "Could not read the clipboard: {0}",
            ["{0} 个文件"] = "{0} files",
            ["图片数据不可用"] = "Image data unavailable",
            ["已复制，但系统没有把焦点交回原窗口；请手动按 Ctrl+V。"] = "Copied, but Windows did not return focus to the original window. Press Ctrl+V manually.",
            ["已复制，但在发送 Ctrl+V 前系统剪贴板又发生变化；为避免粘贴错误内容，已取消自动粘贴。"] = "Copied, but the clipboard changed before Ctrl+V could be sent. Automatic paste was cancelled.",
            ["已复制，但检测到仍按住修饰键；请松开后手动按 Ctrl+V。"] = "Copied, but a modifier key is still pressed. Release it and press Ctrl+V manually.",
            ["已复制，但目标窗口拒绝自动粘贴；管理员应用需要手动按 Ctrl+V。"] = "Copied, but the target rejected automatic paste. Administrator apps require a manual Ctrl+V.",
            ["已复制，但自动粘贴未完成；请手动按 Ctrl+V。"] = "Copied, but automatic paste did not finish. Press Ctrl+V manually.",
        };

    public static AppLanguage CurrentPreference { get; private set; } = AppLanguage.System;

    public static AppLanguage ResolvedLanguage => Resolve(CurrentPreference);

    public static event EventHandler? LanguageChanged;

    public static string Text(string source) =>
        ResolvedLanguage == AppLanguage.English && English.TryGetValue(source, out var translated)
            ? translated
            : source;

    public static string Format(string source, params object?[] arguments) =>
        string.Format(CurrentCulture, Text(source), arguments);

    public static IReadOnlyList<AppLanguageOption> CreateOptions() =>
    [
        new(AppLanguage.System, Text("跟随系统")),
        new(AppLanguage.SimplifiedChinese, Text("简体中文")),
        new(AppLanguage.English, "English"),
    ];

    public static void Apply(AppLanguage language)
    {
        CurrentPreference = language;
        if (Application.Current is { } application)
        {
            foreach (var source in English.Keys)
            {
                application.Resources[source] = Text(source);
            }
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static AppLanguage Resolve(AppLanguage language)
    {
        if (language != AppLanguage.System)
        {
            return language;
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.SimplifiedChinese
            : AppLanguage.English;
    }

    private static CultureInfo CurrentCulture => ResolvedLanguage == AppLanguage.SimplifiedChinese
        ? CultureInfo.GetCultureInfo("zh-Hans")
        : CultureInfo.GetCultureInfo("en");
}
