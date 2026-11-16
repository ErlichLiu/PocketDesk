// 窗口必须同时核对进程号和句柄；句柄/PID 都可重用，不能把 IsWindow 当作身份验证。
// 新恢复记录另外保存进程创建时间；跨调用不缓存任何已验证身份。
namespace PocketDesk.Core.PhoneScreen;

/// <summary>窗口的可持久化身份。</summary>
public readonly record struct WindowKey(int Pid, long Handle)
{
    public bool IsValid => Pid > 0 && Handle != 0;
    public bool MatchesOwner(uint actualPid) => IsValid && actualPid == (uint)Pid;
    public override string ToString() => $"{Pid}/{Handle}";
}

/// <summary>窗口列表里的一张卡片。</summary>
public sealed record WindowInfo(WindowKey Key, string App, string? ExePath, string Title)
{
    /// <summary>日志里用：App 名 +「标题前 30 个字」。</summary>
    public string Label => string.IsNullOrEmpty(Title) ? App : $"{App}「{Truncate(Title, 30)}」";

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

public static class WindowList
{
    /// <summary>不出现在列表里的进程：UU远程 自己（把遥控器放到手机屏上没有意义）。</summary>
    public static readonly HashSet<string> ExcludedProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "GameViewer", "GameViewerServer", "GameViewerHealthd", "PocketDesk" };

    /// <summary>
    /// 按前后层次排：最近用过的在前，和 iPhone 的多任务界面一样。
    /// frontToBack 是屏上窗口从前到后的句柄；不在里面的排最后，彼此保持原顺序。
    /// </summary>
    public static IReadOnlyList<WindowInfo> Ordered(IReadOnlyList<WindowInfo> windows, IReadOnlyList<long> frontToBack)
    {
        var rank = new Dictionary<long, int>();
        for (var i = 0; i < frontToBack.Count; i++) rank.TryAdd(frontToBack[i], i);

        return windows
            .Where(w => !ExcludedProcesses.Contains(w.App)
                && (w.ExePath is null || !ExcludedProcesses.Contains(System.IO.Path.GetFileNameWithoutExtension(w.ExePath))))
            .Select((w, i) => (w, i))
            .OrderBy(t => rank.TryGetValue(t.w.Key.Handle, out var r) ? r : int.MaxValue)
            .ThenBy(t => t.i)
            .Select(t => t.w)
            .ToArray();
    }

    /// <summary>
    /// 列表露着时窗口不换位置：手指正要点，卡片挪了位置就会点到别的窗口上。
    /// 还在的按原来的顺序、用新的标题；新出现的接在后面；关掉的去掉。
    /// </summary>
    public static IReadOnlyList<WindowInfo> KeepingOrder(IReadOnlyList<WindowInfo> shown, IReadOnlyList<WindowInfo> fresh)
    {
        var latest = fresh.ToDictionary(w => w.Key);
        var known = shown.Select(w => w.Key).ToHashSet();
        var kept = shown.Where(w => latest.ContainsKey(w.Key)).Select(w => latest[w.Key]);
        return kept.Concat(fresh.Where(w => !known.Contains(w.Key))).ToArray();
    }
}
