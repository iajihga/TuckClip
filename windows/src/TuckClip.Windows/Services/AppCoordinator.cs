using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using TuckClip.Core;
using TuckClip.Core.Persistence;
using TuckClip.Platform.Windows.Clipboard;
using TuckClip.Platform.Windows.Interop;
using TuckClip.Platform.Windows.Paste;
using TuckClip.Platform.Windows.Security;
using TuckClip.Windows.ViewModels;
using TuckClip.Windows.Views;

namespace TuckClip.Windows.Services;

internal sealed class AppCoordinator : IClipboardUiActions, IPanelSessionBoundary, IDisposable, IAsyncDisposable
{
    private readonly Action _requestShutdown;
    private readonly object _sessionLock = new();
    private readonly object _backgroundTaskLock = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private WindowsAppSettings _settings = new();
    private WindowsSettingsStore? _settingsStore;
    private HistoryStore? _historyStore;
    private WindowsNativeApi? _nativeApi;
    private WindowsMessageHost? _messageHost;
    private AvaloniaClipboardAdapter? _clipboard;
    private PasteService? _pasteService;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _recordingMenuItem;
    private NativeMenuItem? _openMenuItem;
    private PastePanelSession? _panelSession;
    private CancellationTokenSource? _pasteCancellation;
    private string? _deferredNotice;
    private string? _storageError;
    private string? _hotKeyError;
    private int _captureRequested;
    private int _capturePumpRunning;
    private int _settingsGeneration;
    private Task? _stopTask;
    private bool _started;
    private bool _stopping;
    private bool _disposed;
    private bool _showPanelWhenReady;

    public AppCoordinator(Action requestShutdown)
    {
        _requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The application coordinator has already started.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The TuckClip Windows client must run on Windows.");
        }

        _started = true;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var token = linkedCancellation.Token;

        await _lifecycleGate.WaitAsync(token);
        try
        {

            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("Windows did not provide a local application data directory.");
            }

            var dataDirectory = Path.Combine(localApplicationData, "TuckClip");
            _settingsStore = new WindowsSettingsStore(dataDirectory);
            try
            {
                _settings = (await _settingsStore.LoadAsync(token)).Validate();
            }
            catch (WindowsSettingsCorruptedException exception)
            {
                _settings = WindowsAppSettings.PrivacySafeRecovery;
                _storageError = exception.Message;
                const string recoveryNotice =
                    "设置读取失败，已暂停记录并关闭自动粘贴；原文件尚未被改写。";
                _deferredNotice = string.IsNullOrWhiteSpace(_deferredNotice)
                    ? recoveryNotice
                    : string.Join(Environment.NewLine, recoveryNotice, _deferredNotice);
            }

            _nativeApi = new WindowsNativeApi();
            _messageHost = new WindowsMessageHost();
            _messageHost.ClipboardUpdated += OnClipboardUpdated;
            _messageHost.HotKeyPressed += OnHotKeyPressed;
            _messageHost.ShowRequested += OnShowRequested;
            var startupWarnings = _messageHost.Start(_nativeApi, _settings.GlobalHotKey);
            if (_messageHost.ActiveHotKey is null)
            {
                _hotKeyError = $"{_settings.GlobalHotKey.DisplayText} 注册失败，请录入其他组合键。";
            }
            if (startupWarnings.Count > 0)
            {
                _deferredNotice = string.IsNullOrWhiteSpace(_deferredNotice)
                    ? string.Join(Environment.NewLine, startupWarnings)
                    : string.Join(Environment.NewLine, _deferredNotice, string.Join(Environment.NewLine, startupWarnings));
            }

            var protector = new DpapiCurrentUserDataProtector();
            var repository = new EncryptedFileHistoryRepository(dataDirectory, protector);
            _historyStore = new HistoryStore(repository, _settings.ToCoreSettings());
            await _historyStore.InitializeAsync(token);
            _historyStore.Changed += OnHistoryChanged;

