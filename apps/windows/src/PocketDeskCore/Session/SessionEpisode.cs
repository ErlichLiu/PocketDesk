// 一次远程的生命周期：断开后 reconnectWindow 内重新连上算同一次远程（手机屏与屏上的窗口都保留），
// 超过窗口才算这次远程结束。移植自 macOS 版的 SessionEpisode。
namespace PocketDesk.Core.Session;

public enum EpisodeTransition
{
    None,
    /// <summary>新会话开始。</summary>
    Began,
    /// <summary>重连窗口内再连上，仍是同一会话。</summary>
    Resumed,
    /// <summary>断开，进入重连窗口。</summary>
    Interrupted,
    /// <summary>重连窗口过了，会话结束。</summary>
    Ended,
}

public sealed class SessionEpisode
{
    /// <summary>断开后这么久内重连视为同一次远程。</summary>
    public static readonly TimeSpan ReconnectWindow = TimeSpan.FromSeconds(15);

    public bool IsActive { get; private set; }

    private DateTimeOffset? _endedAt;

    public EpisodeTransition Connected(DateTimeOffset now)
    {
        if (!IsActive)
        {
            IsActive = true;
            _endedAt = null;
            return EpisodeTransition.Began;
        }
        if (_endedAt is { } ended && now - ended <= ReconnectWindow)
        {
            _endedAt = null;
            return EpisodeTransition.Resumed;
        }
        if (_endedAt is not null) return EpisodeTransition.Began;
        return EpisodeTransition.None;
    }

    public EpisodeTransition Disconnected(DateTimeOffset now)
    {
        if (!IsActive || _endedAt is not null) return EpisodeTransition.None;
        _endedAt = now;
        return EpisodeTransition.Interrupted;
    }

    /// <summary>每次时钟推进时调用：重连窗口过了就把这次远程了结。</summary>
    public EpisodeTransition Advance(DateTimeOffset now)
    {
        if (_endedAt is not { } ended || now - ended <= ReconnectWindow) return EpisodeTransition.None;
        IsActive = false;
        _endedAt = null;
        return EpisodeTransition.Ended;
    }
}
