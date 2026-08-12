using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using TuckClip.Windows.Services;

namespace TuckClip.Windows.ViewModels;

public enum ClipTypeFilter
{
    All,
    Text,
    Link,
    Image,
    Files,
}

public enum PanelNoticeKind
{
    Information,
    Error,
}

public sealed class ClipboardPanelViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush ActiveFilterBrush = Brush.Parse("#59605AFB");
    private static readonly IBrush InactiveFilterBrush = Brush.Parse("#0EFFFFFF");
    private static readonly IBrush RecordingBrush = Brush.Parse("#31D1EA");
    private static readonly IBrush PausedBrush = Brush.Parse("#8E98AD");
    private static readonly IBrush WarningBrush = Brush.Parse("#FFB15C");
    private static readonly IBrush InformationBrush = Brush.Parse("#31D1EA");

    private readonly IClipboardUiActions _actions;
    private readonly ObservableCollection<ClipCardViewModel> _items = [];
    private readonly ObservableCollection<ClipCardViewModel> _filteredItems = [];
    private readonly RelayCommand _pasteSelectedCommand;
    private readonly RelayCommand _pastePlainTextCommand;
    private readonly RelayCommand _togglePinCommand;
    private readonly RelayCommand _deleteCommand;
    private CancellationTokenSource? _noticeCancellation;
    private string _searchText = string.Empty;
    private ClipTypeFilter _selectedFilter;
    private ClipCardViewModel? _selectedItem;
    private string _statusText = AppLocalization.Text("记录中");
    private IBrush _statusBrush = RecordingBrush;
    private string? _noticeText;
    private PanelNoticeKind _noticeKind;
    private string _globalHotKeyDisplayText = "Ctrl+Alt+V";
    private ClipboardCaptureState _captureState = new(CaptureStateKind.Recording);

    public ClipboardPanelViewModel(IClipboardUiActions? actions = null)
    {
        _actions = actions ?? EmptyClipboardUiActions.Instance;
        Items = new ReadOnlyObservableCollection<ClipCardViewModel>(_items);
        FilteredItems = new ReadOnlyObservableCollection<ClipCardViewModel>(_filteredItems);

        ActivateItemCommand = new RelayCommand<ClipCardViewModel>(ActivateItem, item => item is not null);
        _pasteSelectedCommand = new RelayCommand(() => PasteSelected(false), () => SelectedItem is not null);
        _pastePlainTextCommand = new RelayCommand(
            () => PasteSelected(true),
            () => SelectedItem?.SupportsPlainTextPaste == true);
        _togglePinCommand = new RelayCommand(TogglePinSelected, () => SelectedItem is not null);
        _deleteCommand = new RelayCommand(DeleteSelected, () => SelectedItem is not null);
        OpenSettingsCommand = new RelayCommand(_actions.ShowSettings);
        HidePanelCommand = new RelayCommand(_actions.HidePanel);
    }

    public ReadOnlyObservableCollection<ClipCardViewModel> Items { get; }

    public ReadOnlyObservableCollection<ClipCardViewModel> FilteredItems { get; }

    public RelayCommand<ClipCardViewModel> ActivateItemCommand { get; }

    public RelayCommand PasteSelectedCommand => _pasteSelectedCommand;

    public RelayCommand PastePlainTextCommand => _pastePlainTextCommand;

    public RelayCommand TogglePinCommand => _togglePinCommand;

    public RelayCommand DeleteCommand => _deleteCommand;

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand HidePanelCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasSearch));
                RebuildFilter();
            }
        }
    }

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    public ClipTypeFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                NotifyFilterPropertiesChanged();
                RebuildFilter();
            }
        }
    }

    public ClipCardViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            if (_selectedItem is not null)
            {
                _selectedItem.IsSelected = false;
            }

            _selectedItem = value;
            if (_selectedItem is not null)
            {
                _selectedItem.IsSelected = true;
            }

            OnPropertyChanged();
            NotifySelectionCommandsChanged();
        }
    }

    public bool HasItems => _filteredItems.Count > 0;

    public bool IsEmpty => !HasItems;

    public string ResultCountText => AppLocalization.Format("{0} 项", _filteredItems.Count);

    public string EmptyTitle => AppLocalization.Text(
        HasSearch ? "没有找到匹配内容" : "等待你的下一次复制");

    public string EmptyDetail => HasSearch
        ? AppLocalization.Text("试试缩短关键词或切换类型筛选")
        : AppLocalization.Format(
            "复制文本、链接、图片或文件；按 {0} 随时回来",
            GlobalHotKeyDisplayText);

    public string GlobalHotKeyDisplayText
    {
        get => _globalHotKeyDisplayText;
        private set
        {
            if (SetProperty(ref _globalHotKeyDisplayText, value))
            {
                OnPropertyChanged(nameof(EmptyDetail));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        private set => SetProperty(ref _statusBrush, value);
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public bool HasNoNotice => !HasNotice;

    public string? NoticeText
    {
        get => _noticeText;
        private set
        {
            if (SetProperty(ref _noticeText, value))
            {
                OnPropertyChanged(nameof(HasNotice));
                OnPropertyChanged(nameof(HasNoNotice));
            }
        }
    }

    public PanelNoticeKind NoticeKind
    {
        get => _noticeKind;
        private set
        {
            if (SetProperty(ref _noticeKind, value))
            {
                OnPropertyChanged(nameof(NoticeBrush));
                OnPropertyChanged(nameof(NoticeIcon));
            }
        }
    }

    public IBrush NoticeBrush => NoticeKind == PanelNoticeKind.Error ? WarningBrush : InformationBrush;

    public string NoticeIcon => NoticeKind == PanelNoticeKind.Error ? "!" : "✓";

    public IBrush AllFilterBrush => FilterBrush(ClipTypeFilter.All);

    public IBrush TextFilterBrush => FilterBrush(ClipTypeFilter.Text);

    public IBrush LinkFilterBrush => FilterBrush(ClipTypeFilter.Link);

    public IBrush ImageFilterBrush => FilterBrush(ClipTypeFilter.Image);

    public IBrush FilesFilterBrush => FilterBrush(ClipTypeFilter.Files);

    public void ReplaceItems(IEnumerable<ClipCardViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = items.ToArray();
        RunOnUiThread(() => ReplaceItemsCore(snapshot));
    }

    public void SelectFilter(ClipTypeFilter filter) => SelectedFilter = filter;

    public void ClearSearch() => SearchText = string.Empty;

    public void MoveSelection(int offset)
    {
        if (_filteredItems.Count == 0)
        {
            SelectedItem = null;
            return;
        }

        var currentIndex = SelectedItem is null ? -1 : _filteredItems.IndexOf(SelectedItem);
        if (currentIndex < 0)
        {
            SelectedItem = offset < 0 ? _filteredItems[^1] : _filteredItems[0];
            return;
        }

        var nextIndex = Math.Clamp(currentIndex + offset, 0, _filteredItems.Count - 1);
        SelectedItem = _filteredItems[nextIndex];
    }

    public void ActivateVisibleItem(int oneBasedIndex)
    {
        var index = oneBasedIndex - 1;
        if (index >= 0 && index < _filteredItems.Count)
        {
            ActivateItem(_filteredItems[index]);
        }
    }

    public void ActivateItem(ClipCardViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        _actions.PasteItem(item.Id, asPlainText: false);
    }

    public void PasteSelected(bool asPlainText)
    {
        if (SelectedItem is null)
        {
            return;
        }

        if (asPlainText && !SelectedItem.SupportsPlainTextPaste)
        {
            return;
        }

        _actions.PasteItem(SelectedItem.Id, asPlainText);
    }

    public void TogglePinSelected()
    {
        if (SelectedItem is null)
        {
            return;
        }

        TogglePin(SelectedItem);
    }

    public void TogglePin(ClipCardViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.IsPinned = !item.IsPinned;
        _actions.TogglePinned(item.Id);
    }

    public void DeleteSelected()
    {
        if (SelectedItem is not null)
        {
            Delete(SelectedItem);
        }
    }

    public void Delete(ClipCardViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _actions.DeleteItem(item.Id);
    }

    public void UpdateCaptureState(ClipboardCaptureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RunOnUiThread(() =>
        {
            _captureState = state;
            ApplyCaptureState(state);
        });
    }

    public void UpdateGlobalHotKey(string displayText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        RunOnUiThread(() => GlobalHotKeyDisplayText = displayText);
    }

    public void RefreshLocalization()
    {
        RunOnUiThread(() =>
        {
            ApplyCaptureState(_captureState);
            OnPropertyChanged(nameof(ResultCountText));
            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyDetail));
            foreach (var item in _items)
            {
                item.RefreshLocalization();
            }
        });
    }

    private void ApplyCaptureState(ClipboardCaptureState state)
    {
        StatusText = state.Detail ?? state.Kind switch
        {
            CaptureStateKind.Recording => AppLocalization.Text("记录中"),
            CaptureStateKind.Paused => AppLocalization.Text("已暂停"),
            CaptureStateKind.PermissionRequired => AppLocalization.Text("需要剪贴板权限"),
            CaptureStateKind.StorageReadOnly => AppLocalization.Text("存储受保护"),
            CaptureStateKind.Error => AppLocalization.Text("记录异常"),
            _ => AppLocalization.Text("状态未知"),
        };

        StatusBrush = state.Kind switch
        {
            CaptureStateKind.Recording => RecordingBrush,
            CaptureStateKind.Paused => PausedBrush,
            _ => WarningBrush,
        };
    }

    public void ShowNotice(string message, PanelNoticeKind kind = PanelNoticeKind.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        RunOnUiThread(() =>
        {
            _noticeCancellation?.Cancel();
            _noticeCancellation?.Dispose();
            _noticeCancellation = new CancellationTokenSource();
            NoticeKind = kind;
            NoticeText = message;
            _ = DismissNoticeLaterAsync(_noticeCancellation.Token);
        });
    }

    public void SelectNewestVisibleItem()
    {
        SelectedItem = _filteredItems.FirstOrDefault();
    }

    public void Dispose()
    {
        _noticeCancellation?.Cancel();
        _noticeCancellation?.Dispose();
        _noticeCancellation = null;
        foreach (var item in _items)
        {
            item.Dispose();
        }

        _items.Clear();
        _filteredItems.Clear();
        GC.SuppressFinalize(this);
    }

    private async Task DismissNoticeLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3.5), cancellationToken).ConfigureAwait(false);
            RunOnUiThread(() => NoticeText = null);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReplaceItemsCore(IReadOnlyList<ClipCardViewModel> items)
    {
        var selectedId = SelectedItem?.Id;
        var retainedItems = new HashSet<ClipCardViewModel>(items, ReferenceEqualityComparer.Instance);
        foreach (var oldItem in _items)
        {
            if (!retainedItems.Contains(oldItem))
            {
                oldItem.Dispose();
            }
        }

        _items.Clear();
        foreach (var item in items)
        {
            _items.Add(item);
        }

        RebuildFilter(selectedId);
    }

    private void RebuildFilter(Guid? preferredSelectionId = null)
    {
        preferredSelectionId ??= SelectedItem?.Id;

        foreach (var item in _items)
        {
            item.ShortcutIndex = null;
        }

        _filteredItems.Clear();
        var query = SearchText.Trim();
        foreach (var item in _items)
        {
            if (IncludesSelectedKind(item) && item.Matches(query))
            {
                _filteredItems.Add(item);
            }
        }

        for (var index = 0; index < Math.Min(9, _filteredItems.Count); index++)
        {
            _filteredItems[index].ShortcutIndex = index + 1;
        }

        SelectedItem = preferredSelectionId is Guid id
            ? _filteredItems.FirstOrDefault(item => item.Id == id) ?? _filteredItems.FirstOrDefault()
            : _filteredItems.FirstOrDefault();

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
    }

    private bool IncludesSelectedKind(ClipCardViewModel item) => SelectedFilter switch
    {
        ClipTypeFilter.All => true,
        ClipTypeFilter.Text => item.Kind == ClipDisplayKind.Text,
        ClipTypeFilter.Link => item.Kind == ClipDisplayKind.Link,
        ClipTypeFilter.Image => item.Kind == ClipDisplayKind.Image,
        ClipTypeFilter.Files => item.Kind == ClipDisplayKind.Files,
        _ => false,
    };

    private IBrush FilterBrush(ClipTypeFilter filter) =>
        SelectedFilter == filter ? ActiveFilterBrush : InactiveFilterBrush;

    private void NotifyFilterPropertiesChanged()
    {
        OnPropertyChanged(nameof(AllFilterBrush));
        OnPropertyChanged(nameof(TextFilterBrush));
        OnPropertyChanged(nameof(LinkFilterBrush));
        OnPropertyChanged(nameof(ImageFilterBrush));
        OnPropertyChanged(nameof(FilesFilterBrush));
    }

    private void NotifySelectionCommandsChanged()
    {
        _pasteSelectedCommand.NotifyCanExecuteChanged();
        _pastePlainTextCommand.NotifyCanExecuteChanged();
        _togglePinCommand.NotifyCanExecuteChanged();
        _deleteCommand.NotifyCanExecuteChanged();
    }

    private static void RunOnUiThread(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
