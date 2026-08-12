using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.ViewModels;

public enum ClearHistoryScope
{
    Unpinned,
    All,
}

public enum CaptureStateKind
{
    Recording,
    Paused,
    PermissionRequired,
    StorageReadOnly,
    Error,
}

public sealed record ClipboardCaptureState(CaptureStateKind Kind, string? Detail = null);

public sealed record ClipboardSettingsSnapshot(
    bool RecordingEnabled,
    bool AutomaticPasteEnabled,
    bool CapturesImages,
    int RetentionDays,
    int MaximumItemCount,
    IReadOnlyList<string> ExcludedProcessNames,
    string DataDirectory,
    bool IsStorageReadOnly = false,
    string? StorageError = null,
    GlobalHotKey? GlobalHotKey = null,
    string? HotKeyError = null);

public sealed record ClipboardSettingsDraft(
    bool RecordingEnabled,
    bool AutomaticPasteEnabled,
    bool CapturesImages,
    int RetentionDays,
    int MaximumItemCount,
    IReadOnlyList<string> ExcludedProcessNames);

/// <summary>
/// The only outward-facing dependency of the Windows UI. The application
/// coordinator maps these user intents to TuckClip.Core and the Windows
/// clipboard/keyboard services; no persistence or OS API leaks into views.
/// </summary>
public interface IClipboardUiActions
{
    void PasteItem(Guid itemId, bool asPlainText);

    void TogglePinned(Guid itemId);

    void DeleteItem(Guid itemId);

    void ClearHistory(ClearHistoryScope scope);

    void ApplySettings(ClipboardSettingsDraft settings);

    void ChangeGlobalHotKey(GlobalHotKey hotKey, ClipboardSettingsDraft settings);

    void RevealDataDirectory();

    void ShowSettings();

    void HidePanel();
}

/// <summary>
/// Safe fallback for designers, unit tests, and an app that is still wiring its
/// coordinator. It deliberately never reads or writes the system clipboard.
/// </summary>
public sealed class EmptyClipboardUiActions : IClipboardUiActions
{
    public static EmptyClipboardUiActions Instance { get; } = new();

    private EmptyClipboardUiActions()
    {
    }

    public void PasteItem(Guid itemId, bool asPlainText)
    {
    }

    public void TogglePinned(Guid itemId)
    {
    }

    public void DeleteItem(Guid itemId)
    {
    }

    public void ClearHistory(ClearHistoryScope scope)
    {
    }

    public void ApplySettings(ClipboardSettingsDraft settings)
    {
    }

    public void ChangeGlobalHotKey(GlobalHotKey hotKey, ClipboardSettingsDraft settings)
    {
    }

    public void RevealDataDirectory()
    {
    }

    public void ShowSettings()
    {
    }

    public void HidePanel()
    {
    }
}
