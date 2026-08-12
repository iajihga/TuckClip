namespace TuckClip.Core;

public static class UrlClassifier
{
    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeFtp,
        "mailto",
    };

    public static bool IsStandaloneUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Any(char.IsWhiteSpace) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return SupportedSchemes.Contains(uri.Scheme) &&
            (uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase)
                ? value.Length > "mailto:".Length
                : !string.IsNullOrEmpty(uri.Host));
    }
}
