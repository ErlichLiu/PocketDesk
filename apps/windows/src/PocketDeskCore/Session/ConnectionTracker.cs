// 连接判定（纯值类型，now 由调用方传入）：把每拍的 UU 观测合成连入 / 换控制端 / 断开。
// 移植要点与原版一致：
// - 在场只认正面证据；连入要快，一看到在场就算
// - 断开要稳：连续缺席超过 disconnectDebounce 且已过自扰静默期才判（自己加屏/撤屏会让 UU 停流重开）
namespace PocketDesk.Core.Session;

public sealed class ConnectionTracker
{
    /// <summary>UU 切屏时覆盖窗口会短暂重建，连续缺席超过这么久才可能判断开。</summary>
    public static readonly TimeSpan DisconnectDebounce = TimeSpan.FromSeconds(1.5);

    /// <summary>本工具自己加屏、撤屏后，屏幕组合变化会让 UU 停流重开，这段时间内不判断开。</summary>
    public static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(5);

    public bool IsConnected { get; private set; }

    /// <summary>此刻同时连着的控制端；没认出过为空。</summary>
    public IReadOnlyList<string> Controllers { get; private set; } = Array.Empty<string>();

    private DateTimeOffset? _lastPresentAt;
    private DateTimeOffset? _quietUntil;

    /// <summary>本工具即将或刚刚引起屏幕 / 窗口扰动时调用。</summary>
    public void NoteSelfInducedChange(DateTimeOffset at)
    {
        var until = at + QuietWindow;
        _quietUntil = _quietUntil is { } old && old > until ? old : until;
    }

    public ConnectionEvent? Update(UUSignals signals, DateTimeOffset now)
    {
        var names = signals.Controllers;
        if (signals.IsPresent) _lastPresentAt = now;

        if (!IsConnected)
        {
            if (!signals.IsPresent) return null;
            IsConnected = true;
            Controllers = names;
            Log.Info($"检测到 UU 连入（{signals}）");
            return new ConnectionEvent(ConnectionEventKind.Connected, Controllers);
        }

        if (signals.IsPresent)
        {
            // 没认出控制端时保留原来的：主窗口切走了不代表换了人
            if (names.Count == 0 || new HashSet<string>(names).SetEquals(Controllers)) return null;
            Controllers = names;
            Log.Info($"控制端变为 {string.Join("、", names)}（{signals}）");
            return new ConnectionEvent(ConnectionEventKind.ControllersChanged, names);
        }

        if (_lastPresentAt is not { } last || now - last <= DisconnectDebounce || !QuietPassed(now)) return null;

        Log.Info($"检测到 UU 断开（{signals}）");
        IsConnected = false;
        Controllers = Array.Empty<string>();
        return new ConnectionEvent(ConnectionEventKind.Disconnected, Array.Empty<string>());
    }

    private bool QuietPassed(DateTimeOffset now) => _quietUntil is not { } until || now > until;
}
