using Avalonia;

namespace TuckClip.Windows;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstance = SingleInstanceGuard.TryAcquire();
        if (!singleInstance.IsPrimary)
        {
            SingleInstanceGuard.SignalPrimaryInstance();
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
