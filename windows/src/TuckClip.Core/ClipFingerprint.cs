using System.Security.Cryptography;
using System.Text;

namespace TuckClip.Core;

public static class ClipFingerprint
{
    public static string Compute(ClipKind kind, string? plainText, IReadOnlyList<string> filePaths, byte[]? imageData)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        byte[] payload = kind switch
        {
            ClipKind.Text or ClipKind.Link => Encoding.UTF8.GetBytes(plainText ?? string.Empty),
            ClipKind.Files => Encoding.UTF8.GetBytes(string.Join(
                '\0',
                filePaths.Select(path => NormalizeFilePath(path).ToUpperInvariant()))),
            ClipKind.Image => imageData is null ? Array.Empty<byte>() : (byte[])imageData.Clone(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        var kindBytes = Encoding.UTF8.GetBytes(kind.ToWireName());
        var input = new byte[kindBytes.Length + 1 + payload.Length];
        kindBytes.CopyTo(input, 0);
        payload.CopyTo(input, kindBytes.Length + 1);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    public static string NormalizeFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Trim().Normalize(NormalizationForm.FormC).Replace('/', '\\');
        var builder = new StringBuilder(normalized.Length);
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '\\' && builder.Length > 0 && builder[^1] == '\\')
            {
                // Preserve the leading pair of a UNC path, but collapse every
                // other repeated separator.
                if (index != 1 || normalized[0] != '\\')
                {
                    continue;
                }
            }

            builder.Append(character);
        }

        normalized = builder.ToString();
        if (normalized.Length > 3 && normalized.EndsWith('\\'))
        {
            normalized = normalized.TrimEnd('\\');
        }

        if (normalized.Length >= 2 && normalized[1] == ':' && char.IsAsciiLetter(normalized[0]))
        {
            normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        return normalized;
    }

    internal static bool IsValid(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
