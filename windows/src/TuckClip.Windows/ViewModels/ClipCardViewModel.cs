using Avalonia;
using Avalonia.Media;

namespace TuckClip.Windows.ViewModels;

public enum ClipDisplayKind
{
    Text,
    Link,
    Image,
    Files,
}

/// <summary>
/// Presentation-only clipboard item. Image bytes are borrowed from the shared
/// logical ClipItem and decoded by the card only while it is visible.
/// </summary>
public sealed class ClipCardViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush TextAccent = Brush.Parse("#31D1EA");
    private static readonly IBrush LinkAccent = Brush.Parse("#668CFF");
    private static readonly IBrush ImageAccent = Brush.Parse("#A373FF");
    private static readonly IBrush FilesAccent = Brush.Parse("#52C2C7");
    private static readonly IBrush SelectedBorder = Brush.Parse("#7F7BFF");
    private static readonly IBrush NormalBorder = Brush.Parse("#1FFFFFFF");
    private static readonly IBrush SelectedBackground = Brush.Parse("#2631D1EA");
    private static readonly IBrush NormalBackground = Brush.Parse("#16FFFFFF");

    private bool _isPinned;
    private bool _isSelected;
    private bool _isDisposed;
    private readonly byte[]? _encodedThumbnailData;
    private int? _shortcutIndex;

    public ClipCardViewModel(
        Guid id,
        ClipDisplayKind kind,
        string title,
        string? detail,
        string? searchableContent,
        string? sourceName,
        string? sourceIdentifier,
        DateTimeOffset capturedAt,
        bool isPinned,
        byte[]? encodedThumbnailData = null)
    {
        Id = id;
        Kind = kind;
        Title = title ?? string.Empty;
        Detail = detail ?? string.Empty;
        SearchableContent = searchableContent ?? string.Empty;
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? "未知应用" : sourceName;
        SourceIdentifier = sourceIdentifier;
        CapturedAt = capturedAt;
        _isPinned = isPinned;
        _encodedThumbnailData = encodedThumbnailData is { Length: > 0 }
            ? encodedThumbnailData
            : null;
    }

    public Guid Id { get; }

    public ClipDisplayKind Kind { get; }

    public string Title { get; }

    public string Detail { get; }

    public string SearchableContent { get; }

    public string SourceName { get; }

    public string? SourceIdentifier { get; }

    public DateTimeOffset CapturedAt { get; }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinLabel));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(CardBackground));
                OnPropertyChanged(nameof(CardBorderBrush));
                OnPropertyChanged(nameof(CardBorderThickness));
            }
        }
    }

    public int? ShortcutIndex
    {
        get => _shortcutIndex;
        internal set
        {
            if (SetProperty(ref _shortcutIndex, value))
            {
                OnPropertyChanged(nameof(HasShortcut));
                OnPropertyChanged(nameof(ShortcutText));
            }
        }
    }

    public string KindTitle => Kind switch
    {
        ClipDisplayKind.Text => "文本",
        ClipDisplayKind.Link => "链接",
        ClipDisplayKind.Image => "图片",
        ClipDisplayKind.Files => "文件",
        _ => "内容",
    };

    public string KindIcon => Kind switch
    {
        ClipDisplayKind.Text => "≡",
        ClipDisplayKind.Link => "↗",
        ClipDisplayKind.Image => "▧",
        ClipDisplayKind.Files => "▱",
        _ => "•",
    };

    public IBrush KindAccentBrush => Kind switch
    {
        ClipDisplayKind.Text => TextAccent,
        ClipDisplayKind.Link => LinkAccent,
        ClipDisplayKind.Image => ImageAccent,
        ClipDisplayKind.Files => FilesAccent,
        _ => TextAccent,
    };

    public IBrush CardBackground => IsSelected ? SelectedBackground : NormalBackground;

    public IBrush CardBorderBrush => IsSelected ? SelectedBorder : NormalBorder;

    public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "无标题内容" : Title;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool IsImage => Kind == ClipDisplayKind.Image;

    public bool IsNotImage => !IsImage;

    public bool HasThumbnailData => IsImage && _encodedThumbnailData is not null;

    public bool SupportsPlainTextPaste => Kind is ClipDisplayKind.Text or ClipDisplayKind.Link;

    public string RelativeTime => FormatRelativeTime(CapturedAt, DateTimeOffset.Now);

    public string PinLabel => IsPinned ? "已置顶" : "置顶";

    public bool HasShortcut => ShortcutIndex.HasValue;

    public string ShortcutText => ShortcutIndex is int index ? $"Ctrl+{index}" : string.Empty;

    public string AccessibilitySummary =>
        $"{KindTitle}，{DisplayTitle}，来源 {SourceName}{(IsPinned ? "，已置顶" : string.Empty)}";

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var fields = new[]
        {
            Title,
            Detail,
            SearchableContent,
            SourceName,
            SourceIdentifier ?? string.Empty,
        };

        var tokens = query.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.All(token => fields.Any(field => ContainsIgnoringCaseAndWidth(field, token)));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns borrowed, decrypted image bytes for an in-memory decode. The
    /// caller must not mutate or persist them. Disposing the view model revokes
    /// future requests but deliberately does not clear HistoryStore-owned data.
    /// </summary>
    internal bool TryGetEncodedThumbnailData(out byte[] data)
    {
        if (!_isDisposed && IsImage && _encodedThumbnailData is { } thumbnailData)
        {
            data = thumbnailData;
            return true;
        }

        data = [];
        return false;
    }

    private static bool ContainsIgnoringCaseAndWidth(string value, string query) =>
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            value,
            query,
            System.Globalization.CompareOptions.IgnoreCase
                | System.Globalization.CompareOptions.IgnoreNonSpace
                | System.Globalization.CompareOptions.IgnoreWidth) >= 0;

    private static string FormatRelativeTime(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var elapsed = now - capturedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} 分钟前";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours} 小时前";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays} 天前";
        }

        return capturedAt.LocalDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.CurrentCulture);
    }
}
