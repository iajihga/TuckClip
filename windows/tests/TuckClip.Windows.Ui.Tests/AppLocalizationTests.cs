using TuckClip.Windows.Services;
using TuckClip.Windows.ViewModels;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class AppLocalizationTests
{
    [TestCleanup]
    public void RestoreSystemLanguage() => AppLocalization.Apply(AppLanguage.System);

    [TestMethod]
    public void ExplicitLanguagesTranslateAndFallBackSafely()
    {
        AppLocalization.Apply(AppLanguage.English);
        Assert.AreEqual("Settings", AppLocalization.Text("设置"));
        Assert.AreEqual("7 items", AppLocalization.Format("{0} 项", 7));
        Assert.AreEqual("未收录文案", AppLocalization.Text("未收录文案"));

        AppLocalization.Apply(AppLanguage.SimplifiedChinese);
        Assert.AreEqual("设置", AppLocalization.Text("设置"));
        Assert.AreEqual("7 项", AppLocalization.Format("{0} 项", 7));
    }

    [TestMethod]
    public void RefreshLocalizationRecomputesCaptureStatusAndLanguageOptions()
    {
        AppLocalization.Apply(AppLanguage.SimplifiedChinese);
        using var panel = new ClipboardPanelViewModel();
        using var card = new ClipCardViewModel(
            Guid.NewGuid(),
            ClipDisplayKind.Text,
            "",
            "",
            "",
            "",
            null,
            DateTimeOffset.Now,
            false);
        panel.UpdateCaptureState(new ClipboardCaptureState(CaptureStateKind.Paused));
        var settings = new ClipboardSettingsViewModel();
        settings.ApplySnapshot(new ClipboardSettingsSnapshot(
            true,
            true,
            true,
            30,
            500,
            [],
            @"C:\TuckClip",
            AppLanguage: AppLanguage.SimplifiedChinese));
        Assert.AreEqual("已暂停", panel.StatusText);
        Assert.AreEqual("未知应用", card.SourceName);

        AppLocalization.Apply(AppLanguage.English);
        panel.RefreshLocalization();
        card.RefreshLocalization();
        settings.RefreshLocalization(AppLanguage.SimplifiedChinese);

        Assert.AreEqual("Paused", panel.StatusText);
        Assert.AreEqual("Unknown App", card.SourceName);
        Assert.AreEqual(
            "Simplified Chinese",
            settings.LanguageOptions.Single(
                option => option.Value == AppLanguage.SimplifiedChinese).DisplayName);
        Assert.AreEqual(AppLanguage.SimplifiedChinese, settings.SelectedLanguageOption?.Value);
    }
}
