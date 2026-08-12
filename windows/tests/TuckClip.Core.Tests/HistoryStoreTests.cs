using TuckClip.Core.Persistence;

namespace TuckClip.Core.Tests;

[TestClass]
public sealed class HistoryStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Initialize_CorruptHistoryEntersReadOnlyWithoutSaving()
    {
        var repository = new FakeHistoryRepository
        {
            LoadException = new HistoryCorruptedException("damaged"),
        };
        using var store = new HistoryStore(repository);

        await store.InitializeAsync();
        var result = await store.CaptureAsync(new ClipboardCapture { PlainText = "do not save" });

        Assert.IsTrue(store.IsReadOnly);
        Assert.IsEmpty(store.Items);
        Assert.AreEqual(HistoryMutationStatus.ReadOnly, result.Status);
        Assert.AreEqual(0, repository.SaveCount);
    }

    [TestMethod]
    public async Task Capture_PersistenceFailureDoesNotPublishCandidateOrEvent()
    {
        var original = TestItems.Text("original", Now.AddMinutes(-1));
        var repository = new FakeHistoryRepository
        {
            LoadedItems = [original],
            SaveException = new IOException("disk full"),
        };
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();
        var eventCount = 0;
        store.Changed += (_, _) => eventCount++;

        var result = await store.CaptureAsync(
            new ClipboardCapture { PlainText = "new", CapturedAt = Now });

        Assert.AreEqual(HistoryMutationStatus.PersistenceFailed, result.Status);
        CollectionAssert.AreEqual(new[] { original }, store.Items.ToArray());
        Assert.AreEqual(0, eventCount);
    }

    [TestMethod]
    public async Task Capture_SuccessPublishesOnlyAfterRepositorySave()
    {
        var repository = new FakeHistoryRepository();
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();
        var eventCount = 0;
        store.Changed += (_, _) => eventCount++;

        var result = await store.CaptureAsync(
            new ClipboardCapture { PlainText = "saved", CapturedAt = Now });

        Assert.AreEqual(HistoryMutationStatus.Saved, result.Status);
        Assert.HasCount(1, store.Items);
        Assert.HasCount(1, repository.LastSavedItems!);
        Assert.AreEqual(1, eventCount);
    }

    [TestMethod]
    public async Task Capture_ObserverFailureDoesNotChangeSuccessfulCommitResult()
    {
        var repository = new FakeHistoryRepository();
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();
        var secondObserverCalled = false;
        store.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        store.Changed += (_, _) => secondObserverCalled = true;

        var result = await store.CaptureAsync(new ClipboardCapture { PlainText = "saved", CapturedAt = Now });

        Assert.AreEqual(HistoryMutationStatus.Saved, result.Status);
        Assert.IsTrue(secondObserverCalled);
        Assert.HasCount(1, store.Items);
    }

    [TestMethod]
    public async Task Capture_FilteredContentNeverCallsRepositorySave()
    {
        var repository = new FakeHistoryRepository();
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();

        var result = await store.CaptureAsync(
            new ClipboardCapture { PlainText = "-----BEGIN PRIVATE KEY-----\nsecret" });

        Assert.AreEqual(HistoryMutationStatus.Ignored, result.Status);
        Assert.AreEqual(CaptureRejectionReason.HighConfidencePrivateKey, result.RejectionReason);
        Assert.AreEqual(0, repository.SaveCount);
    }

    [TestMethod]
    public async Task SetPinned_UnpinImmediatelyReappliesMaximumCount()
    {
        var pinned = TestItems.Text("pinned", Now.AddMinutes(-2), pinned: true);
        var newest = TestItems.Text("newest", Now);
        var repository = new FakeHistoryRepository { LoadedItems = [newest, pinned] };
        using var store = new HistoryStore(
            repository,
            new AppSettings { MaximumItemCount = 1, RetentionDays = 0 });
        await store.InitializeAsync();

        var result = await store.SetPinnedAsync(pinned.Id, false, Now);

        Assert.AreEqual(HistoryMutationStatus.Saved, result.Status);
        CollectionAssert.AreEqual(new[] { newest.Id }, store.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task SetPinned_MissingItemDoesNotSave()
    {
        var repository = new FakeHistoryRepository();
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();

        var result = await store.SetPinnedAsync(Guid.NewGuid(), true, Now);

        Assert.AreEqual(HistoryMutationStatus.NotFound, result.Status);
        Assert.AreEqual(0, repository.SaveCount);
    }

    [TestMethod]
    public async Task UpdateSettings_FailureKeepsBothItemsAndOldSettings()
    {
        var first = TestItems.Text("first", Now);
        var second = TestItems.Text("second", Now.AddMinutes(-1));
        var repository = new FakeHistoryRepository
        {
            LoadedItems = [first, second],
            SaveException = new IOException("disk full"),
        };
        var oldSettings = new AppSettings { MaximumItemCount = 10 };
        using var store = new HistoryStore(repository, oldSettings);
        await store.InitializeAsync();

        var result = await store.UpdateSettingsAsync(
            new AppSettings { MaximumItemCount = 1 },
            Now);

        Assert.AreEqual(HistoryMutationStatus.PersistenceFailed, result.Status);
        Assert.AreSame(oldSettings, store.Settings);
        Assert.HasCount(2, store.Items);
    }

    [TestMethod]
    public async Task ApplySettingsDefersPruningUntilNextSuccessfulMutation()
    {
        var first = TestItems.Text("first", Now);
        var second = TestItems.Text("second", Now.AddMinutes(-1));
        var repository = new FakeHistoryRepository { LoadedItems = [first, second] };
        using var store = new HistoryStore(
            repository,
            new AppSettings { MaximumItemCount = 10, RetentionDays = 0 });
        await store.InitializeAsync();
        var restrictiveSettings = new AppSettings { MaximumItemCount = 1, RetentionDays = 0 };

        await store.ApplySettingsAsync(restrictiveSettings);

        Assert.AreEqual(restrictiveSettings, store.Settings);
        Assert.HasCount(2, store.Items);
        Assert.AreEqual(0, repository.SaveCount);

        var result = await store.CaptureAsync(
            new ClipboardCapture { PlainText = "third", CapturedAt = Now.AddMinutes(1) });

        Assert.AreEqual(HistoryMutationStatus.Saved, result.Status);
        Assert.HasCount(1, store.Items);
        Assert.AreEqual("third", store.Items[0].PlainText);
        Assert.AreEqual(1, repository.SaveCount);
    }

    [TestMethod]
    public async Task ClearUnpinned_PreservesPins()
    {
        var pin = TestItems.Text("pin", Now, pinned: true);
        var regular = TestItems.Text("regular", Now);
        var repository = new FakeHistoryRepository { LoadedItems = [pin, regular] };
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();

        await store.ClearUnpinnedAsync();

        CollectionAssert.AreEqual(new[] { pin.Id }, store.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task ClearAll_RemovesPinnedAndUnpinnedItems()
    {
        var pin = TestItems.Text("pin", Now, pinned: true);
        var regular = TestItems.Text("regular", Now);
        var repository = new FakeHistoryRepository { LoadedItems = [pin, regular] };
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();

        var result = await store.ClearAllAsync();

        Assert.AreEqual(HistoryMutationStatus.Saved, result.Status);
        Assert.IsEmpty(store.Items);
        Assert.IsEmpty(repository.LastSavedItems!);
    }

    [TestMethod]
    public async Task Search_RequiresInitialization()
    {
        using var store = new HistoryStore(new FakeHistoryRepository());
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Search("anything"));
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task Capture_HonorsCancellationWithoutPublishing()
    {
        var repository = new FakeHistoryRepository();
        using var store = new HistoryStore(repository);
        await store.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.CaptureAsync(new ClipboardCapture { PlainText = "cancelled" }, cancellation.Token));
        Assert.IsEmpty(store.Items);
    }
}
