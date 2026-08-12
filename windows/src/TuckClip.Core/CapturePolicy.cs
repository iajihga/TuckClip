using System.Text;

namespace TuckClip.Core;

public enum CaptureRejectionReason
{
    None,
    Empty,
    InternalWrite,
    PrivateOrTransient,
    PasswordManager,
    PlatformPolicy,
    Oversized,
    HighConfidencePrivateKey,
}

public sealed record CaptureDecision
{
    private CaptureDecision(ClipboardCapture? capture, ClipKind? kind, CaptureRejectionReason rejectionReason)
    {
        Capture = capture;
        Kind = kind;
        RejectionReason = rejectionReason;
    }

    public bool IsAccepted => Capture is not null && Kind is not null;

    public ClipboardCapture? Capture { get; }

    public ClipKind? Kind { get; }

    public CaptureRejectionReason RejectionReason { get; }

    public static CaptureDecision Accept(ClipboardCapture capture, ClipKind kind) =>
        new(capture, kind, CaptureRejectionReason.None);

    public static CaptureDecision Reject(CaptureRejectionReason reason) => new(null, null, reason);
}

public static class CapturePolicy
{
    public const int MaximumTextUtf8Bytes = 128 * 1024;
    public const int MaximumBinaryOrFileListBytes = 25 * 1024 * 1024;

    public static CaptureDecision Normalize(ClipboardCapture capture, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        if (capture.IsTuckClipGenerated)
        {
            return CaptureDecision.Reject(CaptureRejectionReason.InternalWrite);
        }

        if (capture.IsPrivate || capture.IsTransient)
        {
            return CaptureDecision.Reject(CaptureRejectionReason.PrivateOrTransient);
        }

        if (capture.IsPasswordManagerContent)
        {
            return CaptureDecision.Reject(CaptureRejectionReason.PasswordManager);
        }

        if (capture.ExcludeFromMonitorProcessing ||
            !capture.CanIncludeInClipboardHistory ||
            !capture.CanUploadToCloudClipboard)
        {
            return CaptureDecision.Reject(CaptureRejectionReason.PlatformPolicy);
        }

        if (capture.FilePaths.Count > 0)
        {
            string[] normalizedPaths;
            try
            {
                normalizedPaths = capture.FilePaths.Select(ClipFingerprint.NormalizeFilePath).ToArray();
            }
            catch (ArgumentException)
            {
                return CaptureDecision.Reject(CaptureRejectionReason.Empty);
            }

            var byteCount = Encoding.UTF8.GetByteCount(string.Join('\0', normalizedPaths));
            if (byteCount > MaximumBinaryOrFileListBytes)
            {
                return CaptureDecision.Reject(CaptureRejectionReason.Oversized);
            }

            return CaptureDecision.Accept(
                capture with { PlainText = null, ImageData = null, FilePaths = normalizedPaths },
                ClipKind.Files);
        }

        if (capture.ImageData is { Length: > 0 } imageData)
        {
            if (imageData.Length > MaximumBinaryOrFileListBytes)
            {
                return CaptureDecision.Reject(CaptureRejectionReason.Oversized);
            }

            return CaptureDecision.Accept(
                capture with
                {
                    PlainText = null,
                    FilePaths = Array.Empty<string>(),
                    ImageData = (byte[])imageData.Clone(),
                },
                ClipKind.Image);
        }

        if (string.IsNullOrEmpty(capture.PlainText))
        {
            return CaptureDecision.Reject(CaptureRejectionReason.Empty);
        }

        if (Encoding.UTF8.GetByteCount(capture.PlainText) > MaximumTextUtf8Bytes)
        {
            return CaptureDecision.Reject(CaptureRejectionReason.Oversized);
        }

        if (settings.FilterHighConfidencePrivateKeys &&
            SensitiveContentDetector.ContainsHighConfidencePrivateKey(capture.PlainText))
        {
            return CaptureDecision.Reject(CaptureRejectionReason.HighConfidencePrivateKey);
        }

        var kind = UrlClassifier.IsStandaloneUrl(capture.PlainText) ? ClipKind.Link : ClipKind.Text;
        return CaptureDecision.Accept(
            capture with { FilePaths = Array.Empty<string>(), ImageData = null },
            kind);
    }
}
