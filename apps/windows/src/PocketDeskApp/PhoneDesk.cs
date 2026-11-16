// 窗口调度严格串行：先持久化原始 placement，再动窗口，实际恢复匹配后才销账。
namespace PocketDesk.App;

using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

public enum ScreenState { Off, Preparing, Ready, Failed }
public enum DeskNotice { None, WindowGone, WindowUnresponsive, RecordUnwritable, ScreenNotReady }

public sealed record PlacedWindow(WindowInfo Window, int Zoom, int Width, Native.RECT? Settled)
{
    // 全局 SendInput 不具备接收窗口原子保证；自动字号停用，而不是把期望步数当成实际步数。
    public bool Zoomable => WindowService.AutomaticZoomEnabled && ZoomBook.IsZoomable(Window.ExePath);
}

public sealed record DeskState
{
    public ScreenState Screen { get; init; } = ScreenState.Off;
    public DisplayInfo? Display { get; init; }
    public PlacedWindow? Placed { get; init; }
    public bool ListVisible { get; init; } = true;
    public DeskNotice Notice { get; init; }
    public string? NoticeApp { get; init; }
    public bool IsReady => Screen == ScreenState.Ready && Display is not null;
}

/// <summary>仅做依赖注入，安全测试使用替身，不操作真实窗口/显示配置。</summary>
public interface IPhoneDeskPlatform
{
    DisplayInfo? PhoneScreen();
    bool SetWidth(DisplayInfo display, ScreenProfile profile, int width);
    WindowLook? Look(WindowKey key);
    bool Exists(WindowKey key);
    bool MatchesIdentity(PhoneRecord record);
    Task<Native.RECT?> MoveAsync(WindowKey key, Native.RECT area);
    Task<bool> RestoreAsync(PhoneRecord record);
    void Focus(WindowKey key);
}

public sealed class PhoneDesk
{
    private readonly AppConfig _config;
    private readonly RestoreRecord _ledger;
    private readonly string _configPath;
    private readonly IPhoneDeskPlatform _platform;
    private readonly object _queueLock = new();
    private Task _tail = Task.CompletedTask;
    private Task<bool>? _shutdownTask;
    private bool _wanted;
    private bool _shuttingDown;
    private ScreenProfile Profile => ScreenProfile.FromId(_config.Profile);

    public DeskState State { get; private set; } = new();
    public event Action<DeskState>? Changed;

    public PhoneDesk(AppConfig config, ZoomBook zoomBook, RestoreRecord ledger, IPhoneDeskPlatform? platform = null,
        string? configPath = null)
    {
        _config = config;
        _configPath = configPath ?? PocketDeskPaths.Config;
        _ledger = ledger;
        _platform = platform ?? new WindowsPlatform();
    }

    public Native.RECT? WindowArea() => State.Display is { } display
        ? DisplayService.WindowArea(display, Profile.HomeBarHeight(display.Width)) : null;

    private void Update(Func<DeskState, DeskState> change)
    {
        var next = change(State);
        if (next == State) return;
        State = next;
        Changed?.Invoke(State);
    }

    public void SetWanted(bool on)
    {
        lock (_queueLock)
        {
            if (_shuttingDown || on == _wanted) return;
            _wanted = on;
            Enqueue(async () => { if (on) await AttachAsync(); else await DetachAsync(); });
        }
    }

    public void SetProfile(ScreenProfile profile) => Enqueue(async () =>
    {
        if (_config.Profile == profile.Id) return;
        if (State.Placed is { } placed)
        {
            if (!await ReturnToOriginalAsync(placed)) { RestoreFailed(placed.Window.App); return; }
            Update(s => s with { Placed = null, ListVisible = true });
        }
        if (!_ledger.IsReadable || _ledger.All.Count != 0) { RestoreFailed(null); return; }
        _config.Profile = profile.Id;
        _config.Save(_configPath);
        RefreshDisplay();
        await MatchWidthAsync();
    });