            _mainWindow = new MainWindow(this)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            _settingsWindow = new SettingsWindow(this)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            var ownerResolver = new ClipboardOwnerResolver(_nativeApi, new SystemProcessNameResolver());
            _clipboard = new AvaloniaClipboardAdapter(
                _messageHost.Clipboard ?? throw new InvalidOperationException("The Windows clipboard is unavailable."),
                _messageHost.StorageProvider,
                _nativeApi,
                ownerResolver);
            _pasteService = new PasteService(this, _nativeApi, _nativeApi);

            CreateTrayIcon();
            RefreshHistoryView();
            RefreshSettingsView();
            RefreshCaptureState(_messageHost.IsClipboardMonitoringAvailable
                ? null
                : "剪贴板监听未能启动");
            if (_showPanelWhenReady)
            {
                _showPanelWhenReady = false;
                ShowPanel(capturePasteTarget: false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void PasteItem(Guid itemId, bool asPlainText)
    {
        if (!TryGetInitializedServices(out var history, out _, out _, out _))
        {
            return;
        }

        var item = history.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            ShowNotice("这条记录已不存在。", PanelNoticeKind.Error);
            RefreshHistoryView();
            return;
        }

        CancellationTokenSource cancellation;
        lock (_sessionLock)
        {
            _pasteCancellation?.Cancel();
            _pasteCancellation?.Dispose();
            _pasteCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            cancellation = _pasteCancellation;
        }

        TrackBackgroundTask(PasteItemAsync(item, asPlainText, cancellation.Token));
    }

    public void TogglePinned(Guid itemId) =>
        TrackBackgroundTask(RunHistoryMutationAsync(
            async (history, token) =>
            {
                var item = history.Items.FirstOrDefault(candidate => candidate.Id == itemId);
                return item is null
                    ? new HistoryMutationResult(HistoryMutationStatus.NotFound)
                    : await history.SetPinnedAsync(itemId, !item.IsPinned, DateTimeOffset.UtcNow, token);
            },
            "置顶状态未能保存。"));

    public void DeleteItem(Guid itemId) =>
        TrackBackgroundTask(RunHistoryMutationAsync(
            (history, token) => history.DeleteAsync(itemId, token),
            "这条记录未能删除。"));

    public void ClearHistory(ClearHistoryScope scope) =>
        TrackBackgroundTask(RunHistoryMutationAsync(
            (history, token) => scope == ClearHistoryScope.All
                ? history.ClearAllAsync(token)
                : history.ClearUnpinnedAsync(token),
            "历史记录未能清除。"));

    public void ApplySettings(ClipboardSettingsDraft settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WindowsAppSettings candidate;
        try
        {
            candidate = new WindowsAppSettings
            {
                RecordingEnabled = settings.RecordingEnabled,
                AutomaticPasteEnabled = settings.AutomaticPasteEnabled,
                CapturesImages = settings.CapturesImages,
                RetentionDays = settings.RetentionDays,
                MaximumItemCount = settings.MaximumItemCount,
                ExcludedProcessNames = settings.ExcludedProcessNames,
                GlobalHotKey = _settings.GlobalHotKey,
            }.Validate();
        }
        catch (ArgumentException exception)
        {
            ShowNotice(exception.Message, PanelNoticeKind.Error);
            RefreshSettingsView();
            return;
        }

        var generation = Interlocked.Increment(ref _settingsGeneration);
        TrackBackgroundTask(ApplySettingsAsync(candidate, generation, retryHotKey: false));
    }

    public void ChangeGlobalHotKey(GlobalHotKey hotKey, ClipboardSettingsDraft settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WindowsAppSettings candidate;
        try
        {
            candidate = new WindowsAppSettings
            {
                RecordingEnabled = settings.RecordingEnabled,
                AutomaticPasteEnabled = settings.AutomaticPasteEnabled,
                CapturesImages = settings.CapturesImages,
                RetentionDays = settings.RetentionDays,
                MaximumItemCount = settings.MaximumItemCount,
                ExcludedProcessNames = settings.ExcludedProcessNames,
                GlobalHotKey = hotKey,
            }.Validate();
        }
        catch (ArgumentException exception)
        {
            _hotKeyError = exception.Message;
            RefreshSettingsView();
            return;
        }

        var generation = Interlocked.Increment(ref _settingsGeneration);
        TrackBackgroundTask(ApplySettingsAsync(candidate, generation, retryHotKey: true));
    }

    public void RevealDataDirectory()
    {
        if (_settingsStore is null || _messageHost is null)
        {
            return;
        }

        TrackBackgroundTask(RevealDataDirectoryAsync(_settingsStore.DataDirectory, _messageHost));
    }

    public void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        HidePanel();
        RefreshSettingsView();
        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    public void HidePanel()
    {
        PastePanelSession? session;
        CancellationTokenSource? pasteCancellation;
        lock (_sessionLock)
        {
            session = _panelSession;
            _panelSession = null;
            pasteCancellation = _pasteCancellation;
            _pasteCancellation = null;
        }

        pasteCancellation?.Cancel();
        pasteCancellation?.Dispose();
        session?.Dispose();
        _mainWindow?.Hide();
    }

    public bool IsCurrent(PastePanelSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (_sessionLock)
        {
            return ReferenceEquals(_panelSession, session) && !session.IsCancellationRequested;
        }
    }

    public ValueTask<bool> HideAsync(PastePanelSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ValueTask.FromResult(HideSessionCore(session, cancellationToken));
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(HideSessionCore(session, cancellationToken));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return new ValueTask<bool>(completion.Task);
    }

    public PasteCommitResult CommitIfCurrent(
        PastePanelSession session,
        Func<PasteCommitResult> commit)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(commit);
        lock (_sessionLock)
        {
            if (!ReferenceEquals(_panelSession, session) || session.IsCancellationRequested)
            {
                return PasteCommitResult.StaleSession;
            }

            return commit();
        }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (_backgroundTaskLock)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            _stopping = true;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
        }

