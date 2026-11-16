// Win32 互操作声明。集中在一个文件里，其余模块只调用这里的包装，不各自写 DllImport。
// 坐标一律是「物理像素、左上原点、虚拟桌面坐标」：进程在启动时设成 per-monitor DPI aware v2，
// 系统不会替我们缩放窗口矩形，跨 DPI 屏搬窗口时算出来的就是真实位置。
using System.Runtime.InteropServices;
using System.Text;

namespace PocketDesk.Win;

public static class Native
{
    // ───────────────────────── 显示器 ─────────────────────────

    public const uint MONITORINFOF_PRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public override string ToString() => $"({Left},{Top} {Width}x{Height})";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    // ───────────────────────── DPI ─────────────────────────

    public enum DPI_AWARENESS_CONTEXT
    {
        UNAWARE = -1,
        SYSTEM_AWARE = -2,
        PER_MONITOR_AWARE = -3,
        PER_MONITOR_AWARE_V2 = -4,
        UNAWARE_GDISCALED = -5,
    }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT context);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ───────────────────────── 窗口 ─────────────────────────

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const int WS_MAXIMIZE = 0x01000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_APPWINDOW = 0x00040000;

    public const uint GW_OWNER = 4;
    public const uint GW_HWNDPREV = 3;
    public const uint GW_HWNDNEXT = 2;

    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE = 0;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_SHOWNORMAL = 1;

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

    // ───────────────────────── DWM ─────────────────────────

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // 两个重载共用一个导出名，EntryPoint 必须显式给出
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeInt(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    public static extern int DwmGetWindowAttributeRect(IntPtr hWnd, int attribute, out RECT value, int size);

    // ───────────────────────── 进程 ─────────────────────────

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string? name);

    // ───────────────────────── 输入钩子 ─────────────────────────

    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;

    public const uint LLMHF_INJECTED = 0x00000001;
    public const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    // ───────────────────────── 消息泵（低级钩子所在线程必须跑） ─────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG message, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    public const uint WM_QUIT = 0x0012;

    // ───────────────────────── 输入注入 ─────────────────────────

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x1000;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_OEM_PLUS = 0xBB;
    public const ushort VK_OEM_MINUS = 0xBD;
    public const ushort VK_ADD = 0x6B;
    public const ushort VK_SUBTRACT = 0x6D;

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