    public void Pull(WindowInfo window) => Enqueue(() => PlaceAsync(window));
    public void ShowList() => Enqueue(async () =>
    {
        Update(s => s with { ListVisible = true });
        await MatchWidthAsync();
    });
    public void ReturnToPlaced() => Enqueue(RevealAsync);
    public void PutBack() => Enqueue(async () =>
    {
        if (State.Placed is not { } placed) return;
        if (await ReturnToOriginalAsync(placed))
        {
            Update(s => s with { Placed = null, ListVisible = true, Notice = DeskNotice.None });
            await MatchWidthAsync();
        }
        else RestoreFailed(placed.Window.App);
    });

    /// <summary>禁用不可靠的全局字号注入，实际应用步数始终为 0，持久化记录也为 0。</summary>
    public void ZoomBy(int delta) { }

    public void Verify() => Enqueue(async () =>
    {
        if (State.Placed is not { } placed || !State.IsReady) return;
        if (!_platform.Exists(placed.Window.Key))
        {
            if (!_ledger.Remove(placed.Window.Key)) { RestoreFailed(placed.Window.App); return; }
            Update(s => s with { Placed = null, ListVisible = true });
            await MatchWidthAsync();
            return;
        }
        var record = _ledger.Find(placed.Window.Key);
        if (record is null || !_platform.MatchesIdentity(record)) { RestoreFailed(placed.Window.App); return; }
        if (_platform.Look(placed.Window.Key) is not { } look || look.Minimized)
            Update(s => s with { ListVisible = true });
    });

    public void Recover() => Enqueue(async () => { await RecoverAsync(); });

    /// <summary>调用方必须 await 并检查结果；false 表示恢复未完成，应取消退出或提示用户重试。</summary>
    public Task<bool> Shutdown() => ShutdownAsync();
    public Task<bool> ShutdownAsync()
    {
        lock (_queueLock)
        {
            if (_shutdownTask is { IsCompleted: false } || _shutdownTask is { IsCompletedSuccessfully: true } done && done.Result)
                return _shutdownTask;
            _wanted = false;
            _shuttingDown = true; // 先封队列，再等待已经接收的全部工作。
            var previous = _tail;
            _shutdownTask = Task.Run(async () =>
            {
                var restored = false;
                try
                {
                    await previous.ConfigureAwait(false);
                    restored = await DetachAsync().ConfigureAwait(false);
                    return restored;
                }
                catch (Exception e) { Log.Error($"退出前恢复失败，保留账本：{e}"); return false; }
                finally
                {
                    // 取消退出后仍允许用户点“放回”重试，不能把整个调度器永久封死。
                    if (!restored) lock (_queueLock) _shuttingDown = false;
                }
            });
            _tail = _shutdownTask;
            return _shutdownTask;
        }
    }

    /// <summary>等待当前已排队操作，便于集成及无真实窗口的自动测试。</summary>
    public Task DrainAsync() { lock (_queueLock) return _tail; }

    private async Task AttachAsync()
    {
        if (State.IsReady) return;
        Update(s => s with { Screen = ScreenState.Preparing });
        var display = _platform.PhoneScreen();
        if (display is null)
        {
            Update(s => s with { Screen = ScreenState.Failed });
            return;
        }
        Update(s => s with { Screen = ScreenState.Ready, Display = display, Notice = DeskNotice.None });
        await MatchWidthAsync();
    }

    private async Task<bool> DetachAsync()
    {
        // 包括崩溃遗留，不只恢复当前界面上那一个窗口。
        if (!await RecoverAsync()) { RestoreFailed(State.Placed?.Window.App); return false; }
        Update(s => s with { Screen = ScreenState.Off, Display = null, Placed = null,
            ListVisible = true, Notice = DeskNotice.None });
        return true;
    }

