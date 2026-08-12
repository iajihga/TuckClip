using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TuckClip.Core.Persistence;

internal sealed record PersistedClipItem(
    Guid Id,
    ClipKind Kind,
    string? PlainText,
    IReadOnlyList<string> FilePaths,
    string? ImageFileName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SourceAppName,
    string? SourceIdentifier,
    string Fingerprint,
    bool IsPinned,
    int CopyCount);

internal static class HistoryJsonCodec
{
    public const int SchemaVersion = 1;

    public static byte[] Serialize(IReadOnlyList<PersistedClipItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WritePropertyName("items");
            writer.WriteStartArray();

            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString("kind", item.Kind.ToWireName());

                if (item.PlainText is null)
                {
                    writer.WriteNull("plainText");
                }
                else
                {
                    writer.WriteString("plainText", item.PlainText);
                }

                writer.WritePropertyName("filePaths");
                writer.WriteStartArray();
                foreach (var path in item.FilePaths)
                {
                    writer.WriteStringValue(path);
                }

                writer.WriteEndArray();
                if (item.ImageFileName is null)
                {
                    writer.WriteNull("imageFileName");
                }
                else
                {
                    writer.WriteString("imageFileName", item.ImageFileName);
                }

                writer.WriteString("createdAt", FormatTimestamp(item.CreatedAt));
                writer.WriteString("updatedAt", FormatTimestamp(item.UpdatedAt));
                if (item.SourceAppName is null)
                {
                    writer.WriteNull("sourceAppName");
                }
                else
                {
                    writer.WriteString("sourceAppName", item.SourceAppName);
                }

                if (item.SourceIdentifier is null)
                {
                    writer.WriteNull("sourceIdentifier");
                }
                else
                {
                    writer.WriteString("sourceIdentifier", item.SourceIdentifier);
                }

                writer.WriteString("fingerprint", item.Fingerprint);
                writer.WriteBoolean("isPinned", item.IsPinned);
                writer.WriteNumber("copyCount", item.CopyCount);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static IReadOnlyList<PersistedClipItem> Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("schemaVersion").GetInt32() != SchemaVersion)
        {
            throw new InvalidDataException("Unsupported history schema version.");
        }

        var array = root.GetProperty("items");
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("History items must be an array.");
        }

        var items = new List<PersistedClipItem>(array.GetArrayLength());
        var seenIds = new HashSet<Guid>();
        var seenContent = new HashSet<(ClipKind Kind, string Fingerprint)>();
        foreach (var element in array.EnumerateArray())
        {
            var id = Guid.ParseExact(RequiredString(element, "id"), "D");
            var kind = ClipKindExtensions.ParseWireName(RequiredString(element, "kind"));
            var plainText = OptionalString(element, "plainText");
            var filePaths = ReadFilePaths(element);
            var imageFileName = OptionalString(element, "imageFileName");
            var createdAt = ParseTimestamp(RequiredString(element, "createdAt"));
            var updatedAt = ParseTimestamp(RequiredString(element, "updatedAt"));
            var sourceAppName = OptionalString(element, "sourceAppName");
            var sourceIdentifier = OptionalString(element, "sourceIdentifier");
            var fingerprint = RequiredString(element, "fingerprint");
            var isPinned = element.GetProperty("isPinned").GetBoolean();
            var copyCount = element.GetProperty("copyCount").GetInt32();

            if (!ClipFingerprint.IsValid(fingerprint) || copyCount < 1 || updatedAt < createdAt)
            {
                throw new InvalidDataException("History item contains invalid identity or timestamps.");
            }

            if (!seenIds.Add(id) || !seenContent.Add((kind, fingerprint)))
            {
                throw new InvalidDataException("History contains duplicate stable identities or content records.");
            }

            items.Add(new PersistedClipItem(
                id,
                kind,
                plainText,
                filePaths,
                imageFileName,
                createdAt,
                updatedAt,
                sourceAppName,
                sourceIdentifier,
                fingerprint,
                isPinned,
                copyCount));
        }

        return items;
    }

    private static string[] ReadFilePaths(JsonElement element)
    {
        var pathsElement = element.GetProperty("filePaths");
        if (pathsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("File paths must be an array.");
        }

        return pathsElement.EnumerateArray().Select(path => path.GetString() ?? throw new InvalidDataException(
            "File paths cannot contain null values.")).ToArray();
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
        {
            throw new InvalidDataException($"Property '{propertyName}' must be a non-empty string.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new InvalidDataException($"Property '{propertyName}' must be a string or null."),
        };
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            throw new InvalidDataException("History timestamp is invalid.");
        }

        return timestamp;
    }
}
