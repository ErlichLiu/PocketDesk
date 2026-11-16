// 会话层的组合：连接判定 + 会话生命周期，把每拍观测变成功能模块要处理的会话变化。
// 功能模块（手机屏）只消费会话状态，会话层不知道有哪些功能。
namespace PocketDesk.Core.Session;

public enum SessionTransitionKind
{
    Began,
    Resumed,
    ControllersChanged,
    Interrupted,
    Ended,
}

public readonly record struct SessionTransition(SessionTransitionKind Kind, IReadOnlyList<string> Controllers);

public sealed class RemoteSession
{
    private readonly ConnectionTracker _tracker = new();
    private readonly SessionEpisode _episode = new();
    /// <summary>这次远程最后读到的那组控制端；断开那一拍 tracker 会清空，这里在重连窗口里照样记着。</summary>
    private List<string> _group = new();
    /// <summary>几台同时连着时先离开的控制端与离开时刻，补上同样的重连窗口。</summary>
    private readonly Dictionary<string, DateTimeOffset> _departed = new();

    public DateTimeOffset? EpisodeBeganAt { get; private set; }
    public bool IsConnected => _tracker.IsConnected;
    public IReadOnlyList<string> Controllers => _tracker.Controllers;
    public bool InEpisode => _episode.IsActive;

    /// <summary>这次远程算在内的控制端：最后读到的那组，加上离开还不到重连窗口的。</summary>
    public IReadOnlyList<string> EpisodeControllers(DateTimeOffset now)
    {
        var lingering = _departed.Where(kv => now - kv.Value <= SessionEpisode.ReconnectWindow)
                                 .Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal);
        return _group.Concat(lingering).ToArray();
    }

    public void NoteSelfInducedChange(DateTimeOffset at) => _tracker.NoteSelfInducedChange(at);

    public IReadOnlyList<SessionTransition> Update(UUSignals signals, DateTimeOffset now)
    {
        var output = new List<SessionTransition>();

        if (_episode.Advance(now) == EpisodeTransition.Ended)
        {
            output.Add(new SessionTransition(SessionTransitionKind.Ended, Array.Empty<string>()));
            _group = new List<string>();
            _departed.Clear();
            EpisodeBeganAt = null;
        }

        switch (_tracker.Update(signals, now))
        {
            case { Kind: ConnectionEventKind.Connected } e:
                if (_episode.Connected(now) == EpisodeTransition.Began)
                {
                    EpisodeBeganAt = now;
                    _group = e.Controllers.ToList();
                    _departed.Clear();
                    output.Add(new SessionTransition(SessionTransitionKind.Began, e.Controllers));
                }
                else
                {
                    if (e.Controllers.Count > 0) Regroup(e.Controllers.ToList(), now);
                    output.Add(new SessionTransition(SessionTransitionKind.Resumed, e.Controllers));
                }
                break;

            case { Kind: ConnectionEventKind.ControllersChanged } e:
                Regroup(e.Controllers.ToList(), now);
                output.Add(new SessionTransition(SessionTransitionKind.ControllersChanged, e.Controllers));
                break;

            case { Kind: ConnectionEventKind.Disconnected }:
                if (_episode.Disconnected(now) == EpisodeTransition.Interrupted)
                    output.Add(new SessionTransition(SessionTransitionKind.Interrupted, Array.Empty<string>()));
                break;
        }

        return output;
    }

    /// <summary>换成新的一组：不在新组里的记下离开时刻，回来的不再算离开。</summary>
    private void Regroup(List<string> names, DateTimeOffset now)
    {
        foreach (var name in _group.Where(n => !names.Contains(n))) _departed[name] = now;
        foreach (var name in names) _departed.Remove(name);
        _group = names;
    }
}