    private int WantedWidth => State.Placed is { } placed && !State.ListVisible ? placed.Width : Profile.BaseWidth;
    private async Task MatchWidthAsync()
    {
        if (State.Display is not null) await SwitchWidthAsync(WantedWidth);
    }
    private async Task SwitchWidthAsync(int width)
    {
        if (State.Display is not { } display) return;
        // 同宽不同高同样需要换模式，不能把竖屏误当成已匹配的折叠屏。
        if (display.Width == width && Math.Abs(display.Height - Profile.Height(width)) <= 1) return;
        if (!_platform.SetWidth(display, Profile, width)) return;
        await Task.Delay(400).ConfigureAwait(false);
        RefreshDisplay();
    }
    private void RefreshDisplay()
    {
        if (_platform.PhoneScreen() is { } display) Update(s => s with { Display = display });
    }

    private async Task<Native.RECT?> FillAsync(PlacedWindow placed)
    {
        var record = _ledger.Find(placed.Window.Key);
        if (record is null || !_platform.MatchesIdentity(record)) return null;
        for (var guard = 0; guard < ScreenFit.Widths(Profile).Count; guard++)
        {
            await SwitchWidthAsync(placed.Width);
            if (State.Display is not { } display || WindowArea() is not { } area
                || !_platform.MatchesIdentity(record)) return null;
            var got = await _platform.MoveAsync(placed.Window.Key, area).ConfigureAwait(false);
            if (!_platform.MatchesIdentity(record)) return null;
            // 非空也必须实际匹配，不能把最后一个错误位置当成成功。
            if (got is { } actual && WindowService.Matches(actual, area))
            {
                Update(s => s with { Placed = placed with { Width = display.Width, Settled = actual } });
                return actual;
            }
            // 失败位置仅作最小尺寸诊断，绝不作为 settled；换档后必须重新核验移动成功。
            if (_platform.Look(placed.Window.Key) is not { } look || !_platform.MatchesIdentity(record)
                || Math.Abs((long)look.Frame.Left - area.Left) >= 2
                || Math.Abs((long)look.Frame.Top - area.Top) >= 2) return null;
            var width = ScreenFit.WidthToFit(Profile, look.Frame.Width, look.Frame.Height,
                area.Width, area.Height, display.Width);
            if (width <= display.Width || width == placed.Width) return null;
            placed = placed with { Width = width };
        }
        return null;
    }

    private async Task RevealAsync()
    {
        if (State.Placed is not { } placed || !State.IsReady) return;
        if (await FillAsync(placed) is not { } settled) { RestoreFailed(placed.Window.App); return; }
        Update(s => s with { Placed = (s.Placed ?? placed) with { Settled = settled }, ListVisible = false, Notice = DeskNotice.None });
        FocusIfOwned(placed.Window.Key);
    }

    private async Task PlaceAsync(WindowInfo window)
    {
        if (!State.IsReady) { Update(s => s with { Notice = DeskNotice.ScreenNotReady }); return; }
        if (!_ledger.IsReadable) { Update(s => s with { Notice = DeskNotice.RecordUnwritable }); return; }
        if (State.Placed?.Window.Key == window.Key) { await RevealAsync(); return; }
        if (_platform.Look(window.Key) is null)
        {
            Update(s => s with { Notice = DeskNotice.WindowGone, NoticeApp = window.App });
            return;
        }
        // 没恢复的历史窗口不能被新放入覆盖；账本只允许一个活动恢复事务。
        if (_ledger.All.Any(r => r.Key != State.Placed?.Window.Key)) { RestoreFailed(window.App); return; }
        if (State.Placed is { } current)
        {
            if (!await ReturnToOriginalAsync(current)) { RestoreFailed(current.Window.App); return; }
            Update(s => s with { Placed = null, ListVisible = true });
        }
        // 旧窗口恢复可能耗时，重新捕获目标身份及原始 placement，不能沿用此前快照。
        if (_platform.Look(window.Key) is not { Placement: not null, ProcessStartTimeUtcTicks: not null } look)
        {
            Update(s => s with { Notice = DeskNotice.WindowGone, NoticeApp = window.App });
            return;
        }
        var record = new PhoneRecord(window.Key.Pid, window.Key.Handle, window.App, window.Label,
            look.Frame.Left, look.Frame.Top, look.Frame.Width, look.Frame.Height, look.FullScreen, look.Minimized,
            look.Placement, AppliedZoom: 0, ProcessStartTimeUtcTicks: look.ProcessStartTimeUtcTicks);
        if (!_ledger.Add(record)) { Update(s => s with { Notice = DeskNotice.RecordUnwritable }); return; }
        var placed = new PlacedWindow(window, 0, Profile.BaseWidth, null);
        Update(s => s with { Placed = placed });
        if (await FillAsync(placed) is not { } settled)
        {
            Log.Warn($"{window.Label} 未实际挪到手机屏，按原始 placement 回滚");
            if (await ReturnToOriginalAsync(placed))
            {
                Update(s => s with { Placed = null, ListVisible = true,
                    Notice = DeskNotice.WindowUnresponsive, NoticeApp = window.App });
                await MatchWidthAsync();
            }
            else RestoreFailed(window.App); // 不清空 placed，仍可重试放回；持久化记录保留。
            return;
        }
        Update(s => s with { Placed = (s.Placed ?? placed) with { Settled = settled }, ListVisible = false, Notice = DeskNotice.None });
        FocusIfOwned(window.Key);
        Log.Info($"{window.Label} 已核对放上手机屏，实际字号增量 0");
    }

