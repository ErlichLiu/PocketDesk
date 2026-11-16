// 无 Win32 调用的安全门；低级鼠标钩子不能鉴别来源进程。
namespace PocketDesk.Win;

public readonly record struct CaptureRegion(int Left, int Top, int Right, int Bottom)
{
    public bool IsValid => Right > Left && Bottom > Top;
    public bool Contains(int x, int y) => IsValid && x >= Left && x < Right && y >= Top && y < Bottom;
}
public enum PointerInputKind { Move, LeftDown, LeftUp, Other }
public readonly record struct CaptureDecision(bool Consume = false, bool Translate = false, bool CancelGesture = false);

/// <summary>
/// 默认放行。仅用户明确启用实验模式后，允许接管区域内开始的 injected 流。
/// injected 不是 UU 的身份证；虚拟 HID/物理鼠标没有该标志，一律不接管。
/// 使用者必须串行调用；取消时吞掉原先已吞按下所对应的注入松开，不吞本机输入。
/// </summary>
public sealed class InputCaptureGate
{
    private bool _captured, _discardUntilUp, _physicalLeftDown;
    private CaptureRegion? _region;
    public bool HasCapturedGesture => _captured;

    public CaptureDecision Evaluate(PointerInputKind kind, bool injected, bool selfGenerated,
        int x, int y, bool experimentalEnabled, bool active, CaptureRegion? region)
    {
        if (selfGenerated) return new();
        if (!injected)
        {
            if (kind == PointerInputKind.LeftDown) _physicalLeftDown = true;
            if (kind == PointerInputKind.LeftUp) _physicalLeftDown = false;
            return new(CancelGesture: Cancel());
        }
        var valid = experimentalEnabled && active && region is { } r && r.Contains(x, y);
        var cancel = false;
        if (_captured && (!valid || region != _region || kind == PointerInputKind.Other)) cancel = Cancel();
        if (_discardUntilUp)
        {
            if (kind == PointerInputKind.LeftUp) { _discardUntilUp = false; return new(true, CancelGesture: cancel); }
            // 不让重复 down 破坏已吞掉的 down/up 配对，但区域外移动始终放行。
            return new(kind == PointerInputKind.LeftDown, CancelGesture: cancel);
        }
        if (kind == PointerInputKind.LeftDown)
        {
            if (_captured) return new(true); // 重复 down 不重置手势。
            if (!valid || _physicalLeftDown) return new(CancelGesture: cancel);
            _captured = true; _region = region;
            return new(true, true, cancel);
        }
        if (_captured && kind is PointerInputKind.Move or PointerInputKind.LeftUp)
        {
            if (kind == PointerInputKind.LeftUp) _captured = false;
            return new(true, true, cancel);
        }
        return new(CancelGesture: cancel);
    }

    /// <summary>配置改变/过期时取消；保留需要吞掉配对 up 的记录。</summary>
    public bool Cancel()
    {
        if (!_captured) return false;
        _captured = false; _discardUntilUp = true; _region = null;
        return true;
    }
}
