namespace TuckClip.Core.Tests;

[TestClass]
public sealed class ClipSearchTests
{
    [TestMethod]
    public void Filter_IsCaseInsensitive()
    {
        var item = TestItems.Text("Hello WORLD");
        CollectionAssert.AreEqual(new[] { item }, ClipSearch.Filter([item], "hello world").ToArray());
    }

    [TestMethod]
    public void Filter_TreatsDiacriticsAsEquivalent()
    {
        var item = TestItems.Text("Crème brûlée");
        CollectionAssert.AreEqual(new[] { item }, ClipSearch.Filter([item], "creme brulee").ToArray());
    }

    [TestMethod]
    public void Filter_TreatsFullWidthFormsAsEquivalent()
    {
        var item = TestItems.Text("ＴｕｃｋＣｌｉｐ １２３");
        CollectionAssert.AreEqual(new[] { item }, ClipSearch.Filter([item], "tuckclip 123").ToArray());
    }

    [TestMethod]
    public void Filter_RequiresEveryWhitespaceSeparatedToken()
    {
        var match = TestItems.Text("alpha beta gamma");
        var miss = TestItems.Text("alpha gamma");
        CollectionAssert.AreEqual(new[] { match }, ClipSearch.Filter([match, miss], "alpha beta").ToArray());
    }

    [TestMethod]
    public void Filter_SearchesFileAndSourceMetadata()
    {
        var file = TestItems.Files([@"C:\Users\Alice\Report.pdf"]);
        var source = TestItems.Text("payload", sourceAppName: "Visual Studio", sourceIdentifier: "devenv");

        CollectionAssert.AreEqual(new[] { file }, ClipSearch.Filter([source, file], "alice report").ToArray());
        CollectionAssert.AreEqual(new[] { source }, ClipSearch.Filter([file, source], "visual devenv").ToArray());
    }

    [TestMethod]
    public void Filter_EmptyQueryReturnsRecencyOrder()
    {
        var older = TestItems.Text("old", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = TestItems.Text("new", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        CollectionAssert.AreEqual(new[] { newer, older }, ClipSearch.Filter([older, newer], "  ").ToArray());
    }
}
