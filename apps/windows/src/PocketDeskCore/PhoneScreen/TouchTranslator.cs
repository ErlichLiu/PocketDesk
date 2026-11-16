// 纯逻辑状态机；调用方串行调用，并先确认输入可接管。这里只解释手势，不鉴别 UU。
namespace PocketDesk.Core.PhoneScreen;

public enum TouchKind { Press, Drag, Release }
public readonly record struct TouchInput(TouchKind Kind, int X, int Y, double Seconds);
public enum TouchCommandKind { Scroll, ReplayPress, ReplayCurrent, RightClick, ReplayMove, ReplayRelease }
public readonly record struct TouchCommand(TouchCommandKind Kind, int DeltaY = 0, int DeltaX = 0, int X = 0, int Y = 0);
public readonly record struct TouchResponse(bool PassOriginal, IReadOnlyList<TouchCommand> Commands);
public readonly record struct ScrollStep(int DeltaY, int DeltaX, int X, int Y);

public sealed class TouchTranslator
{
    public const double BaseThreshold = 10;
    public static readonly double LongPressSeconds = 0.5;
    public const double FrameRate = 60;
    private const double PixelsPerNotch = 40, Friction = 0.94, MaxSpeed = 3200;
    private enum Phase { Idle, Pending, Scrolling, Dragging, Stopping }
    private Phase _phase;
    private int _originX, _originY, _lastX, _lastY;
    private double _originSeconds, _lastSeconds, _velocityX, _velocityY, _momentumX, _momentumY;
    private double _scale = 1;
    public bool IsCoasting { get; private set; }
    public bool HasActiveGesture => _phase != Phase.Idle;

