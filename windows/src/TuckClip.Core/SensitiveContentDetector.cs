using System.Text.RegularExpressions;

namespace TuckClip.Core;

public static partial class SensitiveContentDetector
{
    [GeneratedRegex(
        "-----BEGIN[ \\t]+(?:[A-Z0-9-]+[ \\t]+){0,3}PRIVATE[ \\t]+KEY(?:[ \\t]+BLOCK)?-----",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex PrivateKeyHeaderRegex();

    [GeneratedRegex(
        "(?m)^PuTTY-User-Key-File-(?:2|3):[^\\r\\n]+(?:\\r?\\n){1,4}Encryption:[^\\r\\n]+(?:\\r?\\n){1,4}Comment:[^\\r\\n]*(?:\\r?\\n){1,4}Public-Lines:[ \\t]*\\d+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex PuttyPrivateKeyRegex();

    public static bool ContainsHighConfidencePrivateKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return PrivateKeyHeaderRegex().IsMatch(value) || PuttyPrivateKeyRegex().IsMatch(value);
    }
}
