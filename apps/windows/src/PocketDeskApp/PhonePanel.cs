// WPF 的 DIP 会随跨屏 DPI 改变，显示后和 WM_DPICHANGED 后都重新按物理像素定位。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PocketDesk.Win;

namespace PocketDesk.App;

public class PhonePanel : Window
{
    private Native.RECT _target;
    private bool _hasTarget;
    private bool _scheduled;

    public PhonePanel()
    {
        ShowActivated = false;
        Topmost = true;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLong(handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(handle, Native.GWL_EXSTYLE, style | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        };
    }

    protected void ShowAt(Native.RECT target)
    {
        _target = target;
        _hasTarget = true;
        if (!IsVisible) Show();
        ApplyBounds();
        ScheduleBounds();
    }

    private void ScheduleBounds()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _scheduled = false;
            if (IsVisible) ApplyBounds();
        }));
    }

    private void ApplyBounds()
    {
        if (!_hasTarget) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(hwnd, new IntPtr(-1), _target.Left, _target.Top, _target.Width, _target.Height,
            Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE：按钮可点但不抢键盘
        if (message == 0x02E0) ScheduleBounds(); // WM_DPICHANGED
        return IntPtr.Zero;
    }
}
