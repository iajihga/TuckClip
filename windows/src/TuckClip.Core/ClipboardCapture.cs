namespace TuckClip.Core;

/// <summary>
/// A platform clipboard snapshot after format extraction. Platform adapters may
/// populate more than one payload; <see cref="CapturePolicy.Normalize"/> applies
/// the shared files &gt; image &gt; text/link priority.
/// </summary>
public sealed record ClipboardCapture
{
    public string? PlainText { get; init; }

    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    /// <summary>Canonical PNG bytes.</summary>
    public byte[]? ImageData { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? SourceAppName { get; init; }

    public string? SourceIdentifier { get; init; }

    public bool IsTuckClipGenerated { get; init; }

    public bool IsPrivate { get; init; }

    public bool IsTransient { get; init; }

    public bool IsPasswordManagerContent { get; init; }

    public bool ExcludeFromMonitorProcessing { get; init; }

    public bool CanIncludeInClipboardHistory { get; init; } = true;

    public bool CanUploadToCloudClipboard { get; init; } = true;
}
