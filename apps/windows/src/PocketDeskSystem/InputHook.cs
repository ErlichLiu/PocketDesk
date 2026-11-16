// 低级钩子只能看出 injected，不能证明输入来自 UU。默认只观察，绝不重映射。
// 实验模式需要上层明确手动启用；物理鼠标/触控板/无标志的虚拟 HID 永远放行。
using System.Diagnostics;
using System.Runtime.InteropServices;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;

namespace PocketDesk.Win;

public sealed class InputHook : IDisposable
{
    private static readonly IntPtr Marker = (IntPtr)0x5044_4553;
    private const uint TickMessage = 0x8001;
    private readonly TouchTranslator _translator = new();
    private readonly InputCaptureGate _gate = new();
    private readonly Native.LowLevelMouseProc _callback; // 保活 unmanaged 回调。
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly global::System.Threading.Timer _timer;
    private readonly Func<bool> _shouldCapture;
    private readonly Func<Native.RECT?> _captureRegion;
    private sealed record Context(bool Active, CaptureRegion? Region, long SampledAt);
    private Context _context = new(false, null, 0);
    private IntPtr _hook;
    private uint _threadId;
    private int _shutdown, _enabled, _sampling, _tickPending, _notificationPending;
    private long _lastSample, _lastFrame;
    // 以下状态仅钩子消息泵线程访问，包括惯性和自发按键配对。
    private bool _syntheticLeftDown, _syntheticRightDown;
    private CaptureRegion? _gestureRegion;

    /// <summary>未知来源的注入输入活动；不是 UU/连接证明。在线程池异步、合并通知，勿直接访问 UI。</summary>
    public event Action? InjectedInput;

