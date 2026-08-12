using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TuckClip.Windows.ViewModels;
using TuckClip.Windows.Services;

namespace TuckClip.Windows.Controls;

public sealed partial class ClipCardControl : UserControl
{
    private static readonly ThumbnailLoader SharedThumbnailLoader = new();

    private BoundedLruCache<ThumbnailCacheKey, ThumbnailResource>.Lease? _thumbnailLease;
    private CancellationTokenSource? _thumbnailCancellation;
    private TopLevel? _topLevel;
    private int _thumbnailGeneration;
    private bool _isAttached;

    public ClipCardControl()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            AppLocalization.LanguageChanged += OnLanguageChanged;
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null)
            {
                _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
            }

            RefreshThumbnail();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            AppLocalization.LanguageChanged -= OnLanguageChanged;
            if (_topLevel is not null)
            {
                _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
                _topLevel = null;
            }

            ResetThumbnail();
        };
        DataContextChanged += (_, _) => RefreshThumbnail();
    }

    public event EventHandler<ClipCardActionEventArgs>? ActivateRequested;

    public event EventHandler<ClipCardActionEventArgs>? TogglePinRequested;

    public event EventHandler<ClipCardActionEventArgs>? DeleteRequested;

    private void OnActivateClick(object? sender, RoutedEventArgs e) =>
        RaiseActivate(asPlainText: false);

    private void OnPasteMenuClick(object? sender, RoutedEventArgs e) =>
        RaiseActivate(asPlainText: false);

    private void OnPlainTextMenuClick(object? sender, RoutedEventArgs e) =>
        RaiseActivate(asPlainText: true);

    private void OnTogglePinMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipCardViewModel item)
        {
            TogglePinRequested?.Invoke(this, new ClipCardActionEventArgs(item, asPlainText: false));
        }
    }

    private void OnDeleteMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipCardViewModel item)
        {
            DeleteRequested?.Invoke(this, new ClipCardActionEventArgs(item, asPlainText: false));
        }
    }

    private void RaiseActivate(bool asPlainText)
    {
        if (DataContext is ClipCardViewModel item)
        {
            ActivateRequested?.Invoke(this, new ClipCardActionEventArgs(item, asPlainText));
        }
    }

    private void RefreshThumbnail()
    {
        ResetThumbnail();
        if (!_isAttached
            || _topLevel?.IsVisible != true
            || !IsEffectivelyVisible
            || DataContext is not ClipCardViewModel item
            || !item.TryGetEncodedThumbnailData(out var encodedImage))
        {
            return;
        }

        PreviewStatusText.Text = AppLocalization.Text("正在加载…");
        var generation = _thumbnailGeneration;
        var cancellation = new CancellationTokenSource();
        _thumbnailCancellation = cancellation;
        _ = LoadThumbnailAsync(item, encodedImage, generation, cancellation.Token);
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            if (_topLevel?.IsVisible != true)
            {
                ResetThumbnail();
                return;
            }

            // The inherited effective-visibility state is propagated after the
            // top-level property notification, so resume on the next UI turn.
            Dispatcher.UIThread.Post(RefreshThumbnail);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _ = sender;
        PreviewStatusText.Text = AppLocalization.Text(
            _thumbnailCancellation is not null ? "正在加载…" : "图片预览不可用");
    }

    private async Task LoadThumbnailAsync(
        ClipCardViewModel item,
        byte[] encodedImage,
        int generation,
        CancellationToken cancellationToken)
    {
        BoundedLruCache<ThumbnailCacheKey, ThumbnailResource>.Lease? lease = null;
        try
        {
            lease = await SharedThumbnailLoader
                .AcquireAsync(item.Id, encodedImage, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (lease is null)
        {
            if (CanApplyThumbnail(item, generation, cancellationToken))
            {
                PreviewStatusText.Text = AppLocalization.Text("图片预览不可用");
            }

            return;
        }

        if (!CanApplyThumbnail(item, generation, cancellationToken))
        {
            lease.Dispose();
            return;
        }

        _thumbnailLease = lease;
        PreviewImage.Source = lease.Value.Image;
        PreviewImage.IsVisible = true;
        PreviewPlaceholder.IsVisible = false;
    }

    private bool CanApplyThumbnail(
        ClipCardViewModel item,
        int generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && generation == _thumbnailGeneration
        && _isAttached
        && IsEffectivelyVisible
        && ReferenceEquals(DataContext, item);

    private void ResetThumbnail()
    {
        _thumbnailGeneration++;

        var cancellation = _thumbnailCancellation;
        _thumbnailCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }

        PreviewImage.Source = null;
        PreviewImage.IsVisible = false;
        PreviewPlaceholder.IsVisible = true;
        PreviewStatusText.Text = AppLocalization.Text("图片预览不可用");

        _thumbnailLease?.Dispose();
        _thumbnailLease = null;
    }

}

public sealed class ClipCardActionEventArgs : EventArgs
{
    public ClipCardActionEventArgs(ClipCardViewModel item, bool asPlainText)
    {
        Item = item;
        AsPlainText = asPlainText;
    }

    public ClipCardViewModel Item { get; }

    public bool AsPlainText { get; }
}
