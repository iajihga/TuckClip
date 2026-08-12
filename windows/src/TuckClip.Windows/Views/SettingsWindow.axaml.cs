using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TuckClip.Platform.Windows.Interop;
using TuckClip.Windows.ViewModels;

namespace TuckClip.Windows.Views;

public sealed partial class SettingsWindow : Window
{
    private bool _allowClose;

    public SettingsWindow()
        : this(new ClipboardSettingsViewModel())
    {
    }

    public SettingsWindow(IClipboardUiActions actions)
        : this(new ClipboardSettingsViewModel(actions))
    {
    }

    public SettingsWindow(ClipboardSettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
    }

    public ClipboardSettingsViewModel ViewModel { get; }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose
            || e.CloseReason is WindowCloseReason.ApplicationShutdown
                or WindowCloseReason.OSShutdown
                or WindowCloseReason.OwnerWindowClosing)
        {
            return;
        }

        e.Cancel = true;
        HideSettings();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel.IsRecordingHotKey)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                ViewModel.CancelHotKeyRecording();
                e.Handled = true;
                return;
            }

            if (TryCreateHotKey(e, out var hotKey))
            {
                ViewModel.CaptureHotKey(hotKey);
            }
            else if (!IsModifierKey(e.Key))
            {
                ViewModel.CaptureHotKey(new GlobalHotKey(
                    0,
                    ToHotKeyModifiers(e.KeyModifiers)));
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (ViewModel.IsClearConfirmationVisible)
        {
            ViewModel.CancelClear();
        }
        else
        {
            HideSettings();
        }

        e.Handled = true;
    }

    private void OnHideClick(object? sender, RoutedEventArgs e) => HideSettings();

    private void HideSettings()
    {
        ViewModel.CancelHotKeyRecording();
        ViewModel.CancelClear();
        Hide();
    }

    private static bool TryCreateHotKey(KeyEventArgs e, out GlobalHotKey hotKey)
    {
        var virtualKey = ToVirtualKey(e.Key);
        var modifiers = ToHotKeyModifiers(e.KeyModifiers);
        hotKey = new GlobalHotKey(virtualKey, modifiers);
        return virtualKey != 0 && modifiers != HotKeyModifiers.None;
    }

    private static HotKeyModifiers ToHotKeyModifiers(KeyModifiers modifiers)
    {
        var result = HotKeyModifiers.None;
        if ((modifiers & KeyModifiers.Control) != 0)
        {
            result |= HotKeyModifiers.Control;
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            result |= HotKeyModifiers.Alt;
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            result |= HotKeyModifiers.Shift;
        }
        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            result |= HotKeyModifiers.Windows;
        }
        return result;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;

    private static uint ToVirtualKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return 0x41u + (uint)((int)key - (int)Key.A);
        }
        if (key is >= Key.D0 and <= Key.D9)
        {
            return 0x30u + (uint)((int)key - (int)Key.D0);
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return 0x60u + (uint)((int)key - (int)Key.NumPad0);
        }
        if (key is >= Key.F1 and <= Key.F24)
        {
            return 0x70u + (uint)((int)key - (int)Key.F1);
        }

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            _ => 0,
        };
    }
}
