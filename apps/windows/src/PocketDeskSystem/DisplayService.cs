// 显示器坐标统一为物理像素。只调整明确识别的非主虚拟屏，不更改主屏或桌面拓扑。
using System.Runtime.InteropServices;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;

namespace PocketDesk.Win;

public sealed record DisplayInfo(IntPtr Handle, string DeviceName, string AdapterName, string MonitorName,
    Native.RECT Bounds, Native.RECT Work, bool IsPrimary, bool IsPhoneScreen)
{
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;
    public override string ToString() => $"{AdapterName} · {DeviceName} · {Width}×{Height}";
}

public sealed class DisplayService
{
    public const string PhoneScreenName = "PocketDesk Phone";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    // DEVMODEW 精确布局，避免未初始化缓冲区及 dmSize 缺失导致不可预测的模式读取。
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    public struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(76)] public int X;
        [FieldOffset(80)] public int Y;
        [FieldOffset(168)] public uint BitsPerPixel;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
        [FieldOffset(184)] public uint Frequency;
        public static DisplayMode Create() => new() { Size = 220 };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsExW")]
    private static extern bool EnumDisplaySettingsEx(string device, int index, ref DisplayMode mode, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsEx(string device, ref DisplayMode mode, IntPtr hwnd, uint flags, IntPtr param);

    public static IReadOnlyList<DisplayInfo> Monitors()
    {
        var result = new List<DisplayInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr handle, IntPtr hdc, ref Native.RECT rect, IntPtr data) =>
            {
                var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
                if (!Native.GetMonitorInfo(handle, ref info)) return true;
                var adapter = "";
                var monitor = "";
                for (uint i = 0; i < 128; i++)
                {
                    var device = NewDevice();
                    if (!EnumDisplayDevices(null, i, ref device, 0)) break;
                    if (!string.Equals(device.DeviceName, info.szDevice, StringComparison.OrdinalIgnoreCase)) continue;
                    adapter = device.DeviceString;
                    var child = NewDevice();
                    if (EnumDisplayDevices(info.szDevice, 0, ref child, 0)) monitor = child.DeviceString;
                    break;
                }
                var primary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                var identified = adapter.Contains("PocketDesk", StringComparison.OrdinalIgnoreCase)
                    || adapter.Equals("Virtual Display Driver", StringComparison.OrdinalIgnoreCase)
                    || adapter.Contains("MttVDD", StringComparison.OrdinalIgnoreCase)
                    || monitor.Contains("PocketDesk Phone", StringComparison.OrdinalIgnoreCase);
                result.Add(new DisplayInfo(handle, info.szDevice, adapter, monitor, info.rcMonitor, info.rcWork,
                    primary, identified && !primary));
                return true;
            }, IntPtr.Zero);
        return result;
    }

    // 多块通用 VDD 屏时不盲选，避免操作用户其它显示器。
    public static DisplayInfo? PhoneScreen()
    {
        var candidates = Monitors().Where(m => m.IsPhoneScreen).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static Native.RECT WindowArea(DisplayInfo display, int homeBarHeight) => new()
    {
        Left = Math.Max(display.Bounds.Left, display.Work.Left),
        Top = Math.Max(display.Bounds.Top, display.Work.Top),
        Right = Math.Min(display.Bounds.Right, display.Work.Right),
        Bottom = Math.Max(display.Work.Top + 1, Math.Min(display.Bounds.Bottom, display.Work.Bottom) - homeBarHeight),
    };

    public static IReadOnlyList<(int Width, int Height)> AvailableModes(DisplayInfo display)
    {
        var modes = new HashSet<(int, int)>();
        for (var i = 0; i < 4096; i++)
        {
            var mode = DisplayMode.Create();
            if (!EnumDisplaySettingsEx(display.DeviceName, i, ref mode, 0)) break;
            if (mode.Width > 0 && mode.Height > 0) modes.Add(((int)mode.Width, (int)mode.Height));
        }
        return modes.OrderBy(m => m.Item1).ThenBy(m => m.Item2).ToArray();
    }

    public static bool SetWidth(DisplayInfo display, ScreenProfile profile, int width)
    {
        if (!display.IsPhoneScreen || display.IsPrimary) return false;
        var fresh = PhoneScreen();
        if (fresh is null || fresh.DeviceName != display.DeviceName) return false;
        var wanted = profile.Size(width);
        var available = AvailableModes(fresh);
        // 只在驱动确实上报的档位中选择相同比例且不小于所需宽度的模式。
        var selected = SelectSupportedMode(profile, width, available);
        if (selected is null)
        {
            Log.Warn($"驱动没有「{profile.Name}」的可用分辨率；保留当前显示设置");
            return false;
        }
        var mode = DisplayMode.Create();
        if (!EnumDisplaySettingsEx(fresh.DeviceName, -1, ref mode, 0)) return false;
        if (mode.Width == selected.Value.Width && mode.Height == selected.Value.Height) return true;
        mode.Width = (uint)selected.Value.Width;
        mode.Height = (uint)selected.Value.Height;
        // 不带位置、方向、频率等无关字段，也不持久改系统注册表。
        mode.Fields = 0x00080000 | 0x00100000;
        var test = ChangeDisplaySettingsEx(fresh.DeviceName, ref mode, IntPtr.Zero, 2, IntPtr.Zero);
        if (test != 0) { Log.Warn($"模式测试失败：{test}"); return false; }
        var applied = ChangeDisplaySettingsEx(fresh.DeviceName, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
        if (applied != 0) { Log.Warn($"模式切换失败：{applied}"); return false; }
        var verify = DisplayMode.Create();
        var ok = EnumDisplaySettingsEx(fresh.DeviceName, -1, ref verify, 0)
            && verify.Width == mode.Width && verify.Height == mode.Height;
        Log.Info($"虚拟屏模式 {mode.Width}×{mode.Height}，回读{(ok ? "一致" : "不一致")}");
        return ok;
    }

    public static (int Width, int Height)? SelectSupportedMode(ScreenProfile profile, int width,
        IReadOnlyList<(int Width, int Height)> modes)
    {
        var compatible = modes.Where(m => m.Width >= profile.BaseWidth && m.Width <= profile.MaxWidth
            && Math.Abs(m.Height - profile.Height(m.Width)) <= 1).OrderBy(m => m.Width).ToArray();
        foreach (var mode in compatible) if (mode.Width >= width) return mode;
        return compatible.Length > 0 ? compatible[^1] : null;
    }

    private static DISPLAY_DEVICE NewDevice() => new()
    {
        cb = Marshal.SizeOf<DISPLAY_DEVICE>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "",
    };
}
