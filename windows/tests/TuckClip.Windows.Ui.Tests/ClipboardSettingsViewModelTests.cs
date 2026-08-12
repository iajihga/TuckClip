using TuckClip.Platform.Windows.Interop;
using TuckClip.Windows.Services;
using TuckClip.Windows.ViewModels;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class ClipboardSettingsViewModelTests
{
    private static readonly string[] ExpectedExcludedProcessNames =
        ["Alpha.exe", "beta.exe", "zeta.exe"];
    private static readonly string[] ExpectedCommittedExcludedProcessNames = ["KeePassXC.exe"];

    [TestMethod]
    public void ApplyingSnapshotDoesNotWriteSettingsBack()
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);
        var snapshot = new ClipboardSettingsSnapshot(
            RecordingEnabled: false,
            AutomaticPasteEnabled: false,
            CapturesImages: false,
            RetentionDays: 90,
            MaximumItemCount: 5_000,
            ExcludedProcessNames: ["KeePassXC.exe", "1Password.exe"],
            DataDirectory: @"C:\Users\tester\AppData\Local\TuckClip");

        viewModel.ApplySnapshot(snapshot);

        Assert.HasCount(0, actions.SettingsDrafts);
        Assert.IsFalse(viewModel.RecordingEnabled);
        Assert.AreEqual(90, viewModel.RetentionDays);
        StringAssert.Contains(viewModel.ExcludedProcessNamesText, "KeePassXC.exe", StringComparison.Ordinal);
    }

    [TestMethod]
    public void ExcludedProcessNamesAreTrimmedDeduplicatedAndSorted()
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions)
        {
            ExcludedProcessNamesText = " zeta.exe\nAlpha.exe, alpha.exe ; beta.exe ",
        };

        Assert.HasCount(0, actions.SettingsDrafts);
        Assert.IsTrue(viewModel.HasPendingExcludedProcessChanges);

        viewModel.ApplyExcludedProcessNamesCommand.Execute(null);

        Assert.HasCount(1, actions.SettingsDrafts);
        var draft = actions.SettingsDrafts[0];
        CollectionAssert.AreEqual(
            ExpectedExcludedProcessNames,
            draft.ExcludedProcessNames.ToArray());
        Assert.IsTrue(viewModel.HasPendingExcludedProcessChanges);

        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: true,
            AutomaticPasteEnabled: true,
            CapturesImages: true,
            RetentionDays: 30,
            MaximumItemCount: 500,
            ExcludedProcessNames: ExpectedExcludedProcessNames,
            DataDirectory: @"C:\TuckClip"));

        Assert.IsFalse(viewModel.HasPendingExcludedProcessChanges);
    }

    [TestMethod]
    public void OtherSettingsChangesKeepTheLastCommittedExclusionList()
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);
        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: true,
            AutomaticPasteEnabled: true,
            CapturesImages: true,
            RetentionDays: 30,
            MaximumItemCount: 500,
            ExcludedProcessNames: ["KeePassXC.exe"],
            DataDirectory: @"C:\TuckClip"));

        viewModel.ExcludedProcessNamesText = "partially-typed";
        viewModel.RecordingEnabled = false;

        Assert.HasCount(1, actions.SettingsDrafts);
        CollectionAssert.AreEqual(
            ExpectedCommittedExcludedProcessNames,
            actions.SettingsDrafts[0].ExcludedProcessNames.ToArray());

        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: false,
            AutomaticPasteEnabled: true,
            CapturesImages: true,
            RetentionDays: 30,
            MaximumItemCount: 500,
            ExcludedProcessNames: ExpectedCommittedExcludedProcessNames,
            DataDirectory: @"C:\TuckClip"));

        Assert.AreEqual("partially-typed", viewModel.ExcludedProcessNamesText);
        Assert.IsTrue(viewModel.HasPendingExcludedProcessChanges);
    }

    [TestMethod]
    [DataRow(ClearHistoryScope.Unpinned)]
    [DataRow(ClearHistoryScope.All)]
    public void ClearRequiresExplicitConfirmation(ClearHistoryScope scope)
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);

        viewModel.BeginClear(scope);
        Assert.IsTrue(viewModel.IsClearConfirmationVisible);
        Assert.HasCount(0, actions.ClearRequests);

        viewModel.ConfirmClear();

        Assert.IsFalse(viewModel.IsClearConfirmationVisible);
        CollectionAssert.AreEqual(new[] { scope }, actions.ClearRequests);
    }

    [TestMethod]
    public void ReadOnlyStorageBlocksClearRequests()
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);
        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: true,
            AutomaticPasteEnabled: true,
            CapturesImages: true,
            RetentionDays: 30,
            MaximumItemCount: 500,
            ExcludedProcessNames: [],
            DataDirectory: @"C:\TuckClip",
            IsStorageReadOnly: true,
            StorageError: "历史文件损坏"));

        viewModel.BeginClear(ClearHistoryScope.All);

        Assert.IsFalse(viewModel.IsClearConfirmationVisible);
        Assert.HasCount(0, actions.ClearRequests);
    }

    [TestMethod]
    public void HotKeyRecorderValidatesAndSubmitsACompleteGesture()
    {
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);

        viewModel.BeginHotKeyRecording();
        viewModel.CaptureHotKey(new GlobalHotKey(0x58, HotKeyModifiers.None));

        Assert.IsTrue(viewModel.IsRecordingHotKey);
        Assert.IsTrue(viewModel.HasHotKeyError);
        Assert.HasCount(0, actions.HotKeys);

        var requested = new GlobalHotKey(
            0x58,
            HotKeyModifiers.Control | HotKeyModifiers.Shift);
        viewModel.AutomaticPasteEnabled = false;
        viewModel.CaptureHotKey(requested);

        Assert.IsFalse(viewModel.IsRecordingHotKey);
        Assert.IsFalse(viewModel.HasHotKeyError);
        CollectionAssert.AreEqual(new[] { requested }, actions.HotKeys);
        Assert.HasCount(1, actions.HotKeyDrafts);
        Assert.IsFalse(actions.HotKeyDrafts[0].AutomaticPasteEnabled);
    }

    [TestMethod]
    public void HotKeyFailureSnapshotKeepsTheEffectiveGestureVisible()
    {
        var viewModel = new ClipboardSettingsViewModel();
        var effective = new GlobalHotKey(0x42, HotKeyModifiers.Alt | HotKeyModifiers.Shift);

        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: true,
            AutomaticPasteEnabled: true,
            CapturesImages: true,
            RetentionDays: 30,
            MaximumItemCount: 500,
            ExcludedProcessNames: [],
            DataDirectory: @"C:\TuckClip",
            GlobalHotKey: effective,
            HotKeyError: "组合键已被占用"));

        Assert.AreEqual(effective, viewModel.GlobalHotKey);
        Assert.AreEqual("Alt+Shift+B", viewModel.HotKeyButtonText);
        Assert.IsTrue(viewModel.HasHotKeyError);
        StringAssert.Contains(viewModel.HotKeyStatusText, "已被占用", StringComparison.Ordinal);
    }

    [TestMethod]
    public void SelectingLanguageSubmitsItWithoutChangingOtherSettings()
    {
        AppLocalization.Apply(AppLanguage.SimplifiedChinese);
        var actions = new RecordingUiActions();
        var viewModel = new ClipboardSettingsViewModel(actions);
        viewModel.ApplySnapshot(new ClipboardSettingsSnapshot(
            RecordingEnabled: false,
            AutomaticPasteEnabled: false,
            CapturesImages: false,
            RetentionDays: 90,
            MaximumItemCount: 1_000,
            ExcludedProcessNames: ["KeePassXC.exe"],
            DataDirectory: @"C:\TuckClip",
            AppLanguage: AppLanguage.SimplifiedChinese));

        viewModel.SelectedLanguageOption = viewModel.LanguageOptions.Single(
            option => option.Value == AppLanguage.English);

        Assert.HasCount(1, actions.SettingsDrafts);
        var draft = actions.SettingsDrafts[0];
        Assert.AreEqual(AppLanguage.English, draft.AppLanguage);
        Assert.IsFalse(draft.RecordingEnabled);
        Assert.IsFalse(draft.AutomaticPasteEnabled);
        Assert.IsFalse(draft.CapturesImages);
        Assert.AreEqual(90, draft.RetentionDays);
        Assert.AreEqual(1_000, draft.MaximumItemCount);
        CollectionAssert.AreEqual(
            ExpectedCommittedExcludedProcessNames,
            draft.ExcludedProcessNames.ToArray());
    }
}

