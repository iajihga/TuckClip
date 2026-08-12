using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TuckClip.Core.Persistence;

namespace TuckClip.Core.Tests;

[TestClass]
public sealed class EncryptedFileHistoryRepositoryTests
{
    private const string MetadataPurpose = "TuckClip/history-metadata/v1";

    [TestMethod]
    public async Task Load_WithoutMetadataReturnsEmptyHistory()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        Assert.IsEmpty(await repository.LoadAsync());
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsEveryKindAndLogicalField()
    {
        using var directory = new TempDirectory();
        var protector = new AuthenticatedTestProtector();
        var repository = new EncryptedFileHistoryRepository(directory.Path, protector);
        var timestamp = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var text = TestItems.Text(
            "hello",
            timestamp,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            pinned: true,
            copyCount: 7,
            sourceAppName: "Notepad",
            sourceIdentifier: "notepad");
        var link = TestItems.Text(
            "https://example.com",
            timestamp.AddSeconds(1),
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var files = TestItems.Files(
            [@"C:\Docs\A.txt", @"D:\Photos\B.png"],
            timestamp.AddSeconds(2),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var image = TestItems.Image(
            [137, 80, 78, 71, 1, 2, 3],
            timestamp.AddSeconds(3),
            Guid.Parse("44444444-4444-4444-4444-444444444444"));

        await repository.SaveAsync([text, link, files, image]);
        var loaded = await repository.LoadAsync();

        Assert.HasCount(4, loaded);
        Assert.AreEqual(ClipKind.Text, loaded[0].Kind);
        Assert.AreEqual("hello", loaded[0].PlainText);
        Assert.AreEqual("Notepad", loaded[0].SourceAppName);
        Assert.AreEqual("notepad", loaded[0].SourceIdentifier);
        Assert.IsTrue(loaded[0].IsPinned);
        Assert.AreEqual(7, loaded[0].CopyCount);
        Assert.AreEqual(ClipKind.Link, loaded[1].Kind);
        CollectionAssert.AreEqual(files.FilePaths.ToArray(), loaded[2].FilePaths.ToArray());
        CollectionAssert.AreEqual(image.ImageData!, loaded[3].ImageData!);
        StringAssert.StartsWith(loaded[3].ImageFileName!, "image-");
    }

    [TestMethod]
    public async Task Save_EncryptsMetadataAndImageSeparately()
    {
        using var directory = new TempDirectory();
        var protector = new AuthenticatedTestProtector();
        var repository = new EncryptedFileHistoryRepository(directory.Path, protector);
        var secretText = TestItems.Text("clipboard secret");
        var png = new byte[] { 137, 80, 78, 71, 9, 8, 7, 6 };
        var image = TestItems.Image(png);

        await repository.SaveAsync([secretText, image]);

        var metadataPath = Path.Combine(directory.Path, EncryptedFileHistoryRepository.MetadataFileName);
        var protectedMetadata = await File.ReadAllBytesAsync(metadataPath);
        Assert.IsFalse(Encoding.UTF8.GetString(protectedMetadata).Contains("clipboard secret", StringComparison.Ordinal));
        var imagePath = Directory.GetFiles(
            Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName),
            "image-*.protected").Single();
        var protectedImage = await File.ReadAllBytesAsync(imagePath);
        Assert.IsFalse(protectedImage.AsSpan().SequenceEqual(png));
        Assert.IsTrue(protector.LastPlaintextByPurpose.ContainsKey(MetadataPurpose));
        Assert.IsTrue(protector.LastPlaintextByPurpose.Keys.Any(key => key.Contains("history-image", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Save_ProducesStableSchemaV1Json()
    {
        using var directory = new TempDirectory();
        var protector = new AuthenticatedTestProtector();
        var repository = new EncryptedFileHistoryRepository(directory.Path, protector);
        var item = TestItems.Text(
            "hello",
            new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            pinned: true,
            copyCount: 2,
            sourceAppName: "Notepad",
            sourceIdentifier: "notepad");

        await repository.SaveAsync([item]);

        var json = Encoding.UTF8.GetString(protector.LastPlaintextByPurpose[MetadataPurpose]);
        var expected = "{\"schemaVersion\":1,\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"text\",\"plainText\":\"hello\",\"filePaths\":[],\"imageFileName\":null,\"createdAt\":\"2026-02-03T04:05:06.0000000+00:00\",\"updatedAt\":\"2026-02-03T04:05:06.0000000+00:00\",\"sourceAppName\":\"Notepad\",\"sourceIdentifier\":\"notepad\",\"fingerprint\":\"" + item.Fingerprint + "\",\"isPinned\":true,\"copyCount\":2}]}";
        Assert.AreEqual(expected, json);
    }

    [TestMethod]
    public async Task Save_ProtectionFailureLeavesPreviousCommitUntouched()
    {
        using var directory = new TempDirectory();
        var protector = new AuthenticatedTestProtector();
        var repository = new EncryptedFileHistoryRepository(directory.Path, protector);
        var original = TestItems.Image([137, 80, 78, 71, 1]);
        await repository.SaveAsync([original]);
        var metadataPath = Path.Combine(directory.Path, EncryptedFileHistoryRepository.MetadataFileName);
        var previousMetadata = await File.ReadAllBytesAsync(metadataPath);
        var previousImages = Directory.GetFiles(
            Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName)).Order().ToArray();
        protector.FailProtectPurpose = MetadataPurpose;

        await Assert.ThrowsExactlyAsync<CryptographicException>(
            () => repository.SaveAsync([TestItems.Image([137, 80, 78, 71, 2])]));

        CollectionAssert.AreEqual(previousMetadata, await File.ReadAllBytesAsync(metadataPath));
        CollectionAssert.AreEqual(
            previousImages,
            Directory.GetFiles(Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName)).Order().ToArray());
        protector.FailProtectPurpose = null;
        var loaded = await repository.LoadAsync();
        CollectionAssert.AreEqual(original.ImageData!, loaded.Single().ImageData!);
    }

    [TestMethod]
    public async Task Save_CleansGeneratedOrphansButLeavesUnrecognizedFiles()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        var image = TestItems.Image([137, 80, 78, 71, 1]);
        await repository.SaveAsync([image]);
        var imageDirectory = Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName);
        var orphan = Path.Combine(imageDirectory, $"image-{new string('a', 64)}.protected");
        var unrelated = Path.Combine(imageDirectory, "notes.txt");
        await File.WriteAllBytesAsync(orphan, [1, 2, 3]);
        await File.WriteAllTextAsync(unrelated, "keep me");

        await repository.SaveAsync([image]);

        Assert.IsFalse(File.Exists(orphan));
        Assert.IsTrue(File.Exists(unrelated));
    }

    [TestMethod]
    public async Task Load_TamperedMetadataThrowsCorruptionAndDoesNotRewriteIt()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        await repository.SaveAsync([TestItems.Text("original")]);
        var metadataPath = Path.Combine(directory.Path, EncryptedFileHistoryRepository.MetadataFileName);
        var corrupted = await File.ReadAllBytesAsync(metadataPath);
        corrupted[^1] ^= 0xff;
        await File.WriteAllBytesAsync(metadataPath, corrupted);

        await Assert.ThrowsExactlyAsync<HistoryCorruptedException>(() => repository.LoadAsync());

        CollectionAssert.AreEqual(corrupted, await File.ReadAllBytesAsync(metadataPath));
    }

