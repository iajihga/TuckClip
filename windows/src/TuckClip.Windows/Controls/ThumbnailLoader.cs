using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;

[assembly: InternalsVisibleTo("TuckClip.Windows.Ui.Tests")]

namespace TuckClip.Windows.Controls;

/// <summary>
/// Decodes encrypted-history image payloads entirely in memory and keeps a
/// bounded collection of decoded previews. Cache leases prevent an image from
/// being disposed while an attached card is rendering it.
/// </summary>
internal sealed class ThumbnailLoader : IDisposable
{
    internal const int DefaultDecodeWidth = 320;
    private const int DefaultMaximumEntryCount = 32;
    private const long DefaultMaximumDecodedBytes = 64L * 1024 * 1024;
    private const int DefaultMaximumConcurrentDecodes = 2;

    private readonly BoundedLruCache<ThumbnailCacheKey, ThumbnailResource> _cache;
    private readonly SemaphoreSlim _decodeGate;
    private readonly ThumbnailDecoder _decoder;
    private readonly int _decodeWidth;
    private bool _isDisposed;

    public ThumbnailLoader()
        : this(
            DefaultMaximumEntryCount,
            DefaultMaximumDecodedBytes,
            DefaultMaximumConcurrentDecodes,
            DefaultDecodeWidth,
            DecodeInMemory)
    {
    }

    internal ThumbnailLoader(
        int maximumEntryCount,
        long maximumDecodedBytes,
        int maximumConcurrentDecodes,
        int decodeWidth,
        ThumbnailDecoder decoder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConcurrentDecodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodeWidth);
        ArgumentNullException.ThrowIfNull(decoder);

        _cache = new BoundedLruCache<ThumbnailCacheKey, ThumbnailResource>(
            maximumEntryCount,
            maximumDecodedBytes);
        _decodeGate = new SemaphoreSlim(maximumConcurrentDecodes, maximumConcurrentDecodes);
        _decodeWidth = decodeWidth;
        _decoder = decoder;
    }

    internal async Task<BoundedLruCache<ThumbnailCacheKey, ThumbnailResource>.Lease?> AcquireAsync(
        Guid itemId,
        byte[] encodedImage,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(encodedImage);

        if (encodedImage.Length == 0)
        {
            return null;
        }

        var key = new ThumbnailCacheKey(itemId, _decodeWidth);
        if (_cache.TryAcquire(key, out var cachedLease))
        {
            return cachedLease;
        }

        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ThumbnailResource? decoded = null;
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_cache.TryAcquire(key, out cachedLease))
            {
                return cachedLease;
            }

            decoded = await _decoder(encodedImage, _decodeWidth, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded is null)
            {
                return null;
            }

            var lease = _cache.AddOrAcquire(key, decoded, decoded.DecodedByteCost);
            decoded = null;
            return lease;
        }
        finally
        {
            decoded?.Dispose();
            _decodeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cache.Dispose();
        _decodeGate.Dispose();
    }

    private static async ValueTask<ThumbnailResource?> DecodeInMemory(
        byte[] encodedImage,
        int decodeWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var stream = new MemoryStream(encodedImage, writable: false);
                        return Bitmap.DecodeToWidth(
                            stream,
                            decodeWidth,
                            BitmapInterpolationMode.HighQuality);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            var decodedByteCost = checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4L);
            return new ThumbnailResource(bitmap, decodedByteCost);
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException)
        {
            return null;
        }
    }
}

internal delegate ValueTask<ThumbnailResource?> ThumbnailDecoder(
    byte[] encodedImage,
    int decodeWidth,
    CancellationToken cancellationToken);

internal readonly record struct ThumbnailCacheKey(Guid ItemId, int DecodeWidth);

internal sealed class ThumbnailResource : IDisposable
{
    private IDisposable? _disposableImage;

    internal ThumbnailResource(IImage image, long decodedByteCost)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decodedByteCost);

        Image = image;
        DecodedByteCost = decodedByteCost;
        _disposableImage = image as IDisposable;
    }

    internal IImage Image { get; }

    internal long DecodedByteCost { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposableImage, null)?.Dispose();
    }
}

