namespace TuckClip.Platform.Windows.Clipboard;

public static class WindowsClipboardFormats
{
    public const string TuckClipInternalWrite = "io.github.iajihga.TuckClip.InternalWrite";
    public const string TuckClipWriteReceiptV1 = "io.github.iajihga.TuckClip.WriteReceipt.v1";
    public const string ExcludeFromMonitorProcessing = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInClipboardHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";
}

public interface IClipboardFormatReader
{
    bool Contains(string formatName);

    bool TryReadInt32(string formatName, out int value);
}

public enum ClipboardCaptureExclusionReason
{
    None,
    InternalWrite,
    MonitorProcessingExcluded,
    ClipboardHistoryDisabled,
    CloudClipboardDisabled,
    PasswordManager,
    ConfiguredProcess,
}

public readonly record struct ClipboardCaptureDecision(
    bool ShouldCapture,
    ClipboardCaptureExclusionReason ExclusionReason)
{
    public static ClipboardCaptureDecision Include { get; } = new(true, ClipboardCaptureExclusionReason.None);

    public static ClipboardCaptureDecision Exclude(ClipboardCaptureExclusionReason reason) => new(false, reason);
}

public sealed class ClipboardCapturePolicy
{
    private static readonly string[] DefaultPasswordManagerProcesses =
    [
        "1password",
        "1password.native",
        "bitwarden",
        "dashlane",
        "enpass",
        "keepass",
        "keepassxc",
        "keeperpasswordmanager",
        "lastpass",
        "nordpass",
        "protonpass",
        "roboform",
    ];

    private readonly HashSet<string> _passwordManagers;
    private readonly HashSet<string> _configuredProcesses;

    public ClipboardCapturePolicy(IEnumerable<string>? excludedProcessNames = null)
    {
        _passwordManagers = new HashSet<string>(
            DefaultPasswordManagerProcesses.Select(NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        _configuredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (excludedProcessNames is null)
        {
            return;
        }

        foreach (var processName in excludedProcessNames)
        {
            var normalized = NormalizeProcessName(processName);
            if (normalized.Length > 0)
            {
                _configuredProcesses.Add(normalized);
            }
        }
    }

    public ClipboardCaptureDecision Evaluate(
        IClipboardFormatReader formats,
        string? sourceProcessName)
    {
        ArgumentNullException.ThrowIfNull(formats);

        if (formats.Contains(WindowsClipboardFormats.TuckClipInternalWrite))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.InternalWrite);
        }

        if (formats.Contains(WindowsClipboardFormats.ExcludeFromMonitorProcessing))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.MonitorProcessingExcluded);
        }

        if (IsDisabled(formats, WindowsClipboardFormats.CanIncludeInClipboardHistory))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.ClipboardHistoryDisabled);
        }

        if (IsDisabled(formats, WindowsClipboardFormats.CanUploadToCloudClipboard))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.CloudClipboardDisabled);
        }

        var normalizedProcessName = NormalizeProcessName(sourceProcessName);
        if (_passwordManagers.Contains(normalizedProcessName))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.PasswordManager);
        }

        if (_configuredProcesses.Contains(normalizedProcessName))
        {
            return ClipboardCaptureDecision.Exclude(ClipboardCaptureExclusionReason.ConfiguredProcess);
        }

        return ClipboardCaptureDecision.Include;
    }

    private static bool IsDisabled(IClipboardFormatReader formats, string formatName)
    {
        if (!formats.Contains(formatName))
        {
            return false;
        }

        // Privacy markers fail closed: malformed or inaccessible marker data
        // is never treated as permission to persist clipboard content.
        return !formats.TryReadInt32(formatName, out var value) || value == 0;
    }

    private static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var trimmed = processName.Trim();
        var lastSeparator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (lastSeparator >= 0 && lastSeparator + 1 < trimmed.Length)
        {
            trimmed = trimmed[(lastSeparator + 1)..];
        }

        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }
}
