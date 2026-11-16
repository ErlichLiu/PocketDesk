// 一拍里对 UU远程 的观测。移植自 macOS 版的 UUSignals：
// 三路信号（覆盖窗口、控制端表格、推流状态）任一路给出正面证据即视为在场，
// 缺席则要三路同时缺席——UU 切屏时覆盖窗口会短暂重建，单看一路会误判断开。
namespace PocketDesk.Core.Session;

/// <summary>一拍里对 UU远程 的观测。</summary>
public readonly record struct UUSignals(
    /// <summary>UU 被控时放在本机屏幕上的覆盖窗口是否在屏。</summary>
    bool OverlayVisible,
    /// <summary>从 UU 界面读到的控制端名字；读不到时为空。</summary>
    IReadOnlyList<string> Controllers,
    /// <summary>UU 是否在推流。null = 未知（日志监听没在跑）。</summary>
    bool? CaptureActive)
{
    /// <summary>任一路信号给出了在场的正面证据：覆盖窗口在屏、认出了控制端、推流在进行。</summary>
    public bool IsPresent => OverlayVisible || Controllers.Count > 0 || CaptureActive == true;

    public override string ToString()
    {
        var capture = CaptureActive switch { true => "是", false => "否", null => "未知" };
        var names = Controllers.Count == 0 ? "无" : string.Join("、", Controllers);
        return $"overlay={(OverlayVisible ? "是" : "否")} 控制端={names} capture={capture}";
    }
}

public enum ConnectionEventKind
{
    /// <summary>连入时认出的控制端（可能还没读到，为空）。</summary>
    Connected,
    /// <summary>同时连着的控制端变了：有人加入、有人离开。</summary>
    ControllersChanged,
    Disconnected,
}

public readonly record struct ConnectionEvent(ConnectionEventKind Kind, IReadOnlyList<string> Controllers);