internal sealed class RecordingUiActions : IClipboardUiActions
{
    public List<(Guid Id, bool AsPlainText)> Pastes { get; } = [];

    public List<Guid> PinToggles { get; } = [];

    public List<Guid> Deletes { get; } = [];

    public List<ClearHistoryScope> ClearRequests { get; } = [];

    public List<ClipboardSettingsDraft> SettingsDrafts { get; } = [];

    public List<GlobalHotKey> HotKeys { get; } = [];

    public List<ClipboardSettingsDraft> HotKeyDrafts { get; } = [];

    public void PasteItem(Guid itemId, bool asPlainText) => Pastes.Add((itemId, asPlainText));

    public void TogglePinned(Guid itemId) => PinToggles.Add(itemId);

    public void DeleteItem(Guid itemId) => Deletes.Add(itemId);

    public void ClearHistory(ClearHistoryScope scope) => ClearRequests.Add(scope);

    public void ApplySettings(ClipboardSettingsDraft settings) => SettingsDrafts.Add(settings);

    public void ChangeGlobalHotKey(GlobalHotKey hotKey, ClipboardSettingsDraft settings)
    {
        HotKeys.Add(hotKey);
        HotKeyDrafts.Add(settings);
    }

    public void RevealDataDirectory()
    {
    }

    public void ShowSettings()
    {
    }

    public void HidePanel()
    {
    }
}
