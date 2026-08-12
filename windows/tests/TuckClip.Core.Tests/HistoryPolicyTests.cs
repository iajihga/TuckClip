namespace TuckClip.Core.Tests;

[TestClass]
public sealed class HistoryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Upsert_DuplicatePreservesIdentityAndPinAndIncrementsCopyCount()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var existing = TestItems.Text("same", Now.AddHours(-1), id, pinned: true, copyCount: 3);
        var captured = TestItems.Text("same", Now, Guid.NewGuid(), sourceAppName: "Notepad");

        var result = HistoryPolicy.Upsert([existing], captured, new AppSettings(), Now);

        Assert.HasCount(1, result);
        Assert.AreEqual(id, result[0].Id);
        Assert.IsTrue(result[0].IsPinned);
        Assert.AreEqual(4, result[0].CopyCount);
        Assert.AreEqual("Notepad", result[0].SourceAppName);
        Assert.AreEqual(Now, result[0].UpdatedAt);
    }

    [TestMethod]
    public void Upsert_SaturatesCopyCount()
    {
        var existing = TestItems.Text("same", Now.AddHours(-1), copyCount: int.MaxValue);
        var captured = TestItems.Text("same", Now);
        Assert.AreEqual(
            int.MaxValue,
            HistoryPolicy.Upsert([existing], captured, new AppSettings(), Now)[0].CopyCount);
    }

    [TestMethod]
    public void Prune_NeverRemovesPinnedItemsWhenAboveMaximum()
    {
        var items = Enumerable.Range(0, 4)
            .Select(index => TestItems.Text($"pin-{index}", Now.AddMinutes(-index), pinned: true))
            .Concat([TestItems.Text("regular", Now)])
            .ToArray();

        var result = HistoryPolicy.Prune(items, new AppSettings { MaximumItemCount = 2 }, Now);

        Assert.HasCount(4, result);
        Assert.IsTrue(result.All(item => item.IsPinned));
    }

    [TestMethod]
    public void Prune_UsesRemainingCapacityForNewestUnpinnedItems()
    {
        var pin = TestItems.Text("pin", Now.AddDays(-10), pinned: true);
        var old = TestItems.Text("old", Now.AddMinutes(-2));
        var newest = TestItems.Text("new", Now);

        var result = HistoryPolicy.Prune(
            [pin, old, newest],
            new AppSettings { MaximumItemCount = 2, RetentionDays = 0 },
            Now);

        CollectionAssert.AreEquivalent(new[] { pin.Id, newest.Id }, result.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void Prune_RetentionNeverRemovesPinnedItems()
    {
        var pin = TestItems.Text("pin", Now.AddDays(-100), pinned: true);
        var old = TestItems.Text("old", Now.AddDays(-31));
        var fresh = TestItems.Text("fresh", Now.AddDays(-1));

        var result = HistoryPolicy.Prune(
            [pin, old, fresh],
            new AppSettings { RetentionDays = 30 },
            Now);

        CollectionAssert.AreEquivalent(new[] { pin.Id, fresh.Id }, result.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void Prune_ZeroRetentionDisablesAgeLimit()
    {
        var ancient = TestItems.Text("ancient", Now.AddYears(-20));
        Assert.HasCount(
            1,
            HistoryPolicy.Prune([ancient], new AppSettings { RetentionDays = 0 }, Now));
    }

    [TestMethod]
    public void Prune_DeduplicatesAndPreservesAnyPin()
    {
        var older = TestItems.Text("duplicate", Now.AddMinutes(-1), pinned: true, copyCount: 2);
        var newer = TestItems.Text("duplicate", Now, copyCount: 4);

        var result = HistoryPolicy.Prune([older, newer], new AppSettings(), Now);

        Assert.HasCount(1, result);
        Assert.IsTrue(result[0].IsPinned);
        Assert.AreEqual(6, result[0].CopyCount);
        Assert.AreEqual(newer.Id, result[0].Id);
        Assert.AreEqual(older.CreatedAt, result[0].CreatedAt);
    }

    [TestMethod]
    public void Sort_UsesUpdatedThenCreatedThenStableId()
    {
        var sameUpdated = Now;
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = TestItems.Text("first", sameUpdated, firstId);
        var second = TestItems.Text("second", sameUpdated, secondId);
        var newestCreated = TestItems.Text("newest-created", sameUpdated.AddMinutes(1)) with { UpdatedAt = sameUpdated };

        CollectionAssert.AreEqual(
            new[] { newestCreated, first, second },
            HistoryPolicy.Sort([second, newestCreated, first]).ToArray());
    }
}
