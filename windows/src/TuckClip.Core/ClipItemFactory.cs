namespace TuckClip.Core;

public static class ClipItemFactory
{
    public static ClipItem Create(CaptureDecision decision, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.IsAccepted || decision.Capture is null || decision.Kind is null)
        {
            throw new ArgumentException("Only accepted capture decisions can become history items.", nameof(decision));
        }

        var capture = decision.Capture;
        var kind = decision.Kind.Value;
        var timestamp = capture.CapturedAt.ToUniversalTime();
        var filePaths = capture.FilePaths.ToArray();
        var imageData = capture.ImageData is null ? null : (byte[])capture.ImageData.Clone();

        return new ClipItem
        {
            Id = id ?? Guid.NewGuid(),
            Kind = kind,
            PlainText = capture.PlainText,
            FilePaths = filePaths,
            ImageData = imageData,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            SourceAppName = capture.SourceAppName,
            SourceIdentifier = capture.SourceIdentifier,
            Fingerprint = ClipFingerprint.Compute(kind, capture.PlainText, filePaths, imageData),
            CopyCount = 1,
        };
    }
}
