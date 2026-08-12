using System.Security.Cryptography;
using System.Text;
using TuckClip.Core.Persistence;

namespace TuckClip.Core.Tests;

internal static class TestItems
{
    public static ClipItem Text(
        string text,
        DateTimeOffset? timestamp = null,
        Guid? id = null,
        bool pinned = false,
        int copyCount = 1,
        string? sourceAppName = null,
        string? sourceIdentifier = null)
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                PlainText = text,
                CapturedAt = timestamp ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
                SourceAppName = sourceAppName,
                SourceIdentifier = sourceIdentifier,
            },
            new AppSettings());
        return ClipItemFactory.Create(decision, id) with { IsPinned = pinned, CopyCount = copyCount };
    }

    public static ClipItem Image(byte[] bytes, DateTimeOffset? timestamp = null, Guid? id = null)
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                ImageData = bytes,
                CapturedAt = timestamp ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
            new AppSettings());
        return ClipItemFactory.Create(decision, id);
    }

    public static ClipItem Files(string[] paths, DateTimeOffset? timestamp = null, Guid? id = null)
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                FilePaths = paths,
                CapturedAt = timestamp ?? new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
            new AppSettings());
        return ClipItemFactory.Create(decision, id);
    }
}

internal sealed class AuthenticatedTestProtector : IDataProtector
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("TuckClip test-only protector key"));

    public string? FailProtectPurpose { get; set; }

    public Dictionary<string, byte[]> LastPlaintextByPurpose { get; } = new(StringComparer.Ordinal);

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        if (string.Equals(FailProtectPurpose, purpose, StringComparison.Ordinal))
        {
            throw new CryptographicException("Injected protection failure.");
        }

        LastPlaintextByPurpose[purpose] = plaintext.ToArray();
        const int nonceLength = 12;
        const int tagLength = 16;
        var result = new byte[1 + nonceLength + tagLength + plaintext.Length];
        result[0] = 1;
        var nonce = result.AsSpan(1, nonceLength);
        var tag = result.AsSpan(1 + nonceLength, tagLength);
        var ciphertext = result.AsSpan(1 + nonceLength + tagLength);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, tagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(purpose));
        return result;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        if (protectedData.Length < 1 + nonceLength + tagLength || protectedData[0] != 1)
        {
            throw new CryptographicException("Invalid test envelope.");
        }

        var nonce = protectedData.Slice(1, nonceLength);
        var tag = protectedData.Slice(1 + nonceLength, tagLength);
        var ciphertext = protectedData[(1 + nonceLength + tagLength)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(purpose));
        return plaintext;
    }
}

internal sealed class FakeHistoryRepository : IHistoryRepository
{
    public IReadOnlyList<ClipItem> LoadedItems { get; set; } = Array.Empty<ClipItem>();

    public Exception? LoadException { get; set; }

    public Exception? SaveException { get; set; }

    public int SaveCount { get; private set; }

    public IReadOnlyList<ClipItem>? LastSavedItems { get; private set; }

    public Task<IReadOnlyList<ClipItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return LoadException is null
            ? Task.FromResult(LoadedItems)
            : Task.FromException<IReadOnlyList<ClipItem>>(LoadException);
    }

    public Task SaveAsync(IReadOnlyList<ClipItem> items, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        if (SaveException is not null)
        {
            return Task.FromException(SaveException);
        }

        LastSavedItems = items.ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TuckClip.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
