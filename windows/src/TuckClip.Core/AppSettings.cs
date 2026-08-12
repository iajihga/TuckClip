namespace TuckClip.Core;

public sealed record AppSettings
{
    public const int MinimumMaximumItemCount = 1;
    public const int MaximumMaximumItemCount = 10_000;
    public const int MaximumRetentionDays = 3_650;

    public int MaximumItemCount { get; init; } = 500;

    /// <summary>Zero disables age-based pruning.</summary>
    public int RetentionDays { get; init; } = 30;

    public bool FilterHighConfidencePrivateKeys { get; init; } = true;

    public AppSettings Validate()
    {
        if (MaximumItemCount is < MinimumMaximumItemCount or > MaximumMaximumItemCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumItemCount),
                $"Maximum item count must be between {MinimumMaximumItemCount} and {MaximumMaximumItemCount}.");
        }

        if (RetentionDays is < 0 or > MaximumRetentionDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetentionDays),
                $"Retention days must be between 0 and {MaximumRetentionDays}.");
        }

        return this;
    }
}
