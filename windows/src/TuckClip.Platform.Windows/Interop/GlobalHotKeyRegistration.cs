using System.ComponentModel;

namespace TuckClip.Platform.Windows.Interop;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000,
}

public sealed class GlobalHotKeyRegistration : IDisposable
{
    private readonly IWindowsNativeApi _nativeApi;
    private readonly nint _windowHandle;
    private readonly int _identifier;
    private bool _isRegistered;

    private GlobalHotKeyRegistration(
        IWindowsNativeApi nativeApi,
        nint windowHandle,
        int identifier)
    {
        _nativeApi = nativeApi;
        _windowHandle = windowHandle;
        _identifier = identifier;
        _isRegistered = true;
    }

    public static GlobalHotKeyRegistration Register(
        IWindowsNativeApi nativeApi,
        nint windowHandle,
        int identifier,
        HotKeyModifiers modifiers,
        uint virtualKey)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        if (identifier is < 0 or > 0xBFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(identifier));
        }

        ArgumentOutOfRangeException.ThrowIfZero(virtualKey);

        if (!nativeApi.RegisterHotKey(windowHandle, identifier, (uint)modifiers, virtualKey))
        {
            throw new Win32Exception(nativeApi.LastError, "Could not register the global hot key.");
        }

        return new GlobalHotKeyRegistration(nativeApi, windowHandle, identifier);
    }

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        _isRegistered = false;
        _ = _nativeApi.UnregisterHotKey(_windowHandle, _identifier);
    }
}
