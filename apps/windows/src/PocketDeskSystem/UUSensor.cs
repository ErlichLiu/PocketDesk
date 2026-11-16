// UU 连接状态的实验观测，不接入默认生命周期。
// 覆盖窗口和 injected 输入既不能证明控制端身份，也不能可靠证明断连。
// .slog 目前无法解析，编码/保护方式未确认，CaptureActive 保持未知。
namespace PocketDesk.Win;

using PocketDesk.Core;
using PocketDesk.Core.Session;

public sealed class UUSensor : IDisposable
{
    /// <summary>注入事件在这个窗口内算「有人正在操作」。</summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromSeconds(3);

    private DateTimeOffset _lastActivityAt = DateTimeOffset.MinValue;

    /// <summary>记录一次 UU 注入的输入（由 InputHook 调用）。</summary>
    public void NoteInjectedInput(DateTimeOffset at) => _lastActivityAt = at;

    public UUSignals Sample(DateTimeOffset now)
    {
        var activity = _lastActivityAt > DateTimeOffset.MinValue && now - _lastActivityAt <= ActivityWindow;

        return new UUSignals(
            OverlayVisible: OverlayVisible() || activity,
            Controllers: Array.Empty<string>(),
            CaptureActive: null);
    }

    /// <summary>
    /// UU 被控时放在本机屏幕上的提示窗口。
    /// 未连接时 GameViewerServer 的窗口全不可见（GDI+ Hook Window、HiddenWindow、IME），所以「可见」就是信号。
    /// </summary>
    private static bool OverlayVisible()
    {
        var found = false;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            if (!IsServerWindow(hwnd)) return true;

            var className = WindowService.ReadClassName(hwnd);
            if (className.Contains("IME", StringComparison.OrdinalIgnoreCase)) return true;
            if (className.Contains("GDI+ Hook", StringComparison.OrdinalIgnoreCase)) return true;
            found = true;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsServerWindow(IntPtr hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var path = WindowService.ProcessImagePath((int)pid);
        return path is not null
               && Path.GetFileNameWithoutExtension(path).Equals("GameViewerServer", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
    }
}
