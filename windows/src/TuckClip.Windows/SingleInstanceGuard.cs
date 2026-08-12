using System.Runtime.InteropServices;

namespace TuckClip.Windows;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "Local\\io.github.iajihga.TuckClip";
    internal const string ShowMessageName = "io.github.iajihga.TuckClip.ShowPanel";
    private const uint HwndBroadcast = 0xffff;

    private readonly Mutex? _mutex;

    private SingleInstanceGuard(Mutex? mutex, bool isPrimary)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceGuard TryAcquire()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SingleInstanceGuard(null, true);
        }

        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public static void SignalPrimaryInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var message = RegisterWindowMessage(ShowMessageName);
        if (message != 0)
        {
            // The primary process owns the mutex before Avalonia creates its
            // message window. A short bounded retry closes that startup gap;
            // ShowRequested is idempotent, so duplicate broadcasts are safe.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                _ = PostMessage((nint)HwndBroadcast, message, nint.Zero, nint.Zero);
                if (attempt < 7)
                {
                    Thread.Sleep(millisecondsTimeout: 75);
                }
            }
        }
    }

    internal static uint GetShowMessageId() =>
        OperatingSystem.IsWindows() ? RegisterWindowMessage(ShowMessageName) : 0;

    public void Dispose()
    {
        if (IsPrimary)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already terminating; there is no recovery work.
            }
        }

        _mutex?.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
