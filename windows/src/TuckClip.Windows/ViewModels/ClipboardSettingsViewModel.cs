using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.ViewModels;

public sealed class ClipboardSettingsViewModel : ObservableObject
{
    private readonly IClipboardUiActions _actions;
    private bool _isApplyingSnapshot;
    private bool _recordingEnabled = true;
    private bool _automaticPasteEnabled = true;
    private bool _capturesImages = true;
    private int _retentionDays = 30;
    private int _maximumItemCount = 500;
    private string _excludedProcessNamesText = string.Empty;
    private string _committedExcludedProcessNamesText = string.Empty;
    private bool _hasPendingExcludedProcessChanges;
    private bool _isSavingExcludedProcessNames;
    private string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TuckClip");
    private bool _isStorageReadOnly;
    private string? _storageError;
    private GlobalHotKey _globalHotKey = GlobalHotKey.Default;
    private string? _hotKeyError;
    private bool _isRecordingHotKey;
    private ClearHistoryScope? _pendingClearScope;

    public ClipboardSettingsViewModel(IClipboardUiActions? actions = null)
    {
        _actions = actions ?? EmptyClipboardUiActions.Instance;
        RetentionOptions = [1, 7, 30, 90, 365];
        MaximumItemOptions = [100, 500, 1_000, 5_000, 10_000];
        RevealDataDirectoryCommand = new RelayCommand(_actions.RevealDataDirectory);
        ApplyExcludedProcessNamesCommand = new RelayCommand(
            ApplyExcludedProcessNames,
            () => HasPendingExcludedProcessChanges && !_isSavingExcludedProcessNames);
        RequestClearUnpinnedCommand = new RelayCommand(
            () => BeginClear(ClearHistoryScope.Unpinned),
            () => !IsStorageReadOnly);
        RequestClearAllCommand = new RelayCommand(
            () => BeginClear(ClearHistoryScope.All),
            () => !IsStorageReadOnly);
        ConfirmClearCommand = new RelayCommand(ConfirmClear, () => PendingClearScope.HasValue);
        CancelClearCommand = new RelayCommand(CancelClear);
        BeginHotKeyRecordingCommand = new RelayCommand(BeginHotKeyRecording);
        CancelHotKeyRecordingCommand = new RelayCommand(CancelHotKeyRecording);
        RestoreDefaultHotKeyCommand = new RelayCommand(
            RestoreDefaultHotKey,
            () => CanRestoreDefaultHotKey);
    }

    public IReadOnlyList<int> RetentionOptions { get; }

    public IReadOnlyList<int> MaximumItemOptions { get; }

    public RelayCommand RevealDataDirectoryCommand { get; }

    public RelayCommand ApplyExcludedProcessNamesCommand { get; }

    public RelayCommand RequestClearUnpinnedCommand { get; }

    public RelayCommand RequestClearAllCommand { get; }

    public RelayCommand ConfirmClearCommand { get; }

    public RelayCommand CancelClearCommand { get; }

    public RelayCommand BeginHotKeyRecordingCommand { get; }

    public RelayCommand CancelHotKeyRecordingCommand { get; }

    public RelayCommand RestoreDefaultHotKeyCommand { get; }

