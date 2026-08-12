using TuckClip.Core;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.Services;

public sealed record WindowsAppSettings
{
    public static WindowsAppSettings PrivacySafeRecovery { get; } = new()
    {
        RecordingEnabled = false,
        AutomaticPasteEnabled = false,
        CapturesImages = false,
    };

    public bool RecordingEnabled { get; init; } = true;

    public bool AutomaticPasteEnabled { get; init; } = true;

    public bool CapturesImages { get; init; } = true;

    public int RetentionDays { get; init; } = 30;

    public int MaximumItemCount { get; init; } = 500;

    public IReadOnlyList<string> ExcludedProcessNames { get; init; } = Array.Empty<string>();

    public GlobalHotKey GlobalHotKey { get; init; } = GlobalHotKey.Default;

    public WindowsAppSettings Validate()
    {
        _ = ToCoreSettings().Validate();
        ArgumentNullException.ThrowIfNull(ExcludedProcessNames);

        if (ExcludedProcessNames.Count > 256)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExcludedProcessNames),
                "At most 256 process exclusions are supported.");
        }

        var normalizedExclusions = ExcludedProcessNames
            .Select(NormalizeProcessName)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return this with
        {
            ExcludedProcessNames = normalizedExclusions,
            GlobalHotKey = GlobalHotKey.Validate(),
        };
    }

    public AppSettings ToCoreSettings() => new()
    {
        RetentionDays = RetentionDays,
        MaximumItemCount = MaximumItemCount,
    };

    private static string NormalizeProcessName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = value.Trim();
        if (trimmed.Length > 260)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "An excluded process name cannot exceed 260 characters.");
        }

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
