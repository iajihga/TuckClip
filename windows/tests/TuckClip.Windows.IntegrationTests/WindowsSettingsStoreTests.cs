using TuckClip.Windows.Services;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.IntegrationTests;

[TestClass]
public sealed class WindowsSettingsStoreTests
{
    private static readonly string[] ExpectedExcludedProcessNames = ["Bitwarden", "KeePassXC"];
    private static readonly string[] ExpectedSettingsFiles = [WindowsSettingsStore.FileName];

    private string _testDirectory = null!;

    [TestInitialize]
    public void CreateTestDirectory()
    {
        _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "TuckClip.Windows.IntegrationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void RemoveTestDirectory()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsyncMissingFileReturnsSafeDefaults()
    {
        var store = new WindowsSettingsStore(_testDirectory);

        var settings = await store.LoadAsync();

        Assert.IsTrue(settings.RecordingEnabled);
        Assert.IsTrue(settings.AutomaticPasteEnabled);
        Assert.IsTrue(settings.CapturesImages);
        Assert.AreEqual(30, settings.RetentionDays);
        Assert.AreEqual(500, settings.MaximumItemCount);
        Assert.IsEmpty(settings.ExcludedProcessNames);
        Assert.AreEqual(GlobalHotKey.Default, settings.GlobalHotKey);
    }

    [TestMethod]
    public void PrivacySafeRecoveryDisablesCaptureAndAutomaticPaste()
    {
        var settings = WindowsAppSettings.PrivacySafeRecovery.Validate();

        Assert.IsFalse(settings.RecordingEnabled);
        Assert.IsFalse(settings.AutomaticPasteEnabled);
        Assert.IsFalse(settings.CapturesImages);
    }

    [TestMethod]
    public async Task SaveAndLoadAsyncRoundTripsAndNormalizesProcessNames()
    {
        var store = new WindowsSettingsStore(_testDirectory);
        var settings = new WindowsAppSettings
        {
            RecordingEnabled = false,
            AutomaticPasteEnabled = false,
            CapturesImages = false,
            RetentionDays = 90,
            MaximumItemCount = 1_000,
            ExcludedProcessNames =
            [
                @"C:\Program Files\Bitwarden.exe",
                "  bitwarden  ",
                "KeePassXC.EXE",
            ],
            GlobalHotKey = new GlobalHotKey(
                0x58,
                HotKeyModifiers.Control | HotKeyModifiers.Shift),
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.IsFalse(loaded.RecordingEnabled);
        Assert.IsFalse(loaded.AutomaticPasteEnabled);
        Assert.IsFalse(loaded.CapturesImages);
        Assert.AreEqual(90, loaded.RetentionDays);
        Assert.AreEqual(1_000, loaded.MaximumItemCount);
        CollectionAssert.AreEqual(
            ExpectedExcludedProcessNames,
            loaded.ExcludedProcessNames.ToArray());
        Assert.AreEqual(settings.GlobalHotKey, loaded.GlobalHotKey);
    }

    [TestMethod]
    public async Task LoadAsyncVersionOneWithoutHotKeyUsesCurrentDefault()
    {
        var store = new WindowsSettingsStore(_testDirectory);
        const string legacy = """
            {
              "schemaVersion": 1,
              "recordingEnabled": true,
              "automaticPasteEnabled": true,
              "capturesImages": true,
              "retentionDays": 30,
              "maximumItemCount": 500,
              "excludedProcessNames": []
            }
            """;
        await File.WriteAllTextAsync(store.SettingsPath, legacy);

        var loaded = await store.LoadAsync();

        Assert.AreEqual(GlobalHotKey.Default, loaded.GlobalHotKey);
    }

    [TestMethod]
    public async Task LoadAsyncCorruptFileThrowsWithoutChangingOriginalBytes()
    {
        var store = new WindowsSettingsStore(_testDirectory);
        var invalidContents = "{\"schemaVersion\":99,\"unexpected\":true}"u8.ToArray();
        await File.WriteAllBytesAsync(store.SettingsPath, invalidContents);

        await Assert.ThrowsExactlyAsync<WindowsSettingsCorruptedException>(
            () => store.LoadAsync());

        CollectionAssert.AreEqual(invalidContents, await File.ReadAllBytesAsync(store.SettingsPath));
    }

    [TestMethod]
    public async Task SaveAsyncInvalidCandidateDoesNotReplaceExistingSettings()
    {
        var store = new WindowsSettingsStore(_testDirectory);
        await store.SaveAsync(new WindowsAppSettings { MaximumItemCount = 500 });
        var originalContents = await File.ReadAllBytesAsync(store.SettingsPath);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.SaveAsync(new WindowsAppSettings { MaximumItemCount = 0 }));

        CollectionAssert.AreEqual(originalContents, await File.ReadAllBytesAsync(store.SettingsPath));
    }

    [TestMethod]
    public async Task SaveAsyncLeavesNoTemporaryFilesAfterCommit()
    {
        var store = new WindowsSettingsStore(_testDirectory);

        await store.SaveAsync(new WindowsAppSettings());

        CollectionAssert.AreEqual(
            ExpectedSettingsFiles,
            Directory.GetFiles(_testDirectory).Select(Path.GetFileName).ToArray());
    }
}