    public GlobalHotKey GlobalHotKey
    {
        get => _globalHotKey;
        private set
        {
            if (SetProperty(ref _globalHotKey, value))
            {
                OnPropertyChanged(nameof(HotKeyButtonText));
                OnPropertyChanged(nameof(HotKeyStatusText));
                OnPropertyChanged(nameof(CanRestoreDefaultHotKey));
                RestoreDefaultHotKeyCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRecordingHotKey
    {
        get => _isRecordingHotKey;
        private set
        {
            if (SetProperty(ref _isRecordingHotKey, value))
            {
                OnPropertyChanged(nameof(HotKeyButtonText));
                OnPropertyChanged(nameof(HotKeyStatusText));
            }
        }
    }

    public string HotKeyButtonText => IsRecordingHotKey
        ? "请按新的组合键…"
        : GlobalHotKey.DisplayText;

    public string? HotKeyError
    {
        get => _hotKeyError;
        private set
        {
            if (SetProperty(ref _hotKeyError, value))
            {
                OnPropertyChanged(nameof(HasHotKeyError));
                OnPropertyChanged(nameof(HasNoHotKeyError));
                OnPropertyChanged(nameof(HotKeyStatusText));
            }
        }
    }

    public bool HasHotKeyError => !string.IsNullOrWhiteSpace(HotKeyError);

    public bool HasNoHotKeyError => !HasHotKeyError;

    public string HotKeyStatusText => IsRecordingHotKey
        ? HotKeyError ?? "按 Esc 取消；快捷键至少包含一个修饰键。"
        : HotKeyError ?? $"当前使用 {GlobalHotKey.DisplayText}";

    public bool CanRestoreDefaultHotKey =>
        GlobalHotKey != TuckClip.Platform.Windows.Interop.GlobalHotKey.Default;

    public bool RecordingEnabled
    {
        get => _recordingEnabled;
        set
        {
            if (SetProperty(ref _recordingEnabled, value))
            {
                OnPropertyChanged(nameof(RecordingStatusText));
                ApplySettings();
            }
        }
    }

    public bool AutomaticPasteEnabled
    {
        get => _automaticPasteEnabled;
        set
        {
            if (SetProperty(ref _automaticPasteEnabled, value))
            {
                ApplySettings();
            }
        }
    }

    public bool CapturesImages
    {
        get => _capturesImages;
        set
        {
            if (SetProperty(ref _capturesImages, value))
            {
                ApplySettings();
            }
        }
    }

    public int RetentionDays
    {
        get => _retentionDays;
        set
        {
            if (SetProperty(ref _retentionDays, value))
            {
                ApplySettings();
            }
        }
    }

    public int MaximumItemCount
    {
        get => _maximumItemCount;
        set
        {
            if (SetProperty(ref _maximumItemCount, value))
            {
                ApplySettings();
            }
        }
    }

    public string ExcludedProcessNamesText
    {
        get => _excludedProcessNamesText;
        set
        {
            if (SetProperty(ref _excludedProcessNamesText, value ?? string.Empty))
            {
                HasPendingExcludedProcessChanges = !ExcludedProcessListsMatch(
                    _excludedProcessNamesText,
                    _committedExcludedProcessNamesText);
            }
        }
    }

    public bool HasPendingExcludedProcessChanges
    {
        get => _hasPendingExcludedProcessChanges;
        private set
        {
            if (SetProperty(ref _hasPendingExcludedProcessChanges, value))
            {
                OnPropertyChanged(nameof(ExcludedProcessSaveStatus));
                ApplyExcludedProcessNamesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExcludedProcessSaveStatus => _isSavingExcludedProcessNames
        ? "正在保存排除列表…"
        : HasPendingExcludedProcessChanges
            ? "排除列表有尚未保存的修改"
            : "排除列表已生效";

    public string DataDirectory
    {
        get => _dataDirectory;
        private set => SetProperty(ref _dataDirectory, value);
    }

    public bool IsStorageReadOnly
    {
        get => _isStorageReadOnly;
        private set
        {
            if (SetProperty(ref _isStorageReadOnly, value))
            {
                OnPropertyChanged(nameof(CanClearHistory));
                RequestClearUnpinnedCommand.NotifyCanExecuteChanged();
                RequestClearAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanClearHistory => !IsStorageReadOnly;

    public string? StorageError
    {
        get => _storageError;
        private set
        {
            if (SetProperty(ref _storageError, value))
            {
                OnPropertyChanged(nameof(HasStorageError));
            }
        }
    }

    public bool HasStorageError => !string.IsNullOrWhiteSpace(StorageError);

    public string RecordingStatusText => RecordingEnabled ? "正在记录新的剪贴板内容" : "记录已暂停";

    public ClearHistoryScope? PendingClearScope
    {
        get => _pendingClearScope;
        private set
        {
            if (SetProperty(ref _pendingClearScope, value))
            {
                OnPropertyChanged(nameof(IsClearConfirmationVisible));
                OnPropertyChanged(nameof(ClearConfirmationTitle));
                OnPropertyChanged(nameof(ClearConfirmationDetail));
                ConfirmClearCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsClearConfirmationVisible => PendingClearScope.HasValue;

    public string ClearConfirmationTitle => PendingClearScope switch
    {
        ClearHistoryScope.Unpinned => "确认清除未置顶历史？",
        ClearHistoryScope.All => "确认清除所有本地数据？",
        _ => string.Empty,
    };

    public string ClearConfirmationDetail => PendingClearScope switch
    {
        ClearHistoryScope.Unpinned => "文本、链接、图片和文件记录都会删除；置顶项会保留。",
        ClearHistoryScope.All => "包括置顶项在内的所有历史都会永久删除，此操作不可撤销。",
        _ => string.Empty,
    };

    public void ApplySnapshot(ClipboardSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _isApplyingSnapshot = true;
        try
        {
            RecordingEnabled = snapshot.RecordingEnabled;
            AutomaticPasteEnabled = snapshot.AutomaticPasteEnabled;
            CapturesImages = snapshot.CapturesImages;
            RetentionDays = snapshot.RetentionDays;
            MaximumItemCount = snapshot.MaximumItemCount;
            var snapshotExcludedProcessNamesText = string.Join(
                Environment.NewLine,
                snapshot.ExcludedProcessNames);
            _isSavingExcludedProcessNames = false;
            OnPropertyChanged(nameof(ExcludedProcessSaveStatus));
            ApplyExcludedProcessNamesCommand.NotifyCanExecuteChanged();
            _committedExcludedProcessNamesText = snapshotExcludedProcessNamesText;
            if (!HasPendingExcludedProcessChanges || ExcludedProcessListsMatch(
                    ExcludedProcessNamesText,
                    snapshotExcludedProcessNamesText))
            {
                ExcludedProcessNamesText = snapshotExcludedProcessNamesText;
                HasPendingExcludedProcessChanges = false;
            }
            else
            {
                HasPendingExcludedProcessChanges = true;
            }
            DataDirectory = snapshot.DataDirectory;
            IsStorageReadOnly = snapshot.IsStorageReadOnly;
            StorageError = snapshot.StorageError;
            GlobalHotKey = snapshot.GlobalHotKey
                ?? TuckClip.Platform.Windows.Interop.GlobalHotKey.Default;
            HotKeyError = snapshot.HotKeyError;
            IsRecordingHotKey = false;
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    public ClipboardSettingsDraft CreateDraft() => new(
        RecordingEnabled,
        AutomaticPasteEnabled,
        CapturesImages,
        RetentionDays,
        MaximumItemCount,
        ParseExcludedProcessNames(_committedExcludedProcessNamesText));

    public void BeginClear(ClearHistoryScope scope)
    {
        if (IsStorageReadOnly)
        {
            return;
        }

        PendingClearScope = scope;
    }

    public void ConfirmClear()
    {
        if (PendingClearScope is not { } scope)
        {
            return;
        }

        PendingClearScope = null;
        _actions.ClearHistory(scope);
    }

    public void CancelClear() => PendingClearScope = null;

    public void BeginHotKeyRecording()
    {
        HotKeyError = null;
        IsRecordingHotKey = true;
    }

    public void CancelHotKeyRecording()
    {
        HotKeyError = null;
        IsRecordingHotKey = false;
    }

    public void CaptureHotKey(GlobalHotKey hotKey)
    {
        try
        {
            hotKey = hotKey.Validate();
        }
        catch (ArgumentException exception)
        {
            HotKeyError = exception.Message;
            IsRecordingHotKey = true;
            return;
        }

        var shouldRetry = HasHotKeyError;
        IsRecordingHotKey = false;
        HotKeyError = null;
        if (hotKey != GlobalHotKey || shouldRetry)
        {
            _actions.ChangeGlobalHotKey(hotKey, CreateDraft());
        }
    }

    private void RestoreDefaultHotKey() => CaptureHotKey(
        TuckClip.Platform.Windows.Interop.GlobalHotKey.Default);

    private void ApplyExcludedProcessNames()
    {
        var normalizedText = string.Join(
            Environment.NewLine,
            ParseExcludedProcessNames(ExcludedProcessNamesText));
        ExcludedProcessNamesText = normalizedText;
        _isSavingExcludedProcessNames = true;
        OnPropertyChanged(nameof(ExcludedProcessSaveStatus));
        ApplyExcludedProcessNamesCommand.NotifyCanExecuteChanged();
        _actions.ApplySettings(CreateDraft(ParseExcludedProcessNames(normalizedText)));
    }

    private void ApplySettings()
    {
        if (!_isApplyingSnapshot)
        {
            _actions.ApplySettings(CreateDraft());
        }
    }

    private ClipboardSettingsDraft CreateDraft(IReadOnlyList<string> excludedProcessNames) => new(
        RecordingEnabled,
        AutomaticPasteEnabled,
        CapturesImages,
        RetentionDays,
        MaximumItemCount,
        excludedProcessNames);

    private static string[] ParseExcludedProcessNames(string value) =>
        value.Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(processName => processName.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(processName => processName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ExcludedProcessListsMatch(string left, string right) =>
        ParseExcludedProcessNames(left).SequenceEqual(
            ParseExcludedProcessNames(right),
            StringComparer.OrdinalIgnoreCase);
}