    /// <param name="experimentalRemappingEnabled">仅供明确手动选择；不得从自动连接判断直接启用。</param>
    public InputHook(Func<bool> shouldCapture, Func<Native.RECT?> captureRegion, bool experimentalRemappingEnabled = false)
    {
        _shouldCapture = shouldCapture ?? throw new ArgumentNullException(nameof(shouldCapture));
        _captureRegion = captureRegion ?? throw new ArgumentNullException(nameof(captureRegion));
        _enabled = experimentalRemappingEnabled ? 1 : 0;
        _callback = OnMouse;
        // 先初始化 timer，避免装钩子后早到的输入访问尚未构造的 timer。
        _timer = new global::System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
        _thread = new Thread(Run) { IsBackground = true, Name = "PocketDesk 输入观察（手势默认关闭）" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        var ready = _ready.Wait(TimeSpan.FromSeconds(2));
        if (!ready || IsRunning) _timer.Change(0, 16);
        else _timer.Dispose();
    }

    public bool IsRunning => Volatile.Read(ref _hook) != IntPtr.Zero;
    /// <summary>危险的手动实验开关：其他软件的 injected 输入也可能被接管；不能识别 UU 来源。</summary>
    public bool ExperimentalRemappingEnabled
    {
        get => Volatile.Read(ref _enabled) != 0 && Volatile.Read(ref _shutdown) == 0;
        set { Volatile.Write(ref _enabled, value ? 1 : 0); PostTick(); }
    }

    private void Run()
    {
        try
        {
            _threadId = Native.GetCurrentThreadId();
            // 显式创建线程消息队列，避免 Dispose 的 WM_QUIT 早于 GetMessage 而丢失。
            HookNative.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _callback, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            { Log.Error($"输入观察装钩子失败（{Marshal.GetLastWin32Error()}）"); return; }
            Log.Info("输入观察已启动；注入标志不能识别 UU，手势重映射需手动实验启用");
            _ready.Set();
            while (Volatile.Read(ref _shutdown) == 0)
            {
                var result = Native.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result <= 0) break;
                if (message.message == TickMessage)
                {
                    Interlocked.Exchange(ref _tickPending, 0);
                    AdvanceMomentum();
                }
                else { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
            }
        }
        catch (Exception ex) { Log.Error($"输入观察退出：{ex.Message}"); }
        finally
        {
            // 在所属线程释放，并平衡最后一次已重放 down；绝不把光标移回旧区域。
            ReleaseSyntheticButton();
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _ready.Set();
        }
    }

    // Timer 不接触状态机/SendInput；所有逻辑通过消息泵串行。提供者和订阅者都不在钩子回调执行。
    private void Tick(object? state)
    {
        if (Volatile.Read(ref _shutdown) != 0) return;
        PostTick();
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastSample) < 64 || Interlocked.Exchange(ref _sampling, 1) != 0) return;
        try
        {
            Interlocked.Exchange(ref _lastSample, now);
            var active = _shouldCapture();
            var rect = active ? _captureRegion() : null;
            var region = rect is { } r ? new CaptureRegion(r.Left, r.Top, r.Right, r.Bottom) : (CaptureRegion?)null;
            // 只有完整、有效、足够新的快照才可接管。阻塞提供者不能长期保留旧区域。
            Volatile.Write(ref _context, new Context(active && region is { IsValid: true }, region, now));
        }
        catch { Volatile.Write(ref _context, new Context(false, null, now)); }
        finally { Volatile.Write(ref _sampling, 0); }
    }

    private void PostTick()
    {
        if (Volatile.Read(ref _shutdown) != 0 || _threadId == 0 || Interlocked.Exchange(ref _tickPending, 1) != 0) return;
        if (!Native.PostThreadMessage(_threadId, TickMessage, IntPtr.Zero, IntPtr.Zero)) Interlocked.Exchange(ref _tickPending, 0);
    }

    private Context CurrentContext()
    {
        var context = Volatile.Read(ref _context);
        return Environment.TickCount64 - context.SampledAt <= 500 ? context : new Context(false, null, 0);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        // 异常不得越过 unmanaged 回调边界；故障立即关闭实验模式并放行。
        try { return HandleMouse(code, wParam, lParam); }
        catch
        {
            Volatile.Write(ref _enabled, 0);
            _gate.Cancel();
            try { CancelGesture(); } catch { }
            return Next(code, wParam, lParam);
        }
    }

    private IntPtr HandleMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || Volatile.Read(ref _shutdown) != 0) return Next(code, wParam, lParam);
        var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
        if (info.dwExtraInfo == Marker) return Next(code, wParam, lParam);
        var injected = (info.flags & Native.LLMHF_INJECTED) != 0;
        if (injected) NotifyInjectedActivity();
        var kind = (int)wParam switch
        {
            Native.WM_LBUTTONDOWN => PointerInputKind.LeftDown,
            Native.WM_LBUTTONUP => PointerInputKind.LeftUp,
            Native.WM_MOUSEMOVE => PointerInputKind.Move,
            _ => PointerInputKind.Other,
        };
        var context = CurrentContext();
        var decision = _gate.Evaluate(kind, injected, false, info.pt.X, info.pt.Y,
            ExperimentalRemappingEnabled, context.Active, context.Region);
        if (decision.CancelGesture) CancelGesture();
        // 本机输入、现成滚轮/其他按钮或离开区域立即打断惯性，但始终放行它们。
        if (!injected || kind == PointerInputKind.Other ||
            (kind == PointerInputKind.Move && !decision.Translate) ||
            context.Region is not { } r || !r.Contains(info.pt.X, info.pt.Y))
            _translator.StopCoasting();
        if (decision.Translate)
        {
            if (kind == PointerInputKind.LeftDown) _gestureRegion = context.Region;
            var touchKind = kind == PointerInputKind.LeftDown ? TouchKind.Press : kind == PointerInputKind.LeftUp ? TouchKind.Release : TouchKind.Drag;
            Perform(_translator.Handle(new TouchInput(touchKind, info.pt.X, info.pt.Y, Seconds()), true));
            if (_translator.IsCoasting) _lastFrame = Stopwatch.GetTimestamp();
        }
        return decision.Consume ? (IntPtr)1 : Next(code, wParam, lParam);
    }

    private void NotifyInjectedActivity()
    {
        if (InjectedInput is null || Interlocked.Exchange(ref _notificationPending, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (Volatile.Read(ref _shutdown) != 0) return;
                if (InjectedInput is { } handlers)
                    foreach (Action handler in handlers.GetInvocationList())
                        try { handler(); } catch { /* 订阅者不能使输入线程退出。 */ }
            }
            finally { Volatile.Write(ref _notificationPending, 0); }
        });
    }

    private void AdvanceMomentum()
    {
        var context = CurrentContext();
        if (!ExperimentalRemappingEnabled || !context.Active || context.Region != _gestureRegion)
        { _gate.Cancel(); CancelGesture(); return; }
        if (!_translator.IsCoasting) return;
        if (!Native.GetCursorPos(out var cursor) || context.Region is not { } region || !region.Contains(cursor.X, cursor.Y))
        { _translator.StopCoasting(); return; }
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastFrame) / (double)Stopwatch.Frequency;
        _lastFrame = now;
        if (_translator.MomentumFrame(elapsed) is { } step) SendScroll(step.DeltaY, step.DeltaX, step.X, step.Y);
    }

    private void CancelGesture()
    {
        _translator.Cancel();
        ReleaseSyntheticButton();
        _gestureRegion = null;
    }

    private void Perform(TouchResponse response)
    {
        foreach (var command in response.Commands)
        {
            switch (command.Kind)
            {
                case TouchCommandKind.Scroll: SendScroll(command.DeltaY, command.DeltaX, command.X, command.Y); break;
                case TouchCommandKind.ReplayPress:
                    if (!_syntheticLeftDown) _syntheticLeftDown = Send(Positioned(Native.MOUSEEVENTF_LEFTDOWN, command.X, command.Y));
                    break;
                case TouchCommandKind.ReplayMove:
                    if (_syntheticLeftDown) Send(Positioned(0, command.X, command.Y));
                    break;
                case TouchCommandKind.ReplayRelease:
                    if (_syntheticLeftDown) Send(Positioned(0, command.X, command.Y));
                    ReleaseSyntheticButton();
                    break;
                case TouchCommandKind.ReplayCurrent:
                    if (Send(Positioned(Native.MOUSEEVENTF_LEFTDOWN, command.X, command.Y)))
                    { _syntheticLeftDown = true; ReleaseSyntheticButton(); }
                    break;
                case TouchCommandKind.RightClick:
                    if (Send(Positioned(Native.MOUSEEVENTF_RIGHTDOWN, command.X, command.Y)))
                    { _syntheticRightDown = true; ReleaseSyntheticButton(); }
                    break;
            }
        }
    }

    private void ReleaseSyntheticButton()
    {
        // 不带 MOVE：取消/关闭时不能把光标拉回已经失效的区域。
        if (_syntheticLeftDown && Send(Button(Native.MOUSEEVENTF_LEFTUP))) _syntheticLeftDown = false;
        if (_syntheticRightDown && Send(Button(Native.MOUSEEVENTF_RIGHTUP))) _syntheticRightDown = false;
        // SendInput 可能因 UIPI/安全桌面失败；停止新手势，timer 继续尝试清理已有按下。
        if (_syntheticLeftDown || _syntheticRightDown) Volatile.Write(ref _enabled, 0);
    }

    private static Native.INPUT Button(uint flags) => Mouse(flags);
    private static Native.INPUT Positioned(uint flags, int x, int y)
    {
        // ABSOLUTE 需要 MOVE，且必须按含负坐标的虚拟桌面归一化。
        var dx = NormalizeAbsolute(x, HookNative.GetSystemMetrics(76), HookNative.GetSystemMetrics(78));
        var dy = NormalizeAbsolute(y, HookNative.GetSystemMetrics(77), HookNative.GetSystemMetrics(79));
        var input = Mouse(flags | Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK);
        input.u.mi.dx = dx; input.u.mi.dy = dy;
        return input;
    }

    /// <summary>纯坐标换算，公开供无桌面副作用的测试验证。</summary>
    public static int NormalizeAbsolute(int coordinate, int origin, int extent) => extent <= 1 ? 0 :
        (int)Math.Clamp(Math.Round(((double)coordinate - origin) * 65535 / (extent - 1)), 0, 65535);

    private static Native.INPUT Mouse(uint flags, int delta = 0) => new()
    {
        type = Native.INPUT_MOUSE,
        u = new Native.INPUTUNION { mi = new Native.MOUSEINPUT
            { dwFlags = flags, mouseData = unchecked((uint)delta), dwExtraInfo = Marker } },
    };

    private void SendScroll(int y, int x, int anchorX, int anchorY)
    {
        if (y == 0 && x == 0) return;
        var context = CurrentContext();
        if (!ExperimentalRemappingEnabled || !context.Active || context.Region != _gestureRegion ||
            context.Region is not { } r || !r.Contains(anchorX, anchorY)) return;
        // down 可能与 MOVE 合并；吞掉它后系统光标不一定已到按下点。
        // 明确使用手势起点作为滚轮目标，绝不使用被吞掉的手指移动位置或旧区域。
        if (!Native.GetCursorPos(out var cursor) || cursor.X != anchorX || cursor.Y != anchorY)
            if (!Send(Positioned(0, anchorX, anchorY))) return;
        // 一条 INPUT 只能表示一个滚轮轴；斜向滚动必须发两条，不能吞掉横向分量。
        if (y != 0) Send(Mouse(Native.MOUSEEVENTF_WHEEL, y));
        if (x != 0) Send(Mouse(Native.MOUSEEVENTF_HWHEEL, x));
    }
    private static bool Send(Native.INPUT input) => Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>()) == 1;
    private static double Seconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private IntPtr Next(int code, IntPtr wParam, IntPtr lParam) => Native.CallNextHookEx(_hook, code, wParam, lParam);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0) return;
        Volatile.Write(ref _enabled, 0);
        _timer.Dispose();
        // Run 在 finally 里负责 Unhook，WM_QUIT 使消息泵真正退出，不遗留等待线程。
        if (_threadId != 0) Native.PostThreadMessage(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (Thread.CurrentThread != _thread && _thread.Join(TimeSpan.FromSeconds(2))) _ready.Dispose();
    }

    private static class HookNative
    {
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PeekMessage(out Native.MSG message, IntPtr window, uint min, uint max, uint remove);
    }
}
