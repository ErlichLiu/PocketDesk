// 设置页：状态一眼可见（手机屏在不在、UU 连没连上），开关与默认字号就地改。
namespace PocketDesk.App;

using System.Diagnostics;
using System.Windows;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private bool _loading = true;

    public SettingsWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;

        EnabledCheck.IsChecked = controller.PhoneScreenEnabled;
        foreach (var profile in ScreenProfile.All) ProfileCombo.Items.Add(profile);
        ProfileCombo.DisplayMemberPath = "Name";
        ProfileCombo.SelectedItem = ScreenProfile.FromId(controller.ProfileId);
        _loading = false;

        controller.StatusChanged += OnStatus;
        OnStatus();
        Closed += (_, _) => controller.StatusChanged -= OnStatus;
    }

    private void OnStatus()
    {
        StatusText.Text = _controller.StatusText;
        ScreenText.Text = _controller.ScreenText;
        NoticeText.Text = _controller.NoticeText;
        EnabledCheck.IsChecked = _controller.PhoneScreenEnabled;
        _loading = true;
        ProfileCombo.SelectedItem = ScreenProfile.FromId(_controller.ProfileId);
        _loading = false;
    }

    private void OnToggleEnabled(object sender, RoutedEventArgs e) =>
        _controller.SetPhoneScreenEnabled(EnabledCheck.IsChecked == true);

    private void OnProfileChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || ProfileCombo.SelectedItem is not ScreenProfile profile) return;
        _controller.SetProfile(profile);
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"打不开日志：{ex.Message}", "PocketDesk");
        }
    }

    private void OnQuit(object sender, RoutedEventArgs e) => _controller.Quit();
}
