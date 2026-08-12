using Velopack;
using Velopack.Sources;

namespace TuckClip.Windows.Services;

internal interface IWindowsUpdateSession
{
    string VersionText { get; }

    string? ReleaseNotes { get; }

    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);

    void ScheduleApplyAndRestart();
}

internal interface IWindowsUpdateClient
{
    bool IsInstalled { get; }

    Task<IWindowsUpdateSession?> CheckForUpdatesAsync();
}

internal sealed class VelopackUpdateClient : IWindowsUpdateClient
{
    internal static string RepositoryUrl { get; } = "https://github.com/iajihga/TuckClip";

    private readonly UpdateManager _manager;

    public VelopackUpdateClient()
        : this(new UpdateManager(new GithubSource(
            RepositoryUrl,
            accessToken: null,
            prerelease: false)))
    {
    }

    internal VelopackUpdateClient(UpdateManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    public bool IsInstalled => _manager.IsInstalled && !_manager.IsPortable;

    public async Task<IWindowsUpdateSession?> CheckForUpdatesAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }

        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null || !IsEligibleUpdate(
                update.IsDowngrade,
                update.TargetFullRelease.Version.IsPrerelease))
        {
            return null;
        }

        return new VelopackUpdateSession(_manager, update);
    }

    internal static bool IsEligibleUpdate(bool isDowngrade, bool targetIsPrerelease) =>
        !isDowngrade && !targetIsPrerelease;

    private sealed class VelopackUpdateSession : IWindowsUpdateSession
    {
        private readonly UpdateManager _manager;
        private readonly UpdateInfo _update;

        public VelopackUpdateSession(UpdateManager manager, UpdateInfo update)
        {
            _manager = manager;
            _update = update;
        }

        public string VersionText => _update.TargetFullRelease.Version.ToString();

        public string? ReleaseNotes => _update.TargetFullRelease.NotesMarkdown;

        public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken) =>
            _manager.DownloadUpdatesAsync(_update, progress, cancellationToken);

        public void ScheduleApplyAndRestart() => _manager.WaitExitThenApplyUpdates(
            _update.TargetFullRelease,
            silent: false,
            restart: true,
            restartArgs: []);
    }
}