    [TestMethod]
    public async Task Load_MissingReferencedImageThrowsCorruption()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        await repository.SaveAsync([TestItems.Image([137, 80, 78, 71, 1])]);
        var imagePath = Directory.GetFiles(
            Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName)).Single();
        File.Delete(imagePath);

        await Assert.ThrowsExactlyAsync<HistoryCorruptedException>(() => repository.LoadAsync());
    }

    [TestMethod]
    public async Task Load_TamperedImageThrowsCorruption()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        await repository.SaveAsync([TestItems.Image([137, 80, 78, 71, 1])]);
        var imagePath = Directory.GetFiles(
            Path.Combine(directory.Path, EncryptedFileHistoryRepository.ImageDirectoryName)).Single();
        var bytes = await File.ReadAllBytesAsync(imagePath);
        bytes[^1] ^= 0xff;
        await File.WriteAllBytesAsync(imagePath, bytes);

        await Assert.ThrowsExactlyAsync<HistoryCorruptedException>(() => repository.LoadAsync());
    }

    [TestMethod]
    public async Task Load_UnsafeImageNameThrowsCorruptionWithoutEscapingRoot()
    {
        using var directory = new TempDirectory();
        var protector = new AuthenticatedTestProtector();
        var repository = new EncryptedFileHistoryRepository(directory.Path, protector);
        await repository.SaveAsync([TestItems.Image([137, 80, 78, 71, 1])]);
        var metadataPath = Path.Combine(directory.Path, EncryptedFileHistoryRepository.MetadataFileName);
        var json = Encoding.UTF8.GetString(protector.LastPlaintextByPurpose[MetadataPurpose]);
        using var document = JsonDocument.Parse(json);
        var currentName = document.RootElement.GetProperty("items")[0].GetProperty("imageFileName").GetString()!;
        var tamperedJson = Encoding.UTF8.GetBytes(json.Replace(currentName, "../outside.protected", StringComparison.Ordinal));
        await File.WriteAllBytesAsync(metadataPath, protector.Protect(tamperedJson, MetadataPurpose));

        await Assert.ThrowsExactlyAsync<HistoryCorruptedException>(() => repository.LoadAsync());
    }

    [TestMethod]
    public async Task Save_RejectsMismatchedFingerprintBeforeOverwritingMetadata()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        var original = TestItems.Text("original");
        await repository.SaveAsync([original]);
        var metadataPath = Path.Combine(directory.Path, EncryptedFileHistoryRepository.MetadataFileName);
        var previous = await File.ReadAllBytesAsync(metadataPath);
        var invalid = TestItems.Text("changed") with { Fingerprint = original.Fingerprint };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => repository.SaveAsync([invalid]));

        CollectionAssert.AreEqual(previous, await File.ReadAllBytesAsync(metadataPath));
    }

    [TestMethod]
    public async Task Operations_HonorCancellation()
    {
        using var directory = new TempDirectory();
        var repository = new EncryptedFileHistoryRepository(directory.Path, new AuthenticatedTestProtector());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => repository.SaveAsync([TestItems.Text("cancel")], cancellation.Token));
    }
}
