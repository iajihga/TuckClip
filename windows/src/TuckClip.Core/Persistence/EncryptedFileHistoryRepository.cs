using System.Security.Cryptography;
using System.Text.Json;

namespace TuckClip.Core.Persistence;

public sealed class EncryptedFileHistoryRepository : IHistoryRepository
{
    public const string MetadataFileName = "history-v1.json.protected";
    public const string ImageDirectoryName = "images";

    private const string MetadataPurpose = "TuckClip/history-metadata/v1";
    private const string ImagePurposePrefix = "TuckClip/history-image/v1/";
    private readonly string _rootDirectory;
    private readonly string _metadataPath;
    private readonly string _imageDirectory;
    private readonly IDataProtector _protector;

    public EncryptedFileHistoryRepository(string rootDirectory, IDataProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _metadataPath = Path.Combine(_rootDirectory, MetadataFileName);
        _imageDirectory = Path.Combine(_rootDirectory, ImageDirectoryName);
    }

    public async Task<IReadOnlyList<ClipItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_metadataPath))
        {
            return Array.Empty<ClipItem>();
        }

        try
        {
            var protectedMetadata = await File.ReadAllBytesAsync(_metadataPath, cancellationToken).ConfigureAwait(false);
            var metadata = _protector.Unprotect(protectedMetadata, MetadataPurpose);
            var persistedItems = HistoryJsonCodec.Deserialize(metadata);
            var items = new List<ClipItem>(persistedItems.Count);

            foreach (var persisted in persistedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? imageData = null;
                ValidatePayloadShape(persisted);
                if (persisted.Kind == ClipKind.Image)
                {
                    var imageFileName = persisted.ImageFileName!;
                    ValidateImageFileName(imageFileName, persisted.Fingerprint);
                    var imagePath = GetContainedImagePath(imageFileName);
                    var protectedImage = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
                    imageData = _protector.Unprotect(protectedImage, ImagePurposePrefix + imageFileName);
                    if (imageData.Length == 0)
                    {
                        throw new InvalidDataException("An image blob decrypted to an empty payload.");
                    }
                }

                var fingerprint = ClipFingerprint.Compute(
                    persisted.Kind,
                    persisted.PlainText,
                    persisted.FilePaths,
                    imageData);
                if (!string.Equals(fingerprint, persisted.Fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A history item fingerprint does not match its payload.");
                }

                items.Add(new ClipItem
                {
                    Id = persisted.Id,
                    Kind = persisted.Kind,
                    PlainText = persisted.PlainText,
                    FilePaths = persisted.FilePaths.ToArray(),
                    ImageData = imageData,
                    ImageFileName = persisted.ImageFileName,
                    CreatedAt = persisted.CreatedAt,
                    UpdatedAt = persisted.UpdatedAt,
                    SourceAppName = persisted.SourceAppName,
                    SourceIdentifier = persisted.SourceIdentifier,
                    Fingerprint = persisted.Fingerprint,
                    IsPinned = persisted.IsPinned,
                    CopyCount = persisted.CopyCount,
                });
            }

            return items;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsCorruptionException(exception))
        {
            throw new HistoryCorruptedException(
                "The encrypted clipboard history is damaged or cannot be decrypted. It was left unchanged.",
                exception);
        }
    }

    public async Task SaveAsync(IReadOnlyList<ClipItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        var persistedItems = new List<PersistedClipItem>(items.Count);
        var protectedImages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            ValidateItemForSave(item);
            string? imageFileName = null;
            if (item.Kind == ClipKind.Image)
            {
                imageFileName = GetImageFileName(item.Fingerprint);
                protectedImages.TryAdd(
                    imageFileName,
                    _protector.Protect(item.ImageData!, ImagePurposePrefix + imageFileName));
            }

            persistedItems.Add(new PersistedClipItem(
                item.Id,
                item.Kind,
                item.PlainText,
                item.FilePaths.ToArray(),
                imageFileName,
                item.CreatedAt.ToUniversalTime(),
                item.UpdatedAt.ToUniversalTime(),
                item.SourceAppName,
                item.SourceIdentifier,
                item.Fingerprint,
                item.IsPinned,
                item.CopyCount));
        }

        // Prepare every protected payload before touching the previous commit.
        var metadataJson = HistoryJsonCodec.Serialize(persistedItems);
        var protectedMetadata = _protector.Protect(metadataJson, MetadataPurpose);

        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(_imageDirectory);
        foreach (var (fileName, protectedImage) in protectedImages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(
                GetContainedImagePath(fileName),
                protectedImage,
                cancellationToken).ConfigureAwait(false);
        }

        await WriteAtomicallyAsync(_metadataPath, protectedMetadata, cancellationToken).ConfigureAwait(false);
        CleanupOrphanedImages(protectedImages.Keys);
    }

    private static void ValidatePayloadShape(PersistedClipItem item)
    {
        var isValid = item.Kind switch
        {
            ClipKind.Text or ClipKind.Link => item.PlainText is not null &&
                item.FilePaths.Count == 0 && item.ImageFileName is null,
            ClipKind.Files => item.PlainText is null && item.FilePaths.Count > 0 && item.ImageFileName is null,
            ClipKind.Image => item.PlainText is null && item.FilePaths.Count == 0 && item.ImageFileName is not null,
            _ => false,
        };

        if (!isValid)
        {
            throw new InvalidDataException("A history item has fields that do not match its kind.");
        }
    }

    private static void ValidateItemForSave(ClipItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var computedFingerprint = ClipFingerprint.Compute(item.Kind, item.PlainText, item.FilePaths, item.ImageData);
        if (!ClipFingerprint.IsValid(item.Fingerprint) ||
            !string.Equals(computedFingerprint, item.Fingerprint, StringComparison.Ordinal) ||
            item.CopyCount < 1 ||
            item.UpdatedAt < item.CreatedAt)
        {
            throw new ArgumentException("A history item is invalid or has a mismatched fingerprint.", nameof(item));
        }

        var shapeIsValid = item.Kind switch
        {
            ClipKind.Text or ClipKind.Link => item.PlainText is not null &&
                item.FilePaths.Count == 0 && item.ImageData is null,
            ClipKind.Files => item.PlainText is null && item.FilePaths.Count > 0 && item.ImageData is null,
            ClipKind.Image => item.PlainText is null && item.FilePaths.Count == 0 && item.ImageData is { Length: > 0 },
            _ => false,
        };

        if (!shapeIsValid)
        {
            throw new ArgumentException("A history item payload does not match its kind.", nameof(item));
        }
    }

    private static bool IsCorruptionException(Exception exception) => exception is
        CryptographicException or
        JsonException or
        InvalidDataException or
        FormatException or
        KeyNotFoundException or
        FileNotFoundException or
        DirectoryNotFoundException;

    private static string GetImageFileName(string fingerprint) => $"image-{fingerprint}.protected";

    private static void ValidateImageFileName(string fileName, string fingerprint)
    {
        if (!string.Equals(fileName, GetImageFileName(fingerprint), StringComparison.Ordinal))
        {
            throw new InvalidDataException("An image blob name is invalid.");
        }
    }

    private string GetContainedImagePath(string fileName)
    {
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            throw new InvalidDataException("An image blob path is not a safe relative file name.");
        }

        var candidate = Path.GetFullPath(Path.Combine(_imageDirectory, fileName));
        var prefix = _imageDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _imageDirectory
            : _imageDirectory + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("An image blob path escaped the repository.");
        }

        return candidate;
    }

    private static async Task WriteAtomicallyAsync(
        string destinationPath,
        byte[] contents,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The destination path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.bak");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, backupPath, ignoreMetadataErrors: true);
                TryDelete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
            TryDelete(backupPath);
        }
    }

    private void CleanupOrphanedImages(IEnumerable<string> referencedImageFiles)
    {
        var referenced = referencedImageFiles.ToHashSet(StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_imageDirectory, "image-*.protected", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (IsGeneratedImageFileName(fileName) && !referenced.Contains(fileName))
                {
                    TryDelete(path);
                }
            }
        }
        catch (IOException)
        {
            // The metadata commit is already durable; cleanup can be retried next save.
        }
        catch (UnauthorizedAccessException)
        {
            // The metadata commit is already durable; cleanup can be retried next save.
        }
    }

    private static bool IsGeneratedImageFileName(string fileName)
    {
        const string prefix = "image-";
        const string suffix = ".protected";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var fingerprint = fileName[prefix.Length..^suffix.Length];
        return ClipFingerprint.IsValid(fingerprint);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
