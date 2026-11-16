using System.Windows;
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

namespace PocketDesk.App;

public sealed record WindowRow(WindowInfo Window)
{
    public string Title => string.IsNullOrWhiteSpace(Window.Title) ? Window.App : Window.Title;
    public string App => string.Equals(Title, Window.App, StringComparison.Ordinal) ? "" : Window.App;
    public string Initial => Window.App.Length > 0 ? Window.App[..1].ToUpperInvariant() : "?";
}

public partial class PhoneListWindow : PhonePanel
{
    public event Action<WindowInfo>? WindowChosen;
    public event Action? ReturnRequested;
    public event Action? RestoreRequested;
    public event Action? SettingsRequested;
    public PhoneListWindow() => InitializeComponent();

    public void FitTo(Native.RECT bounds)
    {
        DesignRoot.Height = bounds.Height * 540.0 / bounds.Width;
        ShowAt(bounds);
    }

    public void SetState(DeskState state, ScreenProfile profile, string notice)
    {
        ProfileLabel.Text = profile.Id == "fold" ? "OPPO Find N5 · 展开模式" : profile.Name;
        PlacedActions.Visibility = state.Placed is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeLabel.Text = notice;
    }

    public void ShowRows(IReadOnlyList<WindowInfo> windows)
    {
        WindowItems.ItemsSource = windows.Select(w => new WindowRow(w)).ToArray();
        EmptyLabel.Visibility = windows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = "没有可用窗口。请先在电脑上打开一个应用。";
    }
    private void OnWindowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WindowRow row }) WindowChosen?.Invoke(row.Window);
    }
    private void OnReturnClick(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke();
    private void OnRestoreClick(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
