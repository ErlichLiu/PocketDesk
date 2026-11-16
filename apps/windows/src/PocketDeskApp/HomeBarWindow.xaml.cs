using System.Windows;
using PocketDesk.Win;

namespace PocketDesk.App;

public partial class HomeBarWindow : PhonePanel
{
    public event Action? Tapped;
    public event Action? RestoreRequested;
    public HomeBarWindow() => InitializeComponent();
    public void FitTo(Native.RECT bounds) => ShowAt(bounds);
    private void OnTap(object sender, RoutedEventArgs e) => Tapped?.Invoke();
    private void OnRestore(object sender, RoutedEventArgs e) => RestoreRequested?.Invoke();
}