    // 防止 NaN、Infinity 或极端缩放让阈值失效；此值是物理像素比例，不再重复乘 DPI。
    public void SetScale(double scale) => _scale = double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, 0.25, 8) : 1;
    private double Threshold => BaseThreshold * _scale;

    public TouchResponse Handle(TouchInput input, bool insidePhoneScreen)
    {
        if (input.Kind is not (TouchKind.Press or TouchKind.Drag or TouchKind.Release)) return Pass();
        if (!double.IsFinite(input.Seconds)) return Cancel();
        if (input.Kind == TouchKind.Press) return Press(input, insidePhoneScreen);
        if (_phase == Phase.Idle) return Pass();
        // 时钟倒退时取消，不把负时间解释成长按或超高速滚动。
        if (input.Seconds < _lastSeconds) return Cancel();
        return input.Kind == TouchKind.Drag ? Drag(input) : Release(input);
    }

    private TouchResponse Press(TouchInput input, bool inside)
    {
        var cleanup = CancelGesture();
        if (!inside) { StopCoasting(); return new(true, cleanup); }
        var stopping = IsCoasting;
        StopCoasting();
        _phase = stopping ? Phase.Stopping : Phase.Pending;
        _originX = _lastX = input.X;
        _originY = _lastY = input.Y;
        _originSeconds = _lastSeconds = input.Seconds;
        _velocityX = _velocityY = 0;
        return new(false, cleanup);
    }

    private TouchResponse Drag(TouchInput input)
    {
        if (_phase == Phase.Stopping) return Swallow();
        if (_phase == Phase.Pending)
        {
            var dx = (double)input.X - _originX;
            var dy = (double)input.Y - _originY;
            if (Math.Sqrt(dx * dx + dy * dy) < Threshold) return Swallow();
            if (input.Seconds - _originSeconds >= LongPressSeconds)
            {
                _phase = Phase.Dragging;
                _lastX = input.X; _lastY = input.Y; _lastSeconds = input.Seconds;
                return new(false, new[]
                {
                    new TouchCommand(TouchCommandKind.ReplayPress, X: _originX, Y: _originY),
                    new TouchCommand(TouchCommandKind.ReplayMove, X: input.X, Y: input.Y),
                });
            }
            _phase = Phase.Scrolling;
        }
        if (_phase == Phase.Dragging)
        {
            _lastX = input.X; _lastY = input.Y; _lastSeconds = input.Seconds;
            return new(false, new[] { new TouchCommand(TouchCommandKind.ReplayMove, X: input.X, Y: input.Y) });
        }
        var stepX = (double)input.X - _lastX;
        var stepY = (double)input.Y - _lastY;
        var elapsed = Math.Max(0.001, input.Seconds - _lastSeconds);
        _velocityX = Math.Clamp(stepX / elapsed, -MaxSpeed, MaxSpeed);
        _velocityY = Math.Clamp(stepY / elapsed, -MaxSpeed, MaxSpeed);
        _lastX = input.X; _lastY = input.Y; _lastSeconds = input.Seconds;
        if (stepX == 0 && stepY == 0) return Swallow();
        // 纵向自然滚动为负，Windows 横向正数向右，两个轴不可共用同一符号。
        return new(false, new[] { new TouchCommand(TouchCommandKind.Scroll,
            Wheel(-stepY), Wheel(stepX), _originX, _originY) });
    }

    private TouchResponse Release(TouchInput input)
    {
        var commands = new List<TouchCommand>();
        // 松开也可能携带最后一段位移；不遗漏末帧，不把明显滑动误判成点击。
        var dx = (double)input.X - _originX;
        var dy = (double)input.Y - _originY;
        if ((_phase == Phase.Pending && Math.Sqrt(dx * dx + dy * dy) >= Threshold) ||
            (_phase == Phase.Scrolling && (input.X != _lastX || input.Y != _lastY)))
            commands.AddRange(Drag(input).Commands);
        switch (_phase)
        {
            case Phase.Pending:
                commands.Add(new TouchCommand(input.Seconds - _originSeconds >= LongPressSeconds
                    ? TouchCommandKind.RightClick : TouchCommandKind.ReplayCurrent, X: _originX, Y: _originY));
                break;
            case Phase.Dragging:
                commands.Add(new TouchCommand(TouchCommandKind.ReplayRelease, X: input.X, Y: input.Y));
                break;
            case Phase.Scrolling:
                // 停住再松手不应重新启动旧速度。
                if (input.Seconds - _lastSeconds <= 0.12)
                {
                    _momentumX = _velocityX / FrameRate;
                    _momentumY = _velocityY / FrameRate;
                    IsCoasting = Math.Abs(_velocityX) >= 120 || Math.Abs(_velocityY) >= 120;
                }
                break;
        }
        _phase = Phase.Idle;
        return new(false, commands);
    }

    public ScrollStep? MomentumFrame(double elapsedSeconds = 1 / FrameRate)
    {
        if (!IsCoasting) return null;
        // 挂起/阻塞后不积攒一次大滚动。
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 || elapsedSeconds > 0.25)
        { StopCoasting(); return null; }
        var frames = elapsedSeconds * FrameRate;
        var decay = Math.Pow(Friction, frames);
        _momentumX *= decay; _momentumY *= decay;
        if (Math.Abs(_momentumX) < 0.5 && Math.Abs(_momentumY) < 0.5)
        { StopCoasting(); return null; }
        return new ScrollStep(Wheel(-_momentumY * frames), Wheel(_momentumX * frames), _originX, _originY);
    }

    public void StopCoasting() { IsCoasting = false; _momentumX = _momentumY = 0; }
    /// <summary>失去区域/远程状态时取消；拖选必须补松开。调用方取消时不得移动到旧区域。</summary>
    public TouchResponse Cancel()
    {
        StopCoasting();
        return new(false, CancelGesture());
    }
    private TouchCommand[] CancelGesture()
    {
        var dragging = _phase == Phase.Dragging;
        _phase = Phase.Idle;
        return dragging ? new[] { new TouchCommand(TouchCommandKind.ReplayRelease, X: _lastX, Y: _lastY) } : Array.Empty<TouchCommand>();
    }
    private static int Wheel(double pixels) => (int)Math.Clamp(Math.Round(pixels / PixelsPerNotch * 120), int.MinValue, int.MaxValue);
    private static TouchResponse Swallow() => new(false, Array.Empty<TouchCommand>());
    private static TouchResponse Pass() => new(true, Array.Empty<TouchCommand>());
}
