using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TuckClip.Windows.Services;

namespace TuckClip.Windows.Views;

internal sealed class UpdateWindow : Window
{
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;
    private readonly Button _installButton;
    private readonly Button _laterButton;
    private bool _installationStarted;
    private bool _allowClose;

    public UpdateWindow(IWindowsUpdateSession session, Action installRequested)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(installRequested);

        Title = AppLocalization.Text("TuckClip 更新");
        Width = 520;
        Height = 360;
        MinWidth = 420;
        MinHeight = 300;
        CanResize = true;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var title = new TextBlock
        {
            Text = AppLocalization.Format("TuckClip {0} 已可用", session.VersionText),
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
        };
        var detail = new TextBlock
        {
            Text = AppLocalization.Text("下载完成后会安全替换当前版本并重新启动。"),
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
        };
        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(session.ReleaseNotes)
                ? AppLocalization.Text("此版本没有附加更新说明。")
                : session.ReleaseNotes,
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(notes, ScrollBarVisibility.Auto);
        _statusText = new TextBlock
        {
            Text = AppLocalization.Text("更新包会在安装前校验完整性。"),
            Foreground = Brushes.Gray,
        };
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsVisible = false,
        };
        _laterButton = new Button
        {
            Content = AppLocalization.Text("稍后"),
            MinWidth = 88,
        };
        _laterButton.Click += (_, _) => Close();
        _installButton = new Button
        {
            Content = AppLocalization.Text("下载并安装"),
            MinWidth = 112,
        };
        _installButton.Click += (_, _) =>
        {
            if (_installationStarted)
            {
                return;
            }

            _installationStarted = true;
            _installButton.IsEnabled = false;
            _laterButton.IsEnabled = false;
            _progressBar.IsVisible = true;
            _statusText.Text = AppLocalization.Text("正在下载更新…");
            installRequested();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { _laterButton, _installButton },
        };
        Content = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            RowSpacing = 10,
            Children =
            {
                title,
                detail.WithGridRow(1),
                notes.WithGridRow(2),
                _statusText.WithGridRow(3),
                _progressBar.WithGridRow(4),
                buttons.WithGridRow(5),
            },
        };

        Closing += (_, eventArgs) =>
        {
            if (_installationStarted && !_allowClose)
            {
                eventArgs.Cancel = true;
            }
        };
    }

    public void CloseForApplicationExit()
    {
        _allowClose = true;
        Close();
    }

    public void ReportProgress(int progress)
    {
        var clamped = Math.Clamp(progress, 0, 100);
        _progressBar.Value = clamped;
        _statusText.Text = AppLocalization.Format("正在下载更新… {0}%", clamped);
    }

    public void ReportFailure(string message)
    {
        _installationStarted = false;
        _progressBar.IsVisible = false;
        _installButton.IsEnabled = true;
        _laterButton.IsEnabled = true;
        _statusText.Text = message;
        _statusText.Foreground = Brushes.OrangeRed;
    }
}

internal static class GridChildExtensions
{
    public static T WithGridRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
