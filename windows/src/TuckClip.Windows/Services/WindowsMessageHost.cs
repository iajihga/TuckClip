using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.Services;

internal sealed class WindowsMessageHost : Window, IDisposable
{
    private const int FirstHotKeyIdentifier = 0x5443;
    private const int SecondHotKeyIdentifier = 0x5444;

    private readonly Win32Properties.CustomWndProcHookCallback _windowProcedure;
    private ClipboardListenerRegistration? _clipboardRegistration;
    private GlobalHotKeySwitcher? _hotKeySwitcher;
    private uint _showMessage;
    private bool _started;
    private bool _disposed;

    public WindowsMessageHost()
    {
        Width = 1;
        Height = 1;
        MinWidth = 1;
        MinHeight = 1;
        Opacity = 0;
        Position = new PixelPoint(-32_000, -32_000);
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        Focusable = false;
        IsHitTestVisible = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Title = "TuckClip message host";
        _windowProcedure = HandleWindowMessage;
    }

    public event EventHandler? ClipboardUpdated;

    public event EventHandler? HotKeyPressed;

    public event EventHandler? ShowRequested;

    public bool IsClipboardMonitoringAvailable => _clipboardRegistration is not null;

    public IReadOnlyList<string> Start(IWindowsNativeApi nativeApi, GlobalHotKey hotKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nativeApi);
        if (_started)
        {
            throw new InvalidOperationException("The Windows message host has already started.");
        }

        _started = true;
        Win32Properties.AddWndProcHookCallback(this, _windowProcedure);
        Show();

        if (!OperatingSystem.IsWindows())
        {
            return [AppLocalization.Text("Win32 剪贴板监听在当前系统上不可用。")];
        }

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == 0)
        {
            return [AppLocalization.Text("TuckClip 无法创建 Win32 消息窗口。")];
        }

        var warnings = new List<string>();
        try
        {
            _clipboardRegistration = ClipboardListenerRegistration.Register(nativeApi, handle);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            warnings.Add(AppLocalization.Format("剪贴板监听未能启动：{0}", exception.Message));
        }

        try
        {
            _hotKeySwitcher = new GlobalHotKeySwitcher(
                nativeApi,
                handle,
                FirstHotKeyIdentifier,
                SecondHotKeyIdentifier);
            using var change = _hotKeySwitcher.Stage(hotKey);
            change.Commit();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            warnings.Add(AppLocalization.Format(
                "{0} 注册失败：{1}",
                hotKey.DisplayText,
                exception.Message));
        }

        _showMessage = SingleInstanceGuard.GetShowMessageId();
        if (_showMessage == 0)
        {
            warnings.Add(AppLocalization.Text("无法接收另一个 TuckClip 进程的唤起请求。"));
        }

        return warnings;
    }

    public GlobalHotKeySwitcher.GlobalHotKeyChange StageHotKey(GlobalHotKey hotKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_hotKeySwitcher ?? throw new InvalidOperationException(
            AppLocalization.Text("全局快捷键服务不可用。")))
            .Stage(hotKey);
    }

    public GlobalHotKey? ActiveHotKey => _hotKeySwitcher?.Current;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotKeySwitcher?.Dispose();
        _clipboardRegistration?.Dispose();
        _hotKeySwitcher = null;
        _clipboardRegistration = null;
        Win32Properties.RemoveWndProcHookCallback(this, _windowProcedure);
        Close();
        GC.SuppressFinalize(this);
    }

    private nint HandleWindowMessage(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        _ = window;
        _ = lParam;

        if (message == Win32MessageIds.ClipboardUpdate)
        {
            handled = true;
            PostEvent(ClipboardUpdated);
        }
        else if (message == Win32MessageIds.HotKey
                 && _hotKeySwitcher?.IsActiveIdentifier((int)wParam) == true)
        {
            handled = true;
            PostEvent(HotKeyPressed);
        }
        else if (_showMessage != 0 && message == _showMessage)
        {
            handled = true;
            PostEvent(ShowRequested);
        }

        return nint.Zero;
    }

    private void PostEvent(EventHandler? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceError(
                        "A TuckClip message handler failed: {0}",
                        exception.Message);
                }
            }
        });
    }
}
