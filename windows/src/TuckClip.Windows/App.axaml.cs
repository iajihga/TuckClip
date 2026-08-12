using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TuckClip.Windows.Services;

namespace TuckClip.Windows;

public sealed partial class App : Application, IDisposable
{
    private AppCoordinator? _coordinator;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Task? _startupTask;
    private Task? _shutdownTask;
    private int _exitCode;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppLocalization.Apply(AppLanguage.System);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _coordinator = new AppCoordinator(() => BeginShutdown(exitCode: 0));
            // Do not intercept ShutdownRequested: on Windows it also carries
            // OS logoff/shutdown, and cancelling it can block session end.
            // Every in-app quit path calls BeginShutdown directly instead.
            desktop.Exit += OnDesktopExit;
            Dispatcher.UIThread.Post(BeginStartup);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void BeginStartup()
    {
        if (_shutdownTask is not null)
        {
            return;
        }

        _startupTask = StartCoordinatorAsync();
    }

    private async Task StartCoordinatorAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        try
        {
            await _coordinator.StartAsync();
        }
        catch (OperationCanceledException) when (_shutdownTask is not null)
        {
            // A requested shutdown canceled initialization.
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("TuckClip failed to start: {0}", exception);
            BeginShutdown(exitCode: 1);
        }
    }

    private void BeginShutdown(int exitCode)
    {
        _exitCode = Math.Max(_exitCode, exitCode);
        _shutdownTask ??= ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            if (_coordinator is not null)
            {
                await _coordinator.StopAsync();
            }
        }
        catch (Exception exception)
        {
            _exitCode = Math.Max(_exitCode, 1);
            System.Diagnostics.Trace.TraceError("TuckClip shutdown failed: {0}", exception);
        }
        finally
        {
            _desktop?.Shutdown(_exitCode);
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_desktop is not null)
        {
            _desktop.Exit -= OnDesktopExit;
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_startupTask?.IsFaulted == true)
        {
            _ = _startupTask.Exception;
        }

        _coordinator?.Dispose();
        _coordinator = null;
        _desktop = null;
        GC.SuppressFinalize(this);
    }
}
