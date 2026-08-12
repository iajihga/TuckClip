using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TuckClip.Windows.Controls;
using TuckClip.Windows.ViewModels;

namespace TuckClip.Windows.Views;

public sealed partial class MainWindow : Window
{
    private const double DesignMinimumWidth = 640;
    private const double DesignMinimumHeight = 330;
    private const double DesignMaximumWidth = 1080;
    private const double DesignMaximumHeight = 480;
    private const double WorkingAreaInset = 24;
    private const double AbsoluteMinimumWidth = 360;
    private const double AbsoluteMinimumHeight = 240;

    private bool _allowClose;
    private PixelRect? _lastConstrainedWorkingArea;
    private double _lastConstrainedScaling;

    public MainWindow()
        : this(new ClipboardPanelViewModel())
    {
    }

    public MainWindow(IClipboardUiActions actions)
        : this(new ClipboardPanelViewModel(actions))
    {
    }

    public MainWindow(ClipboardPanelViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Closing += OnWindowClosing;
        KeyDown += OnWindowKeyDown;
        AddHandler(KeyDownEvent, OnPreviewShortcutKeyDown, RoutingStrategies.Tunnel);
        Activated += OnWindowActivated;
        Opened += (_, _) => ConstrainToCurrentScreen(force: true);
        PositionChanged += (_, _) => ConstrainToCurrentScreen(force: false);
        ScalingChanged += (_, _) => ConstrainToCurrentScreen(force: true);
    }