    private void FocusIfOwned(WindowKey key)
    {
        if (_ledger.Find(key) is { } record && _platform.MatchesIdentity(record)) _platform.Focus(key);
    }
    private void RestoreFailed(string? app) => Update(s => s with
        { Notice = DeskNotice.WindowUnresponsive, NoticeApp = app, ListVisible = true });

    private Task<bool> ReturnToOriginalAsync(PlacedWindow placed) =>
        _ledger.Find(placed.Window.Key) is { } record ? ReturnRecordAsync(record) : Task.FromResult(false);

    private async Task<bool> ReturnRecordAsync(PhoneRecord record)
    {
        if (!_platform.Exists(record.Key)) return _ledger.Remove(record.Key);
        if (!_platform.MatchesIdentity(record)) return false; // 无法核实身份时，绝不移动也不销账。
        if (!await _platform.RestoreAsync(record).ConfigureAwait(false)) return false;
        if (!_platform.MatchesIdentity(record)) return false;
        // 原生恢复接口负责核验 placement，落盘失败同样不能宣称完成。
        if (!_ledger.Remove(record.Key)) return false;
        Log.Info($"{record.Label} 已恢复原始窗口状态并销账");
        return true;
    }

    private async Task<bool> RecoverAsync()
    {
        if (!_ledger.IsReadable) return false;
        foreach (var record in _ledger.All)
        {
            if (await ReturnRecordAsync(record) && State.Placed?.Window.Key == record.Key)
                Update(s => s with { Placed = null, ListVisible = true });
        }
        return _ledger.All.Count == 0;
    }

    // task-chain 在入队锁内确定顺序；ThreadPool 启动顺序不再决定哪项意图先执行。
    private void Enqueue(Func<Task> work)
    {
        lock (_queueLock)
        {
            if (_shuttingDown) return;
            var previous = _tail;
            _tail = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                try { await work().ConfigureAwait(false); }
                catch (Exception e) { Log.Error($"手机屏调度出错，保留恢复记录：{e}"); }
            });
        }
    }

    private sealed class WindowsPlatform : IPhoneDeskPlatform
    {
        public DisplayInfo? PhoneScreen() => DisplayService.PhoneScreen();
        public bool SetWidth(DisplayInfo display, ScreenProfile profile, int width) => DisplayService.SetWidth(display, profile, width);
        public WindowLook? Look(WindowKey key) => WindowService.Look(key);
        public bool Exists(WindowKey key) => WindowService.Exists(key);
        public bool MatchesIdentity(PhoneRecord record) => WindowService.MatchesIdentity(record);
        public Task<Native.RECT?> MoveAsync(WindowKey key, Native.RECT area) => WindowService.MoveAsync(key, area);
        public Task<bool> RestoreAsync(PhoneRecord record) => WindowService.RestorePlacementAsync(record);
        public void Focus(WindowKey key) => WindowService.Focus(key);
    }
}
