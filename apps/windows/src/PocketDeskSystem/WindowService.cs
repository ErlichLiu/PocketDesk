// PID+句柄现查现用；移动/恢复必须读回匹配。通用全局字号按键不可靠，默认禁用。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;

namespace PocketDesk.Win;

public readonly record struct WindowLook(Native.RECT Frame, bool FullScreen, bool Minimized,
    SavedWindowPlacement? Placement = null, long? ProcessStartTimeUtcTicks = null);

public sealed class WindowService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private const int MaxTries = 10;

    public static bool Matches(Native.RECT a, Native.RECT b, int tolerance = 2) =>
        Math.Abs((long)a.Left - b.Left) < tolerance && Math.Abs((long)a.Top - b.Top) < tolerance
        && Math.Abs((long)a.Width - b.Width) < tolerance && Math.Abs((long)a.Height - b.Height) < tolerance;

    /// <summary>仅 IsWindow 不够：关闭后句柄可能被其他进程重用。</summary>
    public static bool Exists(WindowKey key)
    {
        if (!key.IsValid) return false;
        var hwnd = (IntPtr)key.Handle;
        return Native.IsWindow(hwnd) && Native.GetWindowThreadProcessId(hwnd, out var pid) != 0
            && key.MatchesOwner(pid);
    }

    public static long? ProcessStartTimeUtcTicks(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.StartTime.ToUniversalTime().Ticks; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
    }

    /// <summary>新记录另外核对进程出生时间，避免跨重启 PID 重用；旧记录仍核对 PID+句柄。</summary>
    public static bool MatchesIdentity(PhoneRecord record) => Exists(record.Key)
        && (record.ProcessStartTimeUtcTicks is not { } saved || ProcessStartTimeUtcTicks(record.Pid) == saved);

    public static bool IsShown(WindowKey key)
    {
        var hwnd = (IntPtr)key.Handle;
        return Exists(key) && Native.IsWindowVisible(hwnd) && !IsCloaked(hwnd) && !Native.IsIconic(hwnd);
    }

    public static WindowLook? Look(WindowKey key)
    {
        if (!Exists(key)) return null;
        var hwnd = (IntPtr)key.Handle;
        if (!Native.GetWindowRect(hwnd, out var rect)) return null;
        var placement = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement(hwnd, ref placement) || !Exists(key)) return null;
        // 和 SetWindowPos 使用同一套原始边框，不能混用 DWM 可见边框导致恢复漂移。
        return new WindowLook(rect, Native.IsZoomed(hwnd), Native.IsIconic(hwnd), ToSaved(placement),
            ProcessStartTimeUtcTicks(key.Pid));
    }

    public static (IReadOnlyList<WindowInfo> Windows, IReadOnlyList<long> FrontToBack) List()
    {
        var found = new List<WindowInfo>();
        var order = new List<long>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!IsListable(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out var rawPid);
            var pid = (int)rawPid;
            if (pid == Environment.ProcessId) return true;
            var key = new WindowKey(pid, hwnd.ToInt64());
            var exe = ProcessImagePath(pid);
            var app = exe is null ? ReadClassName(hwnd) : Path.GetFileNameWithoutExtension(exe);
            if (app is null || !Exists(key)) return true;
            found.Add(new WindowInfo(key, app, exe, ReadTitle(hwnd)));
            order.Add(hwnd.ToInt64());
            return true;
        }, IntPtr.Zero);
        return (found, order);
    }

    private static bool IsListable(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd)) return false;
        var style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        var exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        if ((exStyle & Native.WS_EX_TOOLWINDOW) != 0 && (exStyle & Native.WS_EX_APPWINDOW) == 0) return false;
        if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero || IsCloaked(hwnd)) return false;
        if (ReadTitle(hwnd).Length == 0 && (style & Native.WS_THICKFRAME) == 0) return false;
        return Native.GetWindowRect(hwnd, out var rect) && rect.Width > 80 && rect.Height > 60;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        Native.DwmGetWindowAttributeInt(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static string ReadTitle(IntPtr hwnd)
    {
        var length = Native.GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        var buffer = new StringBuilder(length + 1);
        Native.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    public static string ReadClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        Native.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string? ProcessImagePath(int pid)
    {
        var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { Native.CloseHandle(handle); }
    }

    /// <summary>兼容旧调用；UI 应使用 MoveAsync。返回非空必定表示实际匹配，而不是最后一个错误位置。</summary>
    public static Native.RECT? Move(WindowKey key, Native.RECT target) => MoveAsync(key, target).GetAwaiter().GetResult();

    public static Task<Native.RECT?> MoveAsync(WindowKey key, Native.RECT target) => Task.Run(async () =>
    {
        if (target.Width <= 0 || target.Height <= 0 || !Exists(key)) return (Native.RECT?)null;
        var hwnd = (IntPtr)key.Handle;
        if (Native.IsZoomed(hwnd) || Native.IsIconic(hwnd))
        {
            if (!Exists(key) || !ShowWindowAsync(hwnd, Native.SW_RESTORE)) return null;
            await Task.Delay(120).ConfigureAwait(false);
        }
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            if (!Exists(key)) return null;
            // SWP_ASYNCWINDOWPOS：不等待目标 App 的消息线程，避免无响应 App 卡死调度。
            if (!Native.SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | 0x4000)) return null;
            await Task.Delay(RetryDelay).ConfigureAwait(false);
            if (!Exists(key)) return null;
            if (Native.GetWindowRect(hwnd, out var now) && Matches(now, target)) return now;
        }
        Log.Warn($"窗口 {key} 挪不到 {target}，保留恢复记录");
        return null;
    });

    /// <summary>按原始 placement 恢复，读回 normal rect/状态/标记全部匹配才成功。</summary>
    public static Task<bool> RestorePlacementAsync(PhoneRecord record) => Task.Run(async () =>
    {
        if (!MatchesIdentity(record)) return false;
        if (record.AppliedZoom != 0)
        {
            Log.Warn($"{record.Label} 尚有字号恢复增量 {record.AppliedZoom}，不能安全自动还原，保留记录");
            return false;
        }
        if (record.Placement is not { } saved)
        {
            // 兼容旧记录。旧版没保留 normal rect；只能按原始帧和已有状态恢复。
            var target = new Native.RECT { Left = record.X, Top = record.Y,
                Right = record.X + record.Width, Bottom = record.Y + record.Height };
            if (await MoveAsync(record.Key, target).ConfigureAwait(false) is null) return false;
            var derived = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            if (!MatchesIdentity(record) || !Native.GetWindowPlacement((IntPtr)record.Handle, ref derived)) return false;
            // 先核对 Move 成功，再以此时工作区坐标重建尽可能完整的旧版恢复目标。
            // 最小化且原为最大化时保留 WPF_RESTORETOMAXIMIZED；同样走下面的完整读回核验。
            saved = ToSaved(derived) with
            {
                ShowCommand = record.WasMinimized ? Native.SW_SHOWMINIMIZED
                    : record.WasFullScreen ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOWNORMAL,
                Flags = record.WasMinimized && record.WasFullScreen ? 2 : 0,
            };
        }
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            if (!MatchesIdentity(record)) return false;
            var placement = FromSaved(saved);
            placement.flags |= 0x0004; // WPF_ASYNCWINDOWPLACEMENT。
            if (!SetWindowPlacement((IntPtr)record.Handle, ref placement)) return false;
            await Task.Delay(RetryDelay).ConfigureAwait(false);
            var got = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
            if (!MatchesIdentity(record)) return false;
            if (Native.GetWindowPlacement((IntPtr)record.Handle, ref got)
                && PlacementMatches(saved, ToSaved(got))) return true;
        }
        Log.Warn($"{record.Label} 的原始窗口状态未恢复到位，保留记录");
        return false;
    });

    public static bool PlacementMatches(SavedWindowPlacement expected, SavedWindowPlacement actual) =>
        expected.ShowCommand == actual.ShowCommand && (expected.Flags & 3) == (actual.Flags & 3)
        && expected.MinX == actual.MinX && expected.MinY == actual.MinY
        && expected.MaxX == actual.MaxX && expected.MaxY == actual.MaxY
        && Matches(new Native.RECT { Left = expected.Left, Top = expected.Top, Right = expected.Right, Bottom = expected.Bottom },
            new Native.RECT { Left = actual.Left, Top = actual.Top, Right = actual.Right, Bottom = actual.Bottom });

    private static SavedWindowPlacement ToSaved(Native.WINDOWPLACEMENT p) => new(p.flags, p.showCmd,
        p.ptMinPosition.X, p.ptMinPosition.Y, p.ptMaxPosition.X, p.ptMaxPosition.Y,
        p.rcNormalPosition.Left, p.rcNormalPosition.Top, p.rcNormalPosition.Right, p.rcNormalPosition.Bottom);

    private static Native.WINDOWPLACEMENT FromSaved(SavedWindowPlacement p) => new()
    {
        length = Marshal.SizeOf<Native.WINDOWPLACEMENT>(), flags = p.Flags, showCmd = p.ShowCommand,
        ptMinPosition = new Native.POINT { X = p.MinX, Y = p.MinY },
        ptMaxPosition = new Native.POINT { X = p.MaxX, Y = p.MaxY },
        rcNormalPosition = new Native.RECT { Left = p.Left, Top = p.Top, Right = p.Right, Bottom = p.Bottom },
    };

    // 限于本模块，避免改动其他 Agent 负责的 Native.cs。
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref Native.WINDOWPLACEMENT placement);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    public static void Restore(WindowKey key)
    {
        if (!Exists(key)) return;
        var hwnd = (IntPtr)key.Handle;
        if ((Native.IsZoomed(hwnd) || Native.IsIconic(hwnd)) && Exists(key)) ShowWindowAsync(hwnd, Native.SW_RESTORE);
    }

    public static void SetMinimized(WindowKey key, bool minimized)
    {
        if (!Exists(key)) return;
        var hwnd = (IntPtr)key.Handle;
        if (minimized && !Native.IsIconic(hwnd) && Exists(key)) ShowWindowAsync(hwnd, 6);
        else if (!minimized && Native.IsIconic(hwnd) && Exists(key)) ShowWindowAsync(hwnd, Native.SW_RESTORE);
    }

    public static void Focus(WindowKey key)
    {
        if (!Exists(key)) return;
        var hwnd = (IntPtr)key.Handle;
        if (Native.IsIconic(hwnd) && Exists(key)) ShowWindowAsync(hwnd, Native.SW_RESTORE);
        // 不挂接其他线程的输入队列：前台锁拒绝时宁可激活失败，不影响 UU 输入。
        if (!Exists(key)) return;
        Native.SetForegroundWindow(hwnd);
        if (Exists(key)) Native.BringWindowToTop(hwnd);
    }

    /// <summary>SendInput 无法原子保证接收窗口，更不能确认 App 接受了字号。禁用，不抢焦点、不发按键。</summary>
    public static bool Zoom(WindowKey key, int steps) => steps == 0;
    public static int ApplyZoom(WindowKey key, int steps) => 0;
    public static bool AutomaticZoomEnabled => false;

    public static Dictionary<WindowKey, Native.RECT> Snapshot()
    {
        var result = new Dictionary<WindowKey, Native.RECT>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || IsCloaked(hwnd)) return true;
            if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if (!Native.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0) return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            var key = new WindowKey((int)pid, hwnd.ToInt64());
            if (Exists(key)) result[key] = rect;
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static void Restore(Dictionary<WindowKey, Native.RECT> snapshot, WindowKey? except)
    {
        foreach (var (key, saved) in snapshot)
        {
            if (except is { } skip && skip == key) continue;
            if (!Exists(key)) continue;
            var now = Look(key);
            if (now is null || Matches(now.Value.Frame, saved)) continue;
            if (Move(key, saved) is not null) Log.Info($"系统挪了 {key} 的窗口，已放回 {saved}");
        }
    }
}