        _lifetimeCancellation.Cancel();
        _ = RunStopCoreAsync(completion);
        return completion.Task;
    }

    public void Dispose()
    {
        var stopTask = StopAsync();
        if (stopTask.IsCompletedSuccessfully)
        {
            stopTask.GetAwaiter().GetResult();
        }
        else if (stopTask.IsFaulted)
        {
            _ = stopTask.Exception;
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private async Task RunStopCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            await StopCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task StopCoreAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            HidePanel();

            if (_historyStore is not null)
            {
                _historyStore.Changed -= OnHistoryChanged;
            }

            if (_messageHost is not null)
            {
                _messageHost.ClipboardUpdated -= OnClipboardUpdated;
                _messageHost.HotKeyPressed -= OnHotKeyPressed;
                _messageHost.ShowRequested -= OnShowRequested;
            }

            var backgroundTasksDrained = await BoundedTaskDrain.WaitUntilIdleAsync(
                GetPendingBackgroundTasks,
                TimeSpan.FromSeconds(3));
            if (backgroundTasksDrained)
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
                _settingsWindow?.CloseForApplicationExit();
                _mainWindow?.CloseForApplicationExit();
                _messageHost?.Dispose();
                _historyStore?.Dispose();
                _settingsGate.Dispose();
                _lifetimeCancellation.Dispose();
            }
            else
            {
                // Clipboard and launcher APIs are not all cancellable. Avoid
                // disposing objects a timed-out operation could still touch;
                // the process is exiting and the OS will reclaim them.
                System.Diagnostics.Trace.TraceWarning(
                    "TuckClip shutdown continued after the background-task drain deadline.");
            }

            _disposed = true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task PasteItemAsync(ClipItem item, bool asPlainText, CancellationToken cancellationToken)
    {
        if (!TryGetInitializedServices(out _, out var clipboard, out var pasteService, out var mainWindow))
        {
            return;
        }

        PastePanelSession? session;
        lock (_sessionLock)
        {
            session = _panelSession;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            var writeOperation = clipboard.CreateWriteOperation(item, asPlainText);
            if (!_settings.AutomaticPasteEnabled || !session.TargetWindow.IsAvailable)
            {
                _ = await writeOperation.WriteAsync(cancellationToken);
                HidePanel();
                return;
            }

            var result = await pasteService.PasteAsync(session, writeOperation, cancellationToken);
            if (!result.WasPasted &&
                result.FailureReason is not PasteFailureReason.SessionCancelled and
                not PasteFailureReason.RequestCancelled and
                not PasteFailureReason.StalePanelSession)
            {
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                }
                mainWindow.Activate();
                mainWindow.FocusSearch();
                ShowNotice(GetPasteFailureMessage(result.FailureReason), PanelNoticeKind.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer user action or panel session superseded this paste.
        }
        catch (ClipboardWriteConflictException)
        {
            ShowNotice(
                "系统剪贴板在写入确认期间又发生变化；为避免误粘贴，已停止，请重试。",
                PanelNoticeKind.Error);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            ShowNotice($"复制失败：{exception.Message}", PanelNoticeKind.Error);
        }
    }

    private async Task ApplySettingsAsync(
        WindowsAppSettings candidate,
        int generation,
        bool retryHotKey)
    {
        if (_historyStore is null || _settingsStore is null)
        {
            return;
        }

        try
        {
            await _settingsGate.WaitAsync(_lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (generation != Volatile.Read(ref _settingsGeneration))
            {
                return;
            }

            GlobalHotKeySwitcher.GlobalHotKeyChange? hotKeyChange = null;
            if (retryHotKey || candidate.GlobalHotKey != _settings.GlobalHotKey)
            {
                try
                {
                    hotKeyChange = (_messageHost ?? throw new InvalidOperationException(
                        "快捷键服务尚未启动。"))
                        .StageHotKey(candidate.GlobalHotKey);
                }
                catch (Exception exception) when (exception is
                    System.ComponentModel.Win32Exception or
                    InvalidOperationException or
                    ArgumentException)
                {
                    var message = exception is System.ComponentModel.Win32Exception
                        ? $"{candidate.GlobalHotKey.DisplayText} 已被系统或其他应用占用。"
                        : exception.Message;
                    _hotKeyError = message;
                    ShowNotice(message, PanelNoticeKind.Error);
                    RefreshSettingsView();
                    return;
                }
            }

            using (hotKeyChange)
            {
                try
                {
                    // The new registration is staged while the current shortcut
                    // remains active. Persist first, then atomically commit the
                    // registration; save failure simply disposes the staged key.
                    await _settingsStore.SaveAsync(candidate, _lifetimeCancellation.Token);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _storageError = $"设置未能写入磁盘：{exception.Message}";
                    ShowNotice(_storageError, PanelNoticeKind.Error);
                    RefreshSettingsView();
                    return;
                }

                hotKeyChange?.Commit();
            }

            _settings = candidate;
            _hotKeyError = null;
            var newCore = candidate.ToCoreSettings();
            await _historyStore.ApplySettingsAsync(newCore, _lifetimeCancellation.Token);
            _storageError = _historyStore.IsReadOnly
                ? "历史文件损坏，已进入只读保护；原文件没有被覆盖。"
                : null;

            // A newer queued candidate owns the visible state. Keep the last
            // successful disk/in-memory pair coherent, but avoid flashing an
            // intermediate draft back into the settings window.
            if (generation != Volatile.Read(ref _settingsGeneration))
            {
                return;
            }

            RefreshCaptureState();
            RefreshTrayMenu();
            RefreshSettingsView();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private async Task RunHistoryMutationAsync(
        Func<HistoryStore, CancellationToken, Task<HistoryMutationResult>> mutation,
        string failureMessage)
    {
        if (_historyStore is null)
        {
            return;
        }

        try
        {
            var result = await mutation(_historyStore, _lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                ShowNotice(failureMessage, PanelNoticeKind.Error);
                RefreshHistoryView();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task RevealDataDirectoryAsync(string dataDirectory, WindowsMessageHost host)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var folder = await host.StorageProvider.TryGetFolderFromPathAsync(
                new Uri(Path.GetFullPath(dataDirectory)));
            if (folder is null || !await host.Launcher.LaunchFileAsync(folder))
            {
                ShowNotice("无法打开数据目录。", PanelNoticeKind.Error);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowNotice($"无法打开数据目录：{exception.Message}", PanelNoticeKind.Error);
        }
    }

    private void OnClipboardUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_stopping || !_settings.RecordingEnabled || _clipboard is null || _historyStore is null)
        {
            return;
        }

        Interlocked.Exchange(ref _captureRequested, 1);
        if (Interlocked.CompareExchange(ref _capturePumpRunning, 1, 0) == 0)
        {
            TrackBackgroundTask(PumpClipboardCapturesAsync());
        }
    }

    private async Task PumpClipboardCapturesAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _captureRequested, 0) == 1)
            {
                if (!_settings.RecordingEnabled || _clipboard is null || _historyStore is null)
                {
                    continue;
                }

                try
                {
                    var capture = await _clipboard.TryReadCaptureAsync(
                        _settings,
                        _lifetimeCancellation.Token);
                    if (capture is null)
                    {
                        continue;
                    }

                    var result = await _historyStore.CaptureAsync(capture, _lifetimeCancellation.Token);
                    if (result.Status == HistoryMutationStatus.PersistenceFailed)
                    {
                        ShowNotice("剪贴板记录未能写入磁盘。", PanelNoticeKind.Error);
                    }
                    else if (result.Status == HistoryMutationStatus.ReadOnly)
                    {
                        RefreshCaptureState("历史文件处于只读保护状态");
                    }
                }
                catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    ShowNotice($"读取剪贴板失败：{exception.Message}", PanelNoticeKind.Error);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _capturePumpRunning, 0);
            if (!_stopping &&
                Volatile.Read(ref _captureRequested) == 1 &&
                Interlocked.CompareExchange(ref _capturePumpRunning, 1, 0) == 0)
            {
                TrackBackgroundTask(PumpClipboardCapturesAsync());
            }
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_stopping)
            {
                RefreshHistoryView();
            }
        });
    }

    private void OnHotKeyPressed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_settingsWindow?.IsVisible == true
            && _settingsWindow.ViewModel.IsRecordingHotKey)
        {
            _settingsWindow.ViewModel.CaptureHotKey(_settings.GlobalHotKey);
            return;
        }

        if (_mainWindow is null)
        {
            _showPanelWhenReady = true;
            return;
        }

        TogglePanel(capturePasteTarget: true);
    }

    private void OnShowRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (_mainWindow is null)
        {
            _showPanelWhenReady = true;
            return;
        }

        ShowPanel(capturePasteTarget: false);
    }

    private void TogglePanel(bool capturePasteTarget)
    {
        if (_mainWindow?.IsVisible == true)
        {
            HidePanel();
        }
        else
        {
            ShowPanel(capturePasteTarget);
        }
    }

    private void ShowPanel(bool capturePasteTarget)
    {
        if (_mainWindow is null || _nativeApi is null)
        {
            return;
        }

        var targetWindow = capturePasteTarget
            ? CapturePasteTarget(_nativeApi)
            : PasteTargetWindow.Unavailable;
        PastePanelSession? previous;
        CancellationTokenSource? previousPaste;
        lock (_sessionLock)
        {
            previous = _panelSession;
            _panelSession = new PastePanelSession(targetWindow);
            previousPaste = _pasteCancellation;
            _pasteCancellation = null;
        }
        previousPaste?.Cancel();
        previousPaste?.Dispose();
        previous?.Dispose();

        RefreshHistoryView();
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        _mainWindow.Activate();
        _mainWindow.FocusSearch();

        if (!string.IsNullOrWhiteSpace(_deferredNotice))
        {
            _mainWindow.ViewModel.ShowNotice(_deferredNotice, PanelNoticeKind.Error);
            _deferredNotice = null;
        }
    }

    private bool HideSessionCore(PastePanelSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sessionLock)
        {
            if (!ReferenceEquals(_panelSession, session) || session.IsCancellationRequested)
            {
                return false;
            }
        }

        _mainWindow?.Hide();
        return true;
    }

    private void CreateTrayIcon()
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://TuckClip/Assets/TuckClip.ico"));
        var menu = new NativeMenu();
        _openMenuItem = new NativeMenuItem();
        _openMenuItem.Click += (_, _) => ShowPanel(capturePasteTarget: false);
        _recordingMenuItem = new NativeMenuItem();
        _recordingMenuItem.Click += (_, _) => ToggleRecordingFromTray();
        var settingsItem = new NativeMenuItem("设置…");
        settingsItem.Click += (_, _) => ShowSettings();
        var quitItem = new NativeMenuItem("退出 TuckClip");
        quitItem.Click += (_, _) => _requestShutdown();
        menu.Add(_openMenuItem);
        menu.Add(_recordingMenuItem);
        menu.Add(settingsItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "TuckClip · 本地剪贴板历史",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => TogglePanel(capturePasteTarget: false);
        RefreshTrayMenu();
    }

    private void ToggleRecordingFromTray()
    {
        ApplySettings(new ClipboardSettingsDraft(
            !_settings.RecordingEnabled,
            _settings.AutomaticPasteEnabled,
            _settings.CapturesImages,
            _settings.RetentionDays,
            _settings.MaximumItemCount,
            _settings.ExcludedProcessNames));
    }

    private void RefreshTrayMenu()
    {
        if (_recordingMenuItem is not null)
        {
            _recordingMenuItem.Header = _settings.RecordingEnabled ? "暂停记录" : "继续记录";
        }
        if (_openMenuItem is not null)
        {
            _openMenuItem.Header = $"打开 TuckClip（{_settings.GlobalHotKey.DisplayText}）";
        }
        if (_trayIcon is not null)
        {
            _trayIcon.ToolTipText = $"TuckClip · {_settings.GlobalHotKey.DisplayText}";
        }
    }

    private void RefreshHistoryView()
    {
        if (_historyStore is null || _mainWindow is null)
        {
            return;
        }

        var viewModels = _historyStore.Items.Select(CreateCardViewModel).ToArray();
        _mainWindow.ViewModel.ReplaceItems(viewModels);
        _mainWindow.ViewModel.UpdateGlobalHotKey(_settings.GlobalHotKey.DisplayText);
    }

    private void RefreshSettingsView()
    {
        if (_settingsWindow is null || _settingsStore is null || _historyStore is null)
        {
            return;
        }

        _settingsWindow.ViewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            _settings.RecordingEnabled,
            _settings.AutomaticPasteEnabled,
            _settings.CapturesImages,
            _settings.RetentionDays,
            _settings.MaximumItemCount,
            _settings.ExcludedProcessNames,
            _settingsStore.DataDirectory,
            _historyStore.IsReadOnly,
            _storageError ?? (_historyStore.IsReadOnly
                ? "历史文件损坏，已进入只读保护；原文件没有被覆盖。"
                : null),
            _settings.GlobalHotKey,
            _hotKeyError));
    }

    private void RefreshCaptureState(string? detail = null)
    {
        if (_mainWindow is null || _historyStore is null)
        {
            return;
        }

        var state = _historyStore.IsReadOnly
            ? new ClipboardCaptureState(CaptureStateKind.StorageReadOnly, detail)
            : !_settings.RecordingEnabled
                ? new ClipboardCaptureState(CaptureStateKind.Paused, detail)
                : detail is null
                    ? new ClipboardCaptureState(CaptureStateKind.Recording)
                    : new ClipboardCaptureState(CaptureStateKind.Error, detail);
        _mainWindow.ViewModel.UpdateCaptureState(state);
    }

    private void ShowNotice(string message, PanelNoticeKind kind)
    {
        if (_mainWindow?.IsVisible == true)
        {
            _mainWindow.ViewModel.ShowNotice(message, kind);
        }
        else
        {
            _deferredNotice = message;
        }
    }

    private bool TryGetInitializedServices(
        out HistoryStore history,
        out AvaloniaClipboardAdapter clipboard,
        out PasteService pasteService,
        out MainWindow mainWindow)
    {
        history = _historyStore!;
        clipboard = _clipboard!;
        pasteService = _pasteService!;
        mainWindow = _mainWindow!;
        return !_disposed && history is not null && clipboard is not null && pasteService is not null && mainWindow is not null;
    }

    private static ClipCardViewModel CreateCardViewModel(ClipItem item)
    {
        var kind = item.Kind switch
        {
            ClipKind.Text => ClipDisplayKind.Text,
            ClipKind.Link => ClipDisplayKind.Link,
            ClipKind.Image => ClipDisplayKind.Image,
            ClipKind.Files => ClipDisplayKind.Files,
            _ => throw new InvalidDataException("Unsupported clipboard item kind."),
        };

        var title = item.Kind switch
        {
            ClipKind.Text => FirstDisplayLine(item.PlainText, 120),
            ClipKind.Link => Truncate(item.PlainText ?? string.Empty, 160),
            ClipKind.Image => "图片",
            ClipKind.Files => item.FilePaths.Count == 1
                ? Path.GetFileName(item.FilePaths[0]) ?? string.Empty
                : $"{item.FilePaths.Count} 个文件",
            _ => string.Empty,
        };
        var detail = item.Kind switch
        {
            ClipKind.Text => Truncate(item.PlainText ?? string.Empty, 320),
            ClipKind.Link => item.PlainText is { } link && Uri.TryCreate(link, UriKind.Absolute, out var uri)
                ? uri.Host
                : string.Empty,
            ClipKind.Image => item.ImageData is { } image ? FormatByteCount(image.Length) : "图片数据不可用",
            ClipKind.Files => string.Join(Environment.NewLine, item.FilePaths.Take(3)),
            _ => string.Empty,
        };
        var searchable = item.Kind == ClipKind.Files
            ? string.Join(" ", item.FilePaths)
            : item.PlainText;

        return new ClipCardViewModel(
            item.Id,
            kind,
            title,
            detail,
            searchable,
            item.SourceAppName,
            item.SourceIdentifier,
            item.UpdatedAt,
            item.IsPinned,
            item.ImageData);
    }

    private static string FirstDisplayLine(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var lineEnd = value.IndexOfAny(['\r', '\n']);
        return Truncate(lineEnd >= 0 ? value[..lineEnd] : value, maximumLength);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength - 1), "…");

    private static string FormatByteCount(int byteCount) => byteCount switch
    {
        >= 1024 * 1024 => $"{byteCount / (1024d * 1024d):0.0} MB",
        >= 1024 => $"{byteCount / 1024d:0.0} KB",
        _ => $"{byteCount} B",
    };

    private static string GetPasteFailureMessage(PasteFailureReason reason) => reason switch
    {
        PasteFailureReason.TargetWindowUnavailable => "已复制，但系统没有把焦点交回原窗口；请手动按 Ctrl+V。",
        PasteFailureReason.ClipboardChanged =>
            "已复制，但在发送 Ctrl+V 前系统剪贴板又发生变化；为避免粘贴错误内容，已取消自动粘贴。",
        PasteFailureReason.ModifierKeysPressed => "已复制，但检测到仍按住修饰键；请松开后手动按 Ctrl+V。",
        PasteFailureReason.InputRejected => "已复制，但目标窗口拒绝自动粘贴；管理员应用需要手动按 Ctrl+V。",
        _ => "已复制，但自动粘贴未完成；请手动按 Ctrl+V。",
    };

    private static PasteTargetWindow CapturePasteTarget(WindowsNativeApi nativeApi)
    {
        var handle = nativeApi.GetForegroundWindow();
        if (handle == 0)
        {
            return PasteTargetWindow.Unavailable;
        }

        var threadId = nativeApi.GetWindowThreadProcessId(handle, out var processId);
        if (threadId == 0 || processId == 0 || processId == (uint)Environment.ProcessId)
        {
            return PasteTargetWindow.Unavailable;
        }

        return new PasteTargetWindow(handle, threadId, processId);
    }

    private void TrackBackgroundTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_backgroundTaskLock)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_backgroundTaskLock)
                {
                    _backgroundTasks.Remove(completedTask);
                }

                if (completedTask.IsFaulted)
                {
                    System.Diagnostics.Trace.TraceError(
                        "A TuckClip background operation failed: {0}",
                        completedTask.Exception?.GetBaseException().Message);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private IReadOnlyList<Task> GetPendingBackgroundTasks()
    {
        lock (_backgroundTaskLock)
        {
            return _backgroundTasks.Where(static task => !task.IsCompleted).ToArray();
        }
    }
}
