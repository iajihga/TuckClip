using TuckClip.Core.Persistence;

namespace TuckClip.Core;

public enum HistoryMutationStatus
{
    Saved,
    Ignored,
    ReadOnly,
    PersistenceFailed,
    NotFound,
}

public sealed record HistoryMutationResult(HistoryMutationStatus Status, CaptureRejectionReason? RejectionReason = null)
{
    public bool IsSuccess => Status == HistoryMutationStatus.Saved;
}

public sealed class HistoryStore : IDisposable
{
    private readonly IHistoryRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ClipItem> _items = Array.AsReadOnly(Array.Empty<ClipItem>());
    private AppSettings _settings;
    private bool _initialized;
    private bool _disposed;

    public HistoryStore(IHistoryRepository repository, AppSettings? settings = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = (settings ?? new AppSettings()).Validate();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ClipItem> Items => _items;

    public AppSettings Settings => _settings;

    public bool IsReadOnly { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                _items = Array.AsReadOnly(
                    HistoryPolicy.Sort(await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)).ToArray());
            }
            catch (HistoryCorruptedException)
            {
                _items = Array.AsReadOnly(Array.Empty<ClipItem>());
                IsReadOnly = true;
            }

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryMutationResult> CaptureAsync(
        ClipboardCapture capture,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (IsReadOnly)
            {
                return new HistoryMutationResult(HistoryMutationStatus.ReadOnly);
            }

            var decision = CapturePolicy.Normalize(capture, _settings);
            if (!decision.IsAccepted)
            {
                return new HistoryMutationResult(HistoryMutationStatus.Ignored, decision.RejectionReason);
            }

            var item = ClipItemFactory.Create(decision);
            var candidate = HistoryPolicy.Upsert(_items, item, _settings, capture.CapturedAt);
            return await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<HistoryMutationResult> SetPinnedAsync(
        Guid id,
        bool isPinned,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return MutateAsync(
            current =>
            {
                var found = false;
                var changed = current.Select(item =>
                {
                    if (item.Id != id)
                    {
                        return item;
                    }

                    found = true;
                    return item with { IsPinned = isPinned };
                }).ToArray();
                return found ? HistoryPolicy.Prune(changed, _settings, now) : null;
            },
            cancellationToken);
    }

    public Task<HistoryMutationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return MutateAsync(
            current => current.Any(item => item.Id == id)
                ? current.Where(item => item.Id != id).ToArray()
                : null,
            cancellationToken);
    }

    public Task<HistoryMutationResult> ClearUnpinnedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return MutateAsync(current => current.Where(item => item.IsPinned).ToArray(), cancellationToken);
    }

    public Task<HistoryMutationResult> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return MutateAsync(static _ => Array.Empty<ClipItem>(), cancellationToken);
    }

    public async Task<HistoryMutationResult> UpdateSettingsAsync(
        AppSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (IsReadOnly)
            {
                return new HistoryMutationResult(HistoryMutationStatus.ReadOnly);
            }

            var candidate = HistoryPolicy.Prune(_items, settings, now);
            return await SaveAndPublishAsync(
                candidate,
                cancellationToken,
                () => _settings = settings).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies capture and retention settings without rewriting or pruning the
    /// existing history. The policy is enforced the next time a successful
    /// history mutation is committed.
    /// </summary>
    public async Task ApplySettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            _settings = settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<ClipItem> Search(string? query)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        return ClipSearch.Filter(_items, query);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private async Task<HistoryMutationResult> MutateAsync(
        Func<IReadOnlyList<ClipItem>, IReadOnlyList<ClipItem>?> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (IsReadOnly)
            {
                return new HistoryMutationResult(HistoryMutationStatus.ReadOnly);
            }

            var candidate = mutation(_items);
            if (candidate is null)
            {
                return new HistoryMutationResult(HistoryMutationStatus.NotFound);
            }

            return await SaveAndPublishAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(IReadOnlyList<ClipItem> items)
    {
        _items = Array.AsReadOnly(items.ToArray());
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A UI observer must not turn a durable commit into a reported
                // mutation failure or prevent other observers from refreshing.
            }
        }
    }

    private async Task<HistoryMutationResult> SaveAndPublishAsync(
        IReadOnlyList<ClipItem> candidate,
        CancellationToken cancellationToken,
        Action? beforePublish = null)
    {
        try
        {
            await _repository.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new HistoryMutationResult(HistoryMutationStatus.PersistenceFailed);
        }

        beforePublish?.Invoke();
        Publish(candidate);
        return new HistoryMutationResult(HistoryMutationStatus.Saved);
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The history store must be initialized before use.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
