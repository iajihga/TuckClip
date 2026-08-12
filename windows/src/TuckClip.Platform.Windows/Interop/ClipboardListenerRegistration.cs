using System.ComponentModel;

namespace TuckClip.Platform.Windows.Interop;

public sealed class ClipboardListenerRegistration : IDisposable
{
    private readonly IWindowsNativeApi _nativeApi;
    private readonly nint _windowHandle;
    private bool _isRegistered;

    private ClipboardListenerRegistration(IWindowsNativeApi nativeApi, nint windowHandle)
    {
        _nativeApi = nativeApi;
        _windowHandle = windowHandle;
        _isRegistered = true;
    }

    public static ClipboardListenerRegistration Register(
        IWindowsNativeApi nativeApi,
        nint windowHandle)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        if (windowHandle == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowHandle), "A real message-window handle is required.");
        }

        if (!nativeApi.AddClipboardFormatListener(windowHandle))
        {
            throw new Win32Exception(nativeApi.LastError, "Could not register the clipboard listener.");
        }

        return new ClipboardListenerRegistration(nativeApi, windowHandle);
    }

    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }

        _isRegistered = false;
        _ = _nativeApi.RemoveClipboardFormatListener(_windowHandle);
    }
}