/// <summary>
/// Strictly bounded LRU cache. Entries with active leases cannot be evicted;
/// when all room is leased, a new value is returned as an uncached lease and
/// is disposed as soon as that lease is released.
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> : IDisposable
    where TKey : notnull
    where TValue : class, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly LinkedList<TKey> _leastRecentlyUsed = [];
    private readonly int _maximumEntryCount;
    private readonly long _maximumCost;
    private long _totalCost;
    private bool _isDisposed;

    internal BoundedLruCache(int maximumEntryCount, long maximumCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCost);

        _maximumEntryCount = maximumEntryCount;
        _maximumCost = maximumCost;
    }

    internal int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    internal long TotalCost
    {
        get
        {
            lock (_sync)
            {
                return _totalCost;
            }
        }
    }

    internal bool TryAcquire(TKey key, out Lease? lease)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_entries.TryGetValue(key, out var entry))
            {
                lease = null;
                return false;
            }

            entry.ReferenceCount++;
            Touch(entry);
            lease = new Lease(entry.Value, () => Release(entry));
            return true;
        }
    }

    internal Lease? AddOrAcquire(TKey key, TValue value, long cost)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cost);

        List<TValue>? valuesToDispose = null;
        Lease? lease;
        var wasDisposed = false;
        lock (_sync)
        {
            if (_isDisposed)
            {
                valuesToDispose = [value];
                lease = null;
                wasDisposed = true;
            }
            else if (_entries.TryGetValue(key, out var existing))
            {
                existing.ReferenceCount++;
                Touch(existing);
                valuesToDispose = [value];
                lease = new Lease(existing.Value, () => Release(existing));
            }
            else
            {
                if (cost <= _maximumCost)
                {
                    valuesToDispose = EvictUntilFits(cost);
                }

                if (cost > _maximumCost
                    || _entries.Count >= _maximumEntryCount
                    || _totalCost > _maximumCost - cost)
                {
                    (valuesToDispose ??= []).Add(value);
                    lease = null;
                }
                else
                {
                    var node = _leastRecentlyUsed.AddFirst(key);
                    var entry = new Entry(key, value, cost, node)
                    {
                        ReferenceCount = 1,
                    };
                    _entries.Add(key, entry);
                    _totalCost += cost;
                    lease = new Lease(value, () => Release(entry));
                }
            }
        }

        DisposeAll(valuesToDispose);
        ObjectDisposedException.ThrowIf(wasDisposed, this);

        return lease;
    }

    public void Dispose()
    {
        List<TValue>? valuesToDispose = null;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.IsRemoved = true;
                if (entry.ReferenceCount == 0)
                {
                    (valuesToDispose ??= []).Add(entry.Value);
                }
            }

            _entries.Clear();
            _leastRecentlyUsed.Clear();
            _totalCost = 0;
        }

        DisposeAll(valuesToDispose);
    }

    private List<TValue>? EvictUntilFits(long incomingCost)
    {
        List<TValue>? valuesToDispose = null;
        while ((_entries.Count >= _maximumEntryCount || _totalCost > _maximumCost - incomingCost)
            && TryFindEvictionCandidate(out var candidate))
        {
            Remove(candidate!);
            (valuesToDispose ??= []).Add(candidate!.Value);
        }

        return valuesToDispose;
    }

    private bool TryFindEvictionCandidate(out Entry? candidate)
    {
        var node = _leastRecentlyUsed.Last;
        while (node is not null)
        {
            var entry = _entries[node.Value];
            if (entry.ReferenceCount == 0)
            {
                candidate = entry;
                return true;
            }

            node = node.Previous;
        }

        candidate = null;
        return false;
    }

    private void Remove(Entry entry)
    {
        _entries.Remove(entry.Key);
        _leastRecentlyUsed.Remove(entry.Node);
        _totalCost -= entry.Cost;
        entry.IsRemoved = true;
    }

    private void Release(Entry entry)
    {
        TValue? valueToDispose = null;
        lock (_sync)
        {
            if (entry.ReferenceCount <= 0)
            {
                return;
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 && entry.IsRemoved)
            {
                valueToDispose = entry.Value;
            }
        }

        valueToDispose?.Dispose();
    }

    private void Touch(Entry entry)
    {
        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddFirst(entry.Node);
    }

    private static void DisposeAll(List<TValue>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            value.Dispose();
        }
    }

    private sealed class Entry(
        TKey key,
        TValue value,
        long cost,
        LinkedListNode<TKey> node)
    {
        internal TKey Key { get; } = key;

        internal TValue Value { get; } = value;

        internal long Cost { get; } = cost;

        internal LinkedListNode<TKey> Node { get; } = node;

        internal int ReferenceCount { get; set; }

        internal bool IsRemoved { get; set; }
    }

    internal sealed class Lease : IDisposable
    {
        private Action? _release;

        internal Lease(TValue value, Action release)
        {
            Value = value;
            _release = release;
        }

        internal TValue Value { get; }

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
