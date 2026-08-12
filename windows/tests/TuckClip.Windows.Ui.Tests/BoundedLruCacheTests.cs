using TuckClip.Windows.Controls;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class BoundedLruCacheTests
{
    [TestMethod]
    public void AddingOverCapacityEvictsLeastRecentlyUsedUnleasedValue()
    {
        var first = new TrackingResource();
        var second = new TrackingResource();
        var third = new TrackingResource();
        using var cache = new BoundedLruCache<string, TrackingResource>(2, 20);

        cache.AddOrAcquire("first", first, 10)!.Dispose();
        cache.AddOrAcquire("second", second, 10)!.Dispose();
        Assert.IsTrue(cache.TryAcquire("first", out var refreshedFirst));
        refreshedFirst!.Dispose();

        using var thirdLease = cache.AddOrAcquire("third", third, 10);

        Assert.IsFalse(first.IsDisposed);
        Assert.IsTrue(second.IsDisposed);
        Assert.IsFalse(third.IsDisposed);
        Assert.AreEqual(2, cache.Count);
        Assert.AreEqual(20, cache.TotalCost);
        Assert.IsFalse(cache.TryAcquire("second", out _));
    }

    [TestMethod]
    public void ActiveLeaseCannotBeEvictedAndOverflowValueIsRejected()
    {
        var retained = new TrackingResource();
        var overflow = new TrackingResource();
        using var cache = new BoundedLruCache<string, TrackingResource>(1, 10);
        using var retainedLease = cache.AddOrAcquire("retained", retained, 10);

        var overflowLease = cache.AddOrAcquire("overflow", overflow, 10);

        Assert.IsNull(overflowLease);
        Assert.AreEqual(1, cache.Count);
        Assert.IsFalse(cache.TryAcquire("overflow", out _));
        Assert.IsTrue(overflow.IsDisposed);
        Assert.IsFalse(retained.IsDisposed);
    }

    [TestMethod]
    public void CacheDisposalDefersLeasedValueUntilLeaseRelease()
    {
        var resource = new TrackingResource();
        var cache = new BoundedLruCache<string, TrackingResource>(1, 10);
        var lease = cache.AddOrAcquire("item", resource, 10)!;

        cache.Dispose();
        Assert.IsFalse(resource.IsDisposed);

        lease.Dispose();
        Assert.IsTrue(resource.IsDisposed);
    }

    private sealed class TrackingResource : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
