namespace TuckClip.Core;

public enum ClipKind
{
    Text,
    Link,
    Image,
    Files,
}

internal static class ClipKindExtensions
{
    public static string ToWireName(this ClipKind kind) => kind switch
    {
        ClipKind.Text => "text",
        ClipKind.Link => "link",
        ClipKind.Image => "image",
        ClipKind.Files => "files",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static ClipKind ParseWireName(string value) => value switch
    {
        "text" => ClipKind.Text,
        "link" => ClipKind.Link,
        "image" => ClipKind.Image,
        "files" => ClipKind.Files,
        _ => throw new FormatException($"Unsupported clip kind '{value}'."),
    };
}