    public ClipboardPanelViewModel ViewModel { get; }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        ViewModel.Dispose();
        Close();
    }

    private void OnWindowActivated(object? sender, EventArgs e) => FocusSearch();

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose
            || e.CloseReason is WindowCloseReason.ApplicationShutdown
                or WindowCloseReason.OSShutdown
                or WindowCloseReason.OwnerWindowClosing)
        {
            return;
        }

        e.Cancel = true;
        HidePanel();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        HandlePanelShortcut(e);
    }

    private void OnPreviewShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        // TextBox consumes Shift+Delete. Intercept only that exact gesture and
        // leave arrows/Enter/Escape to bubble after IME and text editing.
        if (!ReferenceEquals(e.Source, SearchBox)
            || e.Key != Key.Delete
            || e.KeyModifiers != KeyModifiers.Shift)
        {
            return;
        }

        if (SearchBox.SelectionStart != SearchBox.SelectionEnd)
        {
            // Preserve Windows' standard Shift+Delete cut gesture when the
            // user has explicitly selected search text.
            return;
        }

        ViewModel.DeleteSelected();
        e.Handled = true;
    }

    private void HandlePanelShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            HidePanel();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.MoveSelection(-1);
            ScrollSelectionIntoView();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.MoveSelection(1);
            ScrollSelectionIntoView();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Control)
        {
            ViewModel.PasteSelected(asPlainText: e.KeyModifiers == KeyModifiers.Control);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            ViewModel.TogglePinSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.Shift)
        {
            ViewModel.DeleteSelected();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control
            && TryGetShortcutIndex(e.Key, out var shortcutIndex))
        {
            ViewModel.ActivateVisibleItem(shortcutIndex);
            e.Handled = true;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ClearSearch();
        FocusSearch();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) =>
        ViewModel.OpenSettingsCommand.Execute(null);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => HidePanel();

    private void OnAllFilterClick(object? sender, RoutedEventArgs e) => SetFilter(ClipTypeFilter.All);

    private void OnTextFilterClick(object? sender, RoutedEventArgs e) => SetFilter(ClipTypeFilter.Text);

    private void OnLinkFilterClick(object? sender, RoutedEventArgs e) => SetFilter(ClipTypeFilter.Link);

    private void OnImageFilterClick(object? sender, RoutedEventArgs e) => SetFilter(ClipTypeFilter.Image);

    private void OnFilesFilterClick(object? sender, RoutedEventArgs e) => SetFilter(ClipTypeFilter.Files);

    private void OnCardActivateRequested(object? sender, ClipCardActionEventArgs e)
    {
        ViewModel.SelectedItem = e.Item;
        ViewModel.PasteSelected(e.AsPlainText);
    }

    private void OnCardTogglePinRequested(object? sender, ClipCardActionEventArgs e)
    {
        ViewModel.SelectedItem = e.Item;
        ViewModel.TogglePin(e.Item);
    }

    private void OnCardDeleteRequested(object? sender, ClipCardActionEventArgs e)
    {
        ViewModel.SelectedItem = e.Item;
        ViewModel.Delete(e.Item);
    }

    private void SetFilter(ClipTypeFilter filter)
    {
        ViewModel.SelectFilter(filter);
        ScrollSelectionIntoView();
        FocusSearch();
    }

    private void HidePanel()
    {
        ViewModel.HidePanelCommand.Execute(null);
        Hide();
    }

    private void ScrollSelectionIntoView()
    {
        if (ViewModel.SelectedItem is not null)
        {
            CardList.ScrollIntoView(ViewModel.SelectedItem);
        }
    }

    private void ConstrainToCurrentScreen(bool force)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        if (!force
            && _lastConstrainedWorkingArea == screen.WorkingArea
            && _lastConstrainedScaling.Equals(screen.Scaling))
        {
            return;
        }

        _lastConstrainedWorkingArea = screen.WorkingArea;
        _lastConstrainedScaling = screen.Scaling;

        var constraints = CalculateWindowConstraints(
            screen.WorkingArea.Size,
            screen.Scaling,
            Width,
            Height);
        MinWidth = constraints.MinimumWidth;
        MinHeight = constraints.MinimumHeight;
        MaxWidth = constraints.MaximumWidth;
        MaxHeight = constraints.MaximumHeight;
        Width = constraints.Width;
        Height = constraints.Height;

        var physicalWidth = (int)Math.Ceiling(constraints.Width * screen.Scaling);
        var physicalHeight = (int)Math.Ceiling(constraints.Height * screen.Scaling);
        var maximumX = Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - physicalWidth);
        var maximumY = Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - physicalHeight);
        var constrainedPosition = new PixelPoint(
            Math.Clamp(Position.X, screen.WorkingArea.X, maximumX),
            Math.Clamp(Position.Y, screen.WorkingArea.Y, maximumY));
        if (constrainedPosition != Position)
        {
            Position = constrainedPosition;
        }
    }

    internal static WindowSizeConstraints CalculateWindowConstraints(
        PixelSize workingArea,
        double scaling,
        double requestedWidth,
        double requestedHeight)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        var availableWidth = Math.Max(
            AbsoluteMinimumWidth,
            (workingArea.Width / safeScaling) - WorkingAreaInset);
        var availableHeight = Math.Max(
            AbsoluteMinimumHeight,
            (workingArea.Height / safeScaling) - WorkingAreaInset);
        var minimumWidth = Math.Min(DesignMinimumWidth, availableWidth);
        var minimumHeight = Math.Min(DesignMinimumHeight, availableHeight);
        var maximumWidth = Math.Max(minimumWidth, Math.Min(DesignMaximumWidth, availableWidth));
        var maximumHeight = Math.Max(minimumHeight, Math.Min(DesignMaximumHeight, availableHeight));
        var finiteRequestedWidth = double.IsFinite(requestedWidth) ? requestedWidth : DesignMaximumWidth;
        var finiteRequestedHeight = double.IsFinite(requestedHeight) ? requestedHeight : DesignMinimumHeight;

        return new WindowSizeConstraints(
            Math.Clamp(finiteRequestedWidth, minimumWidth, maximumWidth),
            Math.Clamp(finiteRequestedHeight, minimumHeight, maximumHeight),
            minimumWidth,
            minimumHeight,
            maximumWidth,
            maximumHeight);
    }

    private static bool TryGetShortcutIndex(Key key, out int index)
    {
        index = key switch
        {
            Key.D1 => 1,
            Key.D2 => 2,
            Key.D3 => 3,
            Key.D4 => 4,
            Key.D5 => 5,
            Key.D6 => 6,
            Key.D7 => 7,
            Key.D8 => 8,
            Key.D9 => 9,
            Key.NumPad1 => 1,
            Key.NumPad2 => 2,
            Key.NumPad3 => 3,
            Key.NumPad4 => 4,
            Key.NumPad5 => 5,
            Key.NumPad6 => 6,
            Key.NumPad7 => 7,
            Key.NumPad8 => 8,
            Key.NumPad9 => 9,
            _ => 0,
        };
        return index != 0;
    }
}

internal readonly record struct WindowSizeConstraints(
    double Width,
    double Height,
    double MinimumWidth,
    double MinimumHeight,
    double MaximumWidth,
    double MaximumHeight);
