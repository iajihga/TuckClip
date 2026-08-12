using Avalonia;
using Avalonia.Media;
using TuckClip.Windows.Controls;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class ThumbnailLoaderTests
{
    [TestMethod]
    public async Task ReusesDecodedImageForTheSameClipboardItem()
    {
        var decodeCount = 0;
        var image = new TrackingImage();
        using var loader = new ThumbnailLoader(
            maximumEntryCount: 2,
            maximumDecodedBytes: 100,
            maximumConcurrentDecodes: 1,
            decodeWidth: 64,
            (_, _, _) =>
            {
                decodeCount++;
                return ValueTask.FromResult<ThumbnailResource?>(new ThumbnailResource(image, 10));
            });
        var itemId = Guid.NewGuid();

        using var firstLease = await loader.AcquireAsync(itemId, [1, 2, 3], CancellationToken.None);
        firstLease!.Dispose();
        using var secondLease = await loader.AcquireAsync(itemId, [1, 2, 3], CancellationToken.None);

        Assert.AreEqual(1, decodeCount);
        Assert.AreSame(image, secondLease!.Value.Image);
        Assert.IsFalse(image.IsDisposed);
    }

    [TestMethod]
    public async Task CancellationAfterDecodeDisposesLateResult()
    {
        var completion = new TaskCompletionSource<ThumbnailResource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var loader = new ThumbnailLoader(
            maximumEntryCount: 2,
            maximumDecodedBytes: 100,
            maximumConcurrentDecodes: 1,
            decodeWidth: 64,
            (_, _, _) => new ValueTask<ThumbnailResource?>(completion.Task));
        using var cancellation = new CancellationTokenSource();
        var acquireTask = loader.AcquireAsync(Guid.NewGuid(), [1, 2, 3], cancellation.Token);
        var lateImage = new TrackingImage();

        cancellation.Cancel();
        completion.SetResult(new ThumbnailResource(lateImage, 10));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await acquireTask);
        Assert.IsTrue(lateImage.IsDisposed);
    }

    private sealed class TrackingImage : IImage, IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public Size Size => new(1, 1);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }

        public void Dispose() => IsDisposed = true;
    }
}
