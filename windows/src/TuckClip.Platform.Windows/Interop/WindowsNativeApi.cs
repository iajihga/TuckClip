using System.Runtime.InteropServices;
using TuckClip.Platform.Windows.Paste;

namespace TuckClip.Platform.Windows.Interop;

public interface IWindowsNativeApi
{
    int LastError { get; }

    bool AddClipboardFormatListener(nint windowHandle);

    bool RemoveClipboardFormatListener(nint windowHandle);

    bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey);

    bool UnregisterHotKey(nint windowHandle, int identifier);

    uint GetClipboardSequenceNumber();

    nint GetForegroundWindow();

    bool SetForegroundWindow(nint windowHandle);

    nint GetClipboardOwner();

    uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    bool SendPasteShortcut();
}

public sealed class WindowsNativeApi :
    IWindowsNativeApi,
    IClipboardSequenceReader,
    IForegroundWindowController,
    IPasteCommitter
{
    private readonly IPasteCommitNativeOperations _pasteCommitNative;

    public WindowsNativeApi()
        : this(SystemPasteCommitNativeOperations.Instance)
    {
    }

    internal WindowsNativeApi(IPasteCommitNativeOperations pasteCommitNative)
    {
        _pasteCommitNative = pasteCommitNative ?? throw new ArgumentNullException(nameof(pasteCommitNative));
    }

    public int LastError => Marshal.GetLastPInvokeError();

    public bool AddClipboardFormatListener(nint windowHandle)
    {
        EnsureWindows();
        return NativeMethods.AddClipboardFormatListener(windowHandle);
    }

    public bool RemoveClipboardFormatListener(nint windowHandle)
    {
        EnsureWindows();
        return NativeMethods.RemoveClipboardFormatListener(windowHandle);
    }

    public bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
    {
        EnsureWindows();
        return NativeMethods.RegisterHotKey(windowHandle, identifier, modifiers, virtualKey);
    }

    public bool UnregisterHotKey(nint windowHandle, int identifier)
    {
        EnsureWindows();
        return NativeMethods.UnregisterHotKey(windowHandle, identifier);
    }

    public uint GetClipboardSequenceNumber()
    {
        EnsureWindows();
        return NativeMethods.GetClipboardSequenceNumber();
    }

    public nint GetForegroundWindow()
    {
        EnsureWindows();
        return NativeMethods.GetForegroundWindow();
    }

    public bool SetForegroundWindow(nint windowHandle)
    {
        EnsureWindows();
        return NativeMethods.SetForegroundWindow(windowHandle);
    }

    public nint GetClipboardOwner()
    {
        EnsureWindows();
        return NativeMethods.GetClipboardOwner();
    }

    public uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier)
    {
        EnsureWindows();
        return NativeMethods.GetWindowThreadProcessId(windowHandle, out processIdentifier);
    }

    public unsafe bool SendPasteShortcut()
    {
        EnsureWindows();

        return SendPasteShortcutCore();
    }

    private static unsafe bool SendPasteShortcutCore()
    {

        var inputs = stackalloc NativeInput[4];
        inputs[0] = NativeInput.Key(NativeMethods.VirtualKeyControl, NativeKeyEventFlags.None);
        inputs[1] = NativeInput.Key(NativeMethods.VirtualKeyV, NativeKeyEventFlags.None);
        inputs[2] = NativeInput.Key(NativeMethods.VirtualKeyV, NativeKeyEventFlags.KeyUp);
        inputs[3] = NativeInput.Key(NativeMethods.VirtualKeyControl, NativeKeyEventFlags.KeyUp);

        var inserted = NativeMethods.SendInput(4, inputs, sizeof(NativeInput));
        if (inserted == 4)
        {
            return true;
        }

        // A partial SendInput must not leave Ctrl or V logically held down.
        var releases = stackalloc NativeInput[2];
        releases[0] = NativeInput.Key(NativeMethods.VirtualKeyV, NativeKeyEventFlags.KeyUp);
        releases[1] = NativeInput.Key(NativeMethods.VirtualKeyControl, NativeKeyEventFlags.KeyUp);
        _ = NativeMethods.SendInput(2, releases, sizeof(NativeInput));
        return false;
    }

    public bool IsSameWindow(PasteTargetWindow targetWindow)
    {
        _pasteCommitNative.EnsureSupported();
        return IsSameWindowCore(targetWindow);
    }

    private bool IsSameWindowCore(PasteTargetWindow targetWindow)
    {
        if (!targetWindow.IsAvailable)
        {
            return false;
        }

        var threadId = _pasteCommitNative.GetWindowThreadProcessId(
            targetWindow.Handle,
            out var processId);
        return threadId == targetWindow.ThreadId && processId == targetWindow.ProcessId;
    }

    public PasteCommitResult TryCommitPaste(
        PasteTargetWindow targetWindow,
        uint expectedSequenceNumber)
    {
        _pasteCommitNative.EnsureSupported();
        if (!IsSameWindowCore(targetWindow))
        {
            return PasteCommitResult.TargetWindowUnavailable;
        }

        if (_pasteCommitNative.IsModifierKeyDown())
        {
            return PasteCommitResult.ModifierKeysPressed;
        }

        if (_pasteCommitNative.GetClipboardSequenceNumber() != expectedSequenceNumber)
        {
            return PasteCommitResult.ClipboardChanged;
        }

        // Foreground is deliberately the final observation before SendInput.
        if (_pasteCommitNative.GetForegroundWindow() != targetWindow.Handle)
        {
            return PasteCommitResult.TargetWindowUnavailable;
        }

        return _pasteCommitNative.SendPasteShortcut()
            ? PasteCommitResult.Pasted
            : PasteCommitResult.InputRejected;
    }

    uint IClipboardSequenceReader.GetSequenceNumber() => GetClipboardSequenceNumber();

    nint IForegroundWindowController.GetForegroundWindow() => GetForegroundWindow();

    bool IForegroundWindowController.TryActivate(nint targetWindow) =>
        targetWindow != 0 && SetForegroundWindow(targetWindow);

    bool IForegroundWindowController.IsSameWindow(PasteTargetWindow targetWindow) =>
        IsSameWindow(targetWindow);

    PasteCommitResult IPasteCommitter.TryCommitPaste(
        PasteTargetWindow targetWindow,
        uint expectedSequenceNumber) =>
        TryCommitPaste(targetWindow, expectedSequenceNumber);

    private static bool IsModifierKeyDownCore() =>
        IsKeyDown(NativeMethods.VirtualKeyShift) ||
        IsKeyDown(NativeMethods.VirtualKeyControl) ||
        IsKeyDown(NativeMethods.VirtualKeyMenu) ||
        IsKeyDown(NativeMethods.VirtualKeyLeftWindows) ||
        IsKeyDown(NativeMethods.VirtualKeyRightWindows);

    private static bool IsKeyDown(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Win32 platform layer can only be invoked on Windows.");
        }
    }

    private sealed class SystemPasteCommitNativeOperations : IPasteCommitNativeOperations
    {
        internal static SystemPasteCommitNativeOperations Instance { get; } = new();

        public void EnsureSupported() => EnsureWindows();

        public uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier) =>
            NativeMethods.GetWindowThreadProcessId(windowHandle, out processIdentifier);

        public bool IsModifierKeyDown() => IsModifierKeyDownCore();

        public uint GetClipboardSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

        public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

        public bool SendPasteShortcut() => SendPasteShortcutCore();
    }
}

internal interface IPasteCommitNativeOperations
{
    void EnsureSupported();

    uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    bool IsModifierKeyDown();

    uint GetClipboardSequenceNumber();

    nint GetForegroundWindow();

    bool SendPasteShortcut();
}
