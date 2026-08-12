namespace TuckClip.Core;

public static class HistoryPolicy
{
    public static IReadOnlyList<ClipItem> Upsert(
        IEnumerable<ClipItem> currentItems,
        ClipItem capturedItem,
        AppSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(currentItems);
        ArgumentNullException.ThrowIfNull(capturedItem);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var items = currentItems.ToList();
        var duplicateIndex = items.FindIndex(
            item => item.Kind == capturedItem.Kind &&
                string.Equals(item.Fingerprint, capturedItem.Fingerprint, StringComparison.Ordinal));

        if (duplicateIndex >= 0)
        {
            var existing = items[duplicateIndex];
            items[duplicateIndex] = capturedItem with
            {
                Id = existing.Id,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = capturedItem.UpdatedAt,
                IsPinned = existing.IsPinned,
                CopyCount = SaturatingAdd(existing.CopyCount, 1),
                ImageFileName = existing.ImageFileName,
            };
        }
        else
        {
            items.Add(capturedItem);
        }

        return Prune(items, settings, now);
    }

    public static IReadOnlyList<ClipItem> Prune(
        IEnumerable<ClipItem> items,
        AppSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var deduplicated = Deduplicate(items);
        if (settings.RetentionDays > 0)
        {
            var cutoff = now.ToUniversalTime().AddDays(-settings.RetentionDays);
            deduplicated = deduplicated
                .Where(item => item.IsPinned || item.UpdatedAt >= cutoff)
                .ToArray();
        }

        var sorted = Sort(deduplicated);
        var pinnedCount = sorted.Count(item => item.IsPinned);
        var unpinnedCapacity = Math.Max(0, settings.MaximumItemCount - pinnedCount);
        var retainedUnpinned = 0;
        return sorted.Where(item => item.IsPinned || retainedUnpinned++ < unpinnedCapacity).ToArray();
    }

    public static IReadOnlyList<ClipItem> Sort(IEnumerable<ClipItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();
    }

    private static ClipItem[] Deduplicate(IEnumerable<ClipItem> items)
    {
        return items
            .GroupBy(item => (item.Kind, item.Fingerprint))
            .Select(group =>
            {
                var sorted = Sort(group);
                var newest = sorted[0];
                return newest with
                {
                    CreatedAt = group.Min(item => item.CreatedAt),
                    IsPinned = group.Any(item => item.IsPinned),
                    CopyCount = group.Aggregate(0, (total, item) => SaturatingAdd(total, Math.Max(1, item.CopyCount))),
                };
            })
            .ToArray();
    }

    private static int SaturatingAdd(int left, int right)
    {
        if (left >= int.MaxValue - right)
        {
            return int.MaxValue;
        }

        return left + right;
    }
}
