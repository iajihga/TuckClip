using TuckClip.Windows.ViewModels;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class ClipboardPanelViewModelTests
{
    [TestMethod]
    public void MatchesRequiresEveryWhitespaceTokenAcrossAnyField()
    {
        var item = CreateItem(
            title: "Résumé de projet",
            searchableContent: "版本计划",
            sourceName: "Visual Studio Code");

        Assert.IsTrue(item.Matches("resume studio"));
        Assert.IsTrue(item.Matches("ＲＥＳＵＭＥ 版本"));
        Assert.IsFalse(item.Matches("resume finder"));
    }

    [TestMethod]
    public void SearchAndTypeFiltersRepairSelectionAndShortcutIndexes()
    {
        var actions = new RecordingUiActions();
        using var viewModel = new ClipboardPanelViewModel(actions);
        var text = CreateItem(title: "alpha note", kind: ClipDisplayKind.Text);
        var link = CreateItem(title: "alpha docs", kind: ClipDisplayKind.Link);
        var image = CreateItem(title: "screenshot", kind: ClipDisplayKind.Image);

        viewModel.ReplaceItems([text, link, image]);
        viewModel.SearchText = "alpha";

        CollectionAssert.AreEqual(new[] { text, link }, viewModel.FilteredItems.ToArray());
        Assert.AreSame(text, viewModel.SelectedItem);
        Assert.AreEqual(1, text.ShortcutIndex);
        Assert.AreEqual(2, link.ShortcutIndex);

        viewModel.SelectFilter(ClipTypeFilter.Link);

        CollectionAssert.AreEqual(new[] { link }, viewModel.FilteredItems.ToArray());
        Assert.AreSame(link, viewModel.SelectedItem);
        Assert.IsNull(text.ShortcutIndex);
        Assert.AreEqual(1, link.ShortcutIndex);
    }

    [TestMethod]
    public void NavigationDoesNotPasteButActivationDoes()
    {
        var actions = new RecordingUiActions();
        using var viewModel = new ClipboardPanelViewModel(actions);
        var first = CreateItem(title: "first");
        var second = CreateItem(title: "second");
        viewModel.ReplaceItems([first, second]);

        viewModel.MoveSelection(1);
        Assert.AreSame(second, viewModel.SelectedItem);
        Assert.HasCount(0, actions.Pastes);

        viewModel.ActivateVisibleItem(1);
        CollectionAssert.AreEqual(new[] { (first.Id, false) }, actions.Pastes);

        viewModel.SelectedItem = second;
        viewModel.PasteSelected(asPlainText: true);
        Assert.AreEqual((second.Id, true), actions.Pastes[^1]);
    }

    [TestMethod]
    public void PlainTextPasteIsIgnoredForImages()
    {
        var actions = new RecordingUiActions();
        using var viewModel = new ClipboardPanelViewModel(actions);
        viewModel.ReplaceItems([CreateItem(title: "image", kind: ClipDisplayKind.Image)]);

        viewModel.PasteSelected(asPlainText: true);

        Assert.HasCount(0, actions.Pastes);
    }

    [TestMethod]
    public void ReplacingItemsRevokesRetiredThumbnailWithoutMutatingBorrowedData()
    {
        byte[] thumbnailData = [1, 2, 3, 4];
        using var viewModel = new ClipboardPanelViewModel(new RecordingUiActions());
        var image = CreateItem("image", ClipDisplayKind.Image, encodedThumbnailData: thumbnailData);
        viewModel.ReplaceItems([image]);

        Assert.IsTrue(image.TryGetEncodedThumbnailData(out var borrowedData));
        Assert.AreSame(thumbnailData, borrowedData);

        viewModel.ReplaceItems([]);

        Assert.IsFalse(image.TryGetEncodedThumbnailData(out _));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, thumbnailData);
    }

    private static ClipCardViewModel CreateItem(
        string title,
        ClipDisplayKind kind = ClipDisplayKind.Text,
        string searchableContent = "",
        string sourceName = "测试应用",
        byte[]? encodedThumbnailData = null) =>
        new(
            Guid.NewGuid(),
            kind,
            title,
            detail: string.Empty,
            searchableContent,
            sourceName,
            sourceIdentifier: "test.exe",
            DateTimeOffset.Now,
            isPinned: false,
            encodedThumbnailData);
}
