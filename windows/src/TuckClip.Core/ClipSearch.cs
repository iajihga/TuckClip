using System.Globalization;
using System.Text;

namespace TuckClip.Core;

public static class ClipSearch
{
    public static IReadOnlyList<ClipItem> Filter(IEnumerable<ClipItem> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);

        var tokens = Normalize(query ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ordered = HistoryPolicy.Sort(items);
        if (tokens.Length == 0)
        {
            return ordered;
        }

        return ordered.Where(item => Matches(item, tokens)).ToArray();
    }

    public static bool Matches(ClipItem item, string query)
    {
        ArgumentNullException.ThrowIfNull(item);
        var tokens = Normalize(query)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Matches(item, tokens);
    }

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var decomposed = value.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            builder.Append(rune.ToString().ToLowerInvariant());
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool Matches(ClipItem item, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        var surface = Normalize(string.Join(
            '\n',
            new[] { item.PlainText, item.SourceAppName, item.SourceIdentifier }
                .Where(value => !string.IsNullOrEmpty(value))
                .Concat(item.FilePaths)));
        return tokens.All(token => surface.Contains(token, StringComparison.Ordinal));
    }
}
