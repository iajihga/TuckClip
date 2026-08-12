namespace TuckClip.Core;

public sealed record ClipItem
{
    public required Guid Id { get; init; }

    public required ClipKind Kind { get; init; }

    public string? PlainText { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    /// <summary>Decrypted canonical PNG bytes; never written into metadata JSON.</summary>
    public byte[]? ImageData { get; init; }

    public string? ImageFileName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public string? SourceAppName { get; init; }

    public string? SourceIdentifier { get; init; }

    public required string Fingerprint { get; init; }

    public bool IsPinned { get; init; }

    public int CopyCount { get; init; } = 1;
}
