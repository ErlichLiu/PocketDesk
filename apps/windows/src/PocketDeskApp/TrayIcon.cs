// 托盘：PocketDesk 常驻这里。菜单只有几项，和 macOS 版的菜单栏一致。
namespace PocketDesk.App;

using System.Drawing;
using System.Windows.Forms;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action openSettings, Action togglePhoneScreen, Action quit, Func<bool> phoneScreenEnabled, Func<string> statusText)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("设置…", null, (_, _) => openSettings());

        var toggle = new ToolStripMenuItem("手机屏") { CheckOnClick = false };
        toggle.Click += (_, _) =>
        {
            togglePhoneScreen();
            toggle.Checked = phoneScreenEnabled();
        };
        menu.Items.Add(toggle);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 PocketDesk", null, (_, _) => quit());
        menu.Opening += (_, _) =>
        {
            toggle.Checked = phoneScreenEnabled();
            if (_icon is not null) _icon.Text = Truncate(statusText(), 63);
        };

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "PocketDesk",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => openSettings();
    }

    public void SetStatus(string text) => _icon.Text = Truncate(text, 63);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
