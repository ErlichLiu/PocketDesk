// 零测试框架依赖；仅纯状态机/坐标换算，不构造 InputHook，不装钩子、不发送任何系统输入。
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

var tests = new (string Name, Action Run)[]
{
    ("默认关闭且本机/无标志 HID 始终放行", () => {
        foreach (var enabled in new[] { false, true }) {
            var g = new InputCaptureGate();
            var d = Decide(g, PointerInputKind.LeftDown, injected: false, enabled: enabled);
            Check(!d.Consume && !d.Translate);
            Check(!Decide(g, PointerInputKind.Move, injected: false, enabled: enabled).Consume);
            Check(!Decide(g, PointerInputKind.LeftUp, injected: false, enabled: enabled).Consume);
        }
        Check(!Decide(new(), PointerInputKind.LeftDown, enabled: false).Consume);
    }),
    ("关闭/不活动/无区域/非法区域不得开始", () => {
        Check(!Decide(new(), PointerInputKind.LeftDown, active: false).Consume);
        Check(!new InputCaptureGate().Evaluate(PointerInputKind.LeftDown, true, false, 10, 10, true, true, null).Consume);
        Check(!new InputCaptureGate().Evaluate(PointerInputKind.LeftDown, true, false, 0, 0, true, true, new(0, 0, 0, 10)).Consume);
    }),
    ("区域左上包含，右下排除，负坐标有效", () => {
        Check(Decide(new(), PointerInputKind.LeftDown, x: -100, y: -100).Translate);
        Check(!Decide(new(), PointerInputKind.LeftDown, x: 100).Translate);
        Check(!Decide(new(), PointerInputKind.LeftDown, y: 100).Translate);
        Check(!Decide(new(), PointerInputKind.LeftDown, x: -101).Translate);
    }),
    ("区域外开始后移动入内不能接管", () => {
        var g = new InputCaptureGate();
        Check(!Decide(g, PointerInputKind.LeftDown, x: 150).Consume);
        Check(!Decide(g, PointerInputKind.Move).Consume);
        Check(!Decide(g, PointerInputKind.LeftUp).Consume);
    }),
    ("自发事件不通知状态机，不破坏手势", () => {
        var g = new InputCaptureGate();
        Check(Decide(g, PointerInputKind.LeftDown).Translate);
        Check(!g.Evaluate(PointerInputKind.LeftUp, true, true, 0, 0, true, true, Region()).Consume);
        Check(g.HasCapturedGesture);
        Check(Decide(g, PointerInputKind.LeftUp).Translate);
    }),
    ("重复 down 被吞但不重置，up 恰好一次", () => {
        var g = new InputCaptureGate();
        Check(Decide(g, PointerInputKind.LeftDown).Translate);
        var duplicate = Decide(g, PointerInputKind.LeftDown);
        Check(duplicate.Consume && !duplicate.Translate);
        Check(Decide(g, PointerInputKind.LeftUp).Translate);
        Check(!Decide(g, PointerInputKind.LeftUp).Consume);
    }),
    ("中途退出区域放行移动，吞配对注入 up，绝不吞本机 up", () => {
        var g = new InputCaptureGate();
        Decide(g, PointerInputKind.LeftDown);
        var leave = Decide(g, PointerInputKind.Move, x: 200);
        Check(!leave.Consume && leave.CancelGesture);
        Check(!Decide(g, PointerInputKind.LeftUp, injected: false).Consume);
        var end = Decide(g, PointerInputKind.LeftUp, x: 200);
        Check(end.Consume && !end.Translate);
        Check(!Decide(g, PointerInputKind.LeftUp).Consume);
    }),
    ("区域变动和手动关闭均取消且不吞后续移动", () => {
        foreach (var changeRegion in new[] { true, false }) {
            var g = new InputCaptureGate(); Decide(g, PointerInputKind.LeftDown);
            var d = g.Evaluate(PointerInputKind.Move, true, false, 0, 0, changeRegion, true,
                changeRegion ? new CaptureRegion(-90, -90, 90, 90) : Region());
            Check(d.CancelGesture && !d.Consume);
            Check(Decide(g, PointerInputKind.LeftUp).Consume);
        }
    }),
    ("本机 down 先取消，持有期间禁止捕获注入 down", () => {
        var g = new InputCaptureGate(); Decide(g, PointerInputKind.LeftDown);
        var local = Decide(g, PointerInputKind.LeftDown, injected: false);
        Check(local.CancelGesture && !local.Consume);
        Check(Decide(g, PointerInputKind.LeftUp).Consume);
        Check(!Decide(g, PointerInputKind.LeftDown).Consume);
        Decide(g, PointerInputKind.LeftUp, injected: false);
        Check(Decide(g, PointerInputKind.LeftDown).Translate);
    }),
    ("配置异步失效保留吞配对 up 的记录", () => {
        var g = new InputCaptureGate(); Decide(g, PointerInputKind.LeftDown);
        Check(g.Cancel()); Check(!g.Cancel());
        Check(!Decide(g, PointerInputKind.Move).Consume);
        Check(Decide(g, PointerInputKind.LeftUp, enabled: false).Consume);
    }),
    ("区域外按下与孤立 move/up 均原样放行", () => {
        var t = new TouchTranslator();
        Check(t.Handle(new(TouchKind.Press, 0, 0, 0), false).PassOriginal);
        Check(t.Handle(new(TouchKind.Drag, 100, 100, .1), true).PassOriginal);
        Check(t.Handle(new(TouchKind.Release, 100, 100, .2), true).PassOriginal);
    }),
    ("轻点只重放完整一次 click，使用按下坐标而非抖动坐标", () => {
        var t = new TouchTranslator();
        Check(!Touch(t, TouchKind.Press, -20, 30, 0).PassOriginal);
        Check(Touch(t, TouchKind.Drag, -18, 32, .1).Commands.Count == 0);
        var r = Touch(t, TouchKind.Release, -18, 32, .2);
        Check(!r.PassOriginal && r.Commands.Count == 1);
        Check(r.Commands[0] == new TouchCommand(TouchCommandKind.ReplayCurrent, X: -20, Y: 30));
        Check(!t.HasActiveGesture);
    }),
    ("长按阈值前为点击，阈值及之后为右键", () => {
        foreach (var time in new[] { .499, .5, 2.0 }) {
            var t = new TouchTranslator(); Touch(t, TouchKind.Press, 1, 2, 0);
            Check(Touch(t, TouchKind.Release, 1, 2, time).Commands.Single().Kind ==
                (time < .5 ? TouchCommandKind.ReplayCurrent : TouchCommandKind.RightClick));
        }
    }),
    ("长按拖选从原点 down、同帧移动、恰好一个 up", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 10, 20, 0);
        var start = Touch(t, TouchKind.Drag, 30, 50, .5).Commands;
        Check(start.Count == 2 && start[0] == new TouchCommand(TouchCommandKind.ReplayPress, X: 10, Y: 20));
        Check(start[1] == new TouchCommand(TouchCommandKind.ReplayMove, X: 30, Y: 50));
        Check(Touch(t, TouchKind.Drag, 40, 60, .6).Commands.Single().Kind == TouchCommandKind.ReplayMove);
        var end = Touch(t, TouchKind.Release, 50, 70, .7);
        Check(!end.PassOriginal && end.Commands.Single() == new TouchCommand(TouchCommandKind.ReplayRelease, X: 50, Y: 70));
        Check(t.Cancel().Commands.Count == 0);
    }),
    ("拖选取消仅补一个 up；pending 取消不产生点击", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 1, 2, 0);
        Check(t.Cancel().Commands.Count == 0);
        Touch(t, TouchKind.Press, 1, 2, 1); Touch(t, TouchKind.Drag, 20, 30, 2);
        Check(t.Cancel().Commands.Single().Kind == TouchCommandKind.ReplayRelease);
        Check(t.Cancel().Commands.Count == 0);
    }),
    ("斜向滚动保留两轴，Windows 横纵自然方向符号正确", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
        var c = Touch(t, TouchKind.Drag, 40, 40, .1).Commands.Single();
        Check(c.Kind == TouchCommandKind.Scroll && c.DeltaY == -120 && c.DeltaX == 120);
        Touch(t, TouchKind.Release, 40, 40, .11); Check(t.IsCoasting);
        var step = t.MomentumFrame(); Check(step is { DeltaY: < 0, DeltaX: > 0 });
    }),
    ("移动阈值边界精确，不发生 int 平方溢出", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
        Check(Touch(t, TouchKind.Drag, 9, 0, .1).Commands.Count == 0);
        Check(Touch(t, TouchKind.Drag, 10, 0, .2).Commands.Single().Kind == TouchCommandKind.Scroll);
        t.Cancel(); Touch(t, TouchKind.Press, int.MinValue, 0, 1);
        Check(Touch(t, TouchKind.Drag, int.MaxValue, 0, 1.1).Commands.Single().Kind == TouchCommandKind.Scroll);
    }),
    ("松开携带位移不能错触点击，停止后松开不启动惯性", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
        Check(Touch(t, TouchKind.Release, 20, 0, .1).Commands.Single().Kind == TouchCommandKind.Scroll);
        t.Cancel(); Touch(t, TouchKind.Press, 0, 0, 1);
        Touch(t, TouchKind.Drag, 40, 40, 1.1); Touch(t, TouchKind.Release, 40, 40, 2);
        Check(!t.IsCoasting);
    }),
    ("惯性中按下只停止，整段按住移动松开都不产生 click", () => {
        var t = Coasting();
        Check(Touch(t, TouchKind.Press, 40, 40, .2).Commands.Count == 0 && !t.IsCoasting);
        Check(Touch(t, TouchKind.Drag, 80, 80, .8).Commands.Count == 0);
        Check(Touch(t, TouchKind.Release, 80, 80, 1).Commands.Count == 0);
        Check(!t.HasActiveGesture);
    }),
    ("惯性有限收敛、支持时间步进、挂起后停止", () => {
        var t = Coasting(); var frames = 0;
        while (t.IsCoasting && frames < 1000) { t.MomentumFrame(); frames++; }
        Check(!t.IsCoasting && frames < 1000);
        t = Coasting(); Check(t.MomentumFrame(.5) is null && !t.IsCoasting);
        t = Coasting(); Check(t.MomentumFrame(1 / 120.0) is not null);
    }),
    ("非法时间/倒退/非法 scale 安全取消", () => {
        var t = new TouchTranslator(); t.SetScale(double.NaN); Touch(t, TouchKind.Press, 0, 0, 1);
        Check(Touch(t, TouchKind.Drag, 20, 20, 1.1).Commands.Single().Kind == TouchCommandKind.Scroll);
        Touch(t, TouchKind.Release, 20, 20, double.NaN); Check(!t.IsCoasting && !t.HasActiveGesture);
        Touch(t, TouchKind.Press, 0, 0, 2); Touch(t, TouchKind.Drag, 20, 20, 1);
        Check(!t.HasActiveGesture);
    }),
    ("反向和单轴滚动不会丢失或互相替代", () => {
        foreach (var (x, y, expectedX, expectedY) in new[] { (-40, 0, -120, 0), (0, -40, 0, 120), (40, 0, 120, 0), (0, 40, 0, -120) }) {
            var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
            var c = Touch(t, TouchKind.Drag, x, y, .1).Commands.Single();
            Check(c.DeltaX == expectedX && c.DeltaY == expectedY);
        }
    }),
    ("长按后仅松开携带拖动仍完整配对，不落为右键", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, -20, -30, 0);
        var c = Touch(t, TouchKind.Release, 10, 20, .6).Commands;
        Check(c.Select(x => x.Kind).SequenceEqual(new[] { TouchCommandKind.ReplayPress, TouchCommandKind.ReplayMove, TouchCommandKind.ReplayRelease }));
        Check(!t.HasActiveGesture && !t.IsCoasting);
    }),
    ("重复纯逻辑按下会清理旧拖选，未知类型不触发点击", () => {
        var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
        Touch(t, TouchKind.Drag, 20, 20, 1);
        Check(Touch(t, TouchKind.Press, 10, 10, 2).Commands.Single().Kind == TouchCommandKind.ReplayRelease);
        Check(Touch(t, (TouchKind)999, 10, 10, 3).Commands.Count == 0);
        Check(t.HasActiveGesture);
    }),
    ("其他注入按钮/滚轮不被吞掉，同时取消捕获", () => {
        var g = new InputCaptureGate(); Decide(g, PointerInputKind.LeftDown);
        var other = Decide(g, PointerInputKind.Other);
        Check(other.CancelGesture && !other.Consume && !other.Translate);
        Check(Decide(g, PointerInputKind.LeftUp).Consume);
    }),
    ("gate 与 translator 合成取消不重放 orphan click", () => {
        var g = new InputCaptureGate(); var t = new TouchTranslator();
        Check(Decide(g, PointerInputKind.LeftDown).Translate); Touch(t, TouchKind.Press, 0, 0, 0);
        var changed = Decide(g, PointerInputKind.Move, active: false);
        Check(changed.CancelGesture); Check(t.Cancel().Commands.Count == 0);
        var release = Decide(g, PointerInputKind.LeftUp);
        Check(release.Consume && !release.Translate && !t.HasActiveGesture);
    }),
    ("虚拟桌面归一化处理负原点、端点、越界和单像素", () => {
        Check(InputHook.NormalizeAbsolute(-1920, -1920, 3840) == 0);
        Check(InputHook.NormalizeAbsolute(1919, -1920, 3840) == 65535);
        Check(InputHook.NormalizeAbsolute(-3000, -1920, 3840) == 0);
        Check(InputHook.NormalizeAbsolute(3000, -1920, 3840) == 65535);
        Check(InputHook.NormalizeAbsolute(100, 0, 1) == 0);
        Check(InputHook.NormalizeAbsolute(int.MaxValue, int.MinValue, int.MaxValue) == 65535);
    }),
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed; no hooks installed or native input sent.");
return failed == 0 ? 0 : 1;

static void Check(bool value) { if (!value) throw new Exception("断言失败"); }
static CaptureRegion Region() => new(-100, -100, 100, 100);
static CaptureDecision Decide(InputCaptureGate gate, PointerInputKind kind, bool injected = true,
    bool enabled = true, bool active = true, int x = 0, int y = 0) => gate.Evaluate(kind, injected, false, x, y, enabled, active, Region());
static TouchResponse Touch(TouchTranslator t, TouchKind kind, int x, int y, double seconds) => t.Handle(new(kind, x, y, seconds), true);
static TouchTranslator Coasting()
{
    var t = new TouchTranslator(); Touch(t, TouchKind.Press, 0, 0, 0);
    Touch(t, TouchKind.Drag, 40, 40, .1); Touch(t, TouchKind.Release, 40, 40, .11);
    Check(t.IsCoasting); return t;
}
