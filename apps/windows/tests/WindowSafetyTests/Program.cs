using PocketDesk.App;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

var root = Path.Combine(AppContext.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Log.UsePath(Path.Combine(root, "test.log"));
var passed = 0;
try
{
    await Test("PID + handle identity rejects handle-only reuse", () =>
    {
        var key = new WindowKey(41, 99);
        Check(key.MatchesOwner(41) && !key.MatchesOwner(42));
        Check(!new WindowKey(41, 0).MatchesOwner(41));
        Check(!new WindowKey(0, 99).MatchesOwner(0));
        Check(!WindowService.Exists(new WindowKey(0, 0))); // 无原生调用。
        return Task.CompletedTask;
    });
    await Test("placement verifies normal rectangle, show state, min/max points and flags", () =>
    {
        var p = Record().Placement!;
        Check(WindowService.PlacementMatches(p, p));
        Check(WindowService.PlacementMatches(p, p with { Flags = p.Flags | 4 }));
        Check(!WindowService.PlacementMatches(p, p with { ShowCommand = 3 }));
        Check(!WindowService.PlacementMatches(p, p with { Flags = p.Flags ^ 2 }));
        Check(!WindowService.PlacementMatches(p, p with { Left = p.Left + 20 }));
        Check(!WindowService.PlacementMatches(p, p with { MinX = 1 }));
        Check(!WindowService.PlacementMatches(p, p with { MaxY = 1 }));
        return Task.CompletedTask;
    });
    await Test("actual mismatched frame is never a successful match", () =>
    {
        var a = new Native.RECT { Left = 0, Top = 0, Right = 800, Bottom = 600 };
        Check(WindowService.Matches(a, a));
        Check(!WindowService.Matches(a, a with { Left = 40, Right = 840 }));
        Check(!WindowService.Matches(a, a with { Right = 1000 }));
        return Task.CompletedTask;
    });
    await Test("durable original placement survives reload; duplicate add cannot overwrite", () =>
    {
        var path = NewPath();
        var ledger = new RestoreRecord(path);
        var original = Record();
        Check(ledger.Add(original));
        Check(!ledger.Add(original with { X = 999 }));
        Check(ledger.Find(original.Key) == original);
        Check(new RestoreRecord(path).Find(original.Key) == original);
        Check(!ledger.Update(original with { Width = 900 }));
        return Task.CompletedTask;
    });
    await Test("only actual applied zoom increment is persisted without altering original", () =>
    {
        var path = NewPath();
        var ledger = new RestoreRecord(path);
        var original = Record();
        Check(ledger.Add(original));
        Check(ledger.Update(original with { AppliedZoom = 2 }));
        Check(new RestoreRecord(path).Find(original.Key) == original with { AppliedZoom = 2 });
        Check(!ledger.Update(original with { AppliedZoom = 2, WasFullScreen = true }));
        Check(!ledger.Update(original with { AppliedZoom = 101 }));
        return Task.CompletedTask;
    });
    await Test("failed add leaves memory and disk unchanged", () =>
    {
        var path = NewPath();
        var ledger = new RestoreRecord(path);
        Directory.CreateDirectory(path); // 构造后出现写入障碍。
        Check(!ledger.Add(Record()) && ledger.All.Count == 0);
        Directory.Delete(path);
        Check(ledger.Add(Record()) && new RestoreRecord(path).All.Count == 1);
        return Task.CompletedTask;
    });
    await Test("failed removal does not clear in-memory restoration debt", () =>
    {
        var path = NewPath();
        var ledger = new RestoreRecord(path);
        Check(ledger.Add(Record()));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!ledger.Remove(Record().Key) && ledger.All.Count == 1);
        Check(new RestoreRecord(path).All.Count == 1);
        Check(ledger.Remove(Record().Key) && new RestoreRecord(path).All.Count == 0);
        return Task.CompletedTask;
    });
    await Test("corrupt ledger fails closed and is never replaced with empty data", () =>
    {
        var path = NewPath();
        const string corrupt = "{not-json";
        File.WriteAllText(path, corrupt);
        var ledger = new RestoreRecord(path);
        Check(!ledger.IsReadable && !ledger.Add(Record()) && !ledger.Remove(Record().Key));
        Check(File.ReadAllText(path) == corrupt);
        return Task.CompletedTask;
    });
    await Test("directory masquerading as a ledger fails closed", () =>
    {
        var path = NewPath(); Directory.CreateDirectory(path);
        var ledger = new RestoreRecord(path);
        Check(!ledger.IsReadable && !ledger.Add(Record()));
        return Task.CompletedTask;
    });
    await Test("legacy record remains readable for conservative recovery", () =>
    {
        var path = NewPath();
        File.WriteAllText(path, "[{\"Pid\":41,\"Handle\":99,\"App\":\"Code\",\"Label\":\"old\",\"X\":10,\"Y\":20,\"Width\":800,\"Height\":600,\"WasFullScreen\":true,\"WasMinimized\":false}]");
        var ledger = new RestoreRecord(path);
        Check(ledger.IsReadable && ledger.All.Single().Placement is null && ledger.All.Single().AppliedZoom == 0);
        return Task.CompletedTask;
    });
    await Test("automatic zoom never sends keys and reports zero actual steps", () =>
    {
        Check(!WindowService.AutomaticZoomEnabled);
        Check(!WindowService.Zoom(new WindowKey(41, 99), 2));
        Check(WindowService.ApplyZoom(new WindowKey(41, 99), 2) == 0);
        return Task.CompletedTask;
    });
    await Test("journal is durably written before move; default zoom is not applied", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.CheckJournalBeforeMove = () => Check(new RestoreRecord(fake.JournalPath).All.Single() == Record());
        desk.Pull(Window()); await desk.DrainAsync();
        Check(fake.MoveCalls == 1 && desk.State.Placed?.Zoom == 0 && ledger.All.Single().AppliedZoom == 0);
        Check(!desk.State.ListVisible && !desk.State.Placed!.Zoomable);
        desk.ZoomBy(2);
        Check(desk.State.Placed!.Zoom == 0 && ledger.All.Single().AppliedZoom == 0);
        Check(await desk.ShutdownAsync());
        Check(fake.Restored.Single() == Record() && ledger.All.Count == 0);
    });
    await Test("original maximized, minimized and restore-to-maximized placement is preserved", async () =>
    {
        foreach (var (command, flags) in new[] { (3, 0), (2, 0), (2, 2) })
        {
            var (desk, fake, ledger) = await Ready();
            var original = Record().Placement! with { ShowCommand = command, Flags = flags };
            fake.CapturedLook = fake.Look(Window().Key)!.Value with
                { Placement = original, FullScreen = command == 3, Minimized = command == 2 };
            desk.Pull(Window()); await desk.DrainAsync();
            Check(ledger.All.Single().Placement == original);
            Check(await desk.ShutdownAsync());
            Check(fake.Restored.Single().Placement == original);
        }
    });
    await Test("mismatched move is rolled back and never focused or reported placed", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.WrongMove = true;
        desk.Pull(Window()); await desk.DrainAsync();
        Check(desk.State.Placed is null && ledger.All.Count == 0 && fake.FocusCalls == 0);
        Check(fake.Restored.Count == 1 && desk.State.Notice == DeskNotice.WindowUnresponsive);
    });
    await Test("minimum-size fallback requires a subsequent matching move before success", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.AllowWidthChange = true;
        fake.MinWindowWidth = ScreenProfile.FromId("fold").BaseWidth + ScreenProfile.FromId("fold").Step;
        desk.Pull(Window()); await desk.DrainAsync();
        Check(fake.MoveCalls == 2 && desk.State.Placed is not null && !desk.State.ListVisible);
        Check(desk.State.Placed!.Width == fake.MinWindowWidth && desk.State.Placed.Settled!.Value.Width == fake.MinWindowWidth);
        Check(ledger.All.Single() == Record());
        Check(await desk.ShutdownAsync());
    });
    await Test("unsupported minimum-size fallback is rolled back, never recorded as success", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.MinWindowWidth = ScreenProfile.FromId("fold").BaseWidth + ScreenProfile.FromId("fold").Step;
        desk.Pull(Window()); await desk.DrainAsync();
        Check(desk.State.Placed is null && fake.FocusCalls == 0 && ledger.All.Count == 0 && fake.Restored.Count == 1);
    });
    await Test("failed move and failed rollback preserve record and retryable window", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.WrongMove = true; fake.RestoreSucceeds = false;
        desk.Pull(Window()); await desk.DrainAsync();
        Check(desk.State.Placed is not null && ledger.All.Count == 1 && fake.FocusCalls == 0);
        Check(!await desk.ShutdownAsync() && ledger.All.Count == 1 && desk.State.Screen != ScreenState.Off);
        fake.RestoreSucceeds = true;
        Check(await desk.ShutdownAsync() && ledger.All.Count == 0 && desk.State.Screen == ScreenState.Off);
    });
    await Test("successful native restoration but failed durable removal is not completion", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        desk.Pull(Window()); await desk.DrainAsync();
        using (var locked = new FileStream(fake.JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            desk.PutBack(); await desk.DrainAsync();
            Check(desk.State.Placed is not null && ledger.All.Count == 1);
            Check(desk.State.Notice == DeskNotice.WindowUnresponsive);
        }
        Check(await desk.ShutdownAsync() && ledger.All.Count == 0);
    });
    await Test("shutdown recovers every inherited record even without a placed window", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        Check(ledger.Add(Record()));
        Check(ledger.Add(Record() with { Pid = 42, Handle = 100 }));
        Check(await desk.ShutdownAsync());
        Check(fake.Restored.Count == 2 && ledger.All.Count == 0);
    });
    await Test("unrestored inherited record blocks new placement and preserves origin", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        Check(ledger.Add(Record()));
        desk.Pull(Window() with { Key = new WindowKey(42, 100) }); await desk.DrainAsync();
        Check(fake.MoveCalls == 0 && ledger.All.Single() == Record());
    });
    await Test("identity change after original capture prevents movement and restoration", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.IdentityMatches = false;
        desk.Pull(Window()); await desk.DrainAsync();
        Check(fake.MoveCalls == 0 && fake.Restored.Count == 0 && ledger.All.Count == 1);
        Check(!await desk.ShutdownAsync() && ledger.All.Count == 1);
    });
    await Test("failed recovery keeps legacy record instead of removing it", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        Check(ledger.Add(Record() with { Placement = null }));
        fake.RestoreSucceeds = false;
        desk.Recover(); await desk.DrainAsync();
        Check(ledger.All.Count == 1 && fake.Restored.Single().Placement is null);
    });
    await Test("closed original window is forgotten without touching a replacement", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        Check(ledger.Add(Record())); fake.WindowExists = false;
        desk.Recover(); await desk.DrainAsync();
        Check(ledger.All.Count == 0 && fake.MoveCalls == 0 && fake.Restored.Count == 0);
    });
    await Test("switch profile cannot discard a failed restoration", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        desk.Pull(Window()); await desk.DrainAsync();
        fake.RestoreSucceeds = false;
        desk.SetProfile(ScreenProfile.FromId("phone")); await desk.DrainAsync();
        Check(desk.State.Placed is not null && ledger.All.Count == 1);
        Check(desk.State.Notice == DeskNotice.WindowUnresponsive);
    });
    await Test("FIFO queue and awaited shutdown restore in-flight work before completion", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        fake.MoveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        desk.Pull(Window());
        await fake.MoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shutdown = desk.ShutdownAsync();
        Check(!shutdown.IsCompleted);
        desk.Pull(Window() with { Key = new WindowKey(42, 100) }); // 封队列后拒绝新窗口。
        fake.MoveGate.SetResult(true);
        Check(await shutdown.WaitAsync(TimeSpan.FromSeconds(5)));
        Check(fake.MoveCalls == 1 && fake.Restored.Count == 1 && ledger.All.Count == 0);
        Check(desk.State.Screen == ScreenState.Off && desk.State.Placed is null);
        Check(await desk.Shutdown()); // 幂等且等待同一个已成功退出任务。
    });
    await Test("cancelled shutdown reopens the queue for a manual restoration retry", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        desk.Pull(Window()); await desk.DrainAsync(); fake.RestoreSucceeds = false;
        Check(!await desk.ShutdownAsync());
        fake.RestoreSucceeds = true;
        desk.PutBack(); await desk.DrainAsync();
        Check(desk.State.Placed is null && ledger.All.Count == 0);
        Check(await desk.ShutdownAsync());
    });
    await Test("failed detach preserves placed state and pending restoration debt", async () =>
    {
        var (desk, fake, ledger) = await Ready();
        desk.Pull(Window()); await desk.DrainAsync(); fake.RestoreSucceeds = false;
        desk.SetWanted(false); await desk.DrainAsync();
        Check(desk.State.Placed is not null && desk.State.Screen != ScreenState.Off && ledger.All.Count == 1);
    });
    await Test("same-width profile switch changes height in both directions", async () =>
    {
        var (desk, fake, _) = await Ready();
        fake.AllowWidthChange = true;
        desk.SetProfile(ScreenProfile.Phone); await desk.DrainAsync();
        Check(desk.State.Display!.Width == 1080 && desk.State.Display.Height == 1900);
        desk.SetProfile(ScreenProfile.Fold); await desk.DrainAsync();
        Check(desk.State.Display!.Width == 1080 && desk.State.Display.Height == 1191);
    });
    Console.WriteLine($"PASS: {passed} tests. No real user windows, display modes, drivers, or input were touched.");
}
finally { Directory.Delete(root, true); }

async Task Test(string name, Func<Task> run)
{
    await run(); passed++; Console.WriteLine("PASS " + name);
}
string NewPath() => Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
async Task<(PhoneDesk Desk, FakePlatform Fake, RestoreRecord Ledger)> Ready()
{
    var path = NewPath();
    var ledger = new RestoreRecord(path);
    var fake = new FakePlatform { JournalPath = path };
    var desk = new PhoneDesk(new AppConfig { DefaultZoom = 4, Profile = "fold" }, new ZoomBook(NewPath()), ledger, fake, configPath: NewPath());
    desk.SetWanted(true); await desk.DrainAsync();
    Check(desk.State.IsReady);
    return (desk, fake, ledger);
}
static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static WindowInfo Window() => new(new WindowKey(41, 99), "Code", @"C:\Code.exe", "test");
static PhoneRecord Record() => new(41, 99, "Code", Window().Label, 10, 20, 800, 600, false, false,
    new SavedWindowPlacement(0, 1, -1, -1, -1, -1, 10, 20, 810, 620), 0, 12345);

sealed class FakePlatform : IPhoneDeskPlatform
{
    public string JournalPath = "";
    public bool WrongMove, RestoreSucceeds = true, IdentityMatches = true, WindowExists = true;
    public int MoveCalls, FocusCalls;
    public Action? CheckJournalBeforeMove;
    public WindowLook? CapturedLook;
    private WindowLook? _lastMoved;
    public bool AllowWidthChange;
    public int MinWindowWidth;
    private int? _displayWidth;
    private ScreenProfile _profile = ScreenProfile.Fold;
    public List<PhoneRecord> Restored = new();
    public TaskCompletionSource<bool>? MoveGate;
    public TaskCompletionSource<bool> MoveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DisplayInfo? PhoneScreen()
    {
        var profile = _profile;
        var width = _displayWidth ?? profile.BaseWidth;
        var bounds = new Native.RECT { Left = 1920, Top = 0, Right = 1920 + width, Bottom = profile.Height(width) };
        return new DisplayInfo(IntPtr.Zero, "FAKE", "FAKE", "FAKE", bounds, bounds, false, true);
    }
    public bool SetWidth(DisplayInfo display, ScreenProfile profile, int width)
    {
        if (!AllowWidthChange) return false;
        _displayWidth = width; _profile = profile; return true;
    }
    public WindowLook? Look(WindowKey key) => WindowExists ? _lastMoved ?? CapturedLook ?? new WindowLook(
        new Native.RECT { Left = 10, Top = 20, Right = 810, Bottom = 620 }, false, false,
        new SavedWindowPlacement(0, 1, -1, -1, -1, -1, 10, 20, 810, 620), 12345) : null;
    public bool Exists(WindowKey key) => WindowExists;
    public bool MatchesIdentity(PhoneRecord record) => WindowExists && IdentityMatches;
    public async Task<Native.RECT?> MoveAsync(WindowKey key, Native.RECT area)
    {
        CheckJournalBeforeMove?.Invoke();
        MoveCalls++; MoveStarted.TrySetResult(true);
        if (MoveGate is not null) await MoveGate.Task;
        if (MinWindowWidth > area.Width)
        {
            _lastMoved = Look(key)!.Value with { Frame = area with { Right = area.Left + MinWindowWidth } };
            return null; // 和真实 MoveAsync 一样，不匹配就失败，诊断需另读 Look。
        }
        return WrongMove ? area with { Left = area.Left + 100 } : area;
    }
    public Task<bool> RestoreAsync(PhoneRecord record) { Restored.Add(record); return Task.FromResult(RestoreSucceeds); }
    public void Focus(WindowKey key) => FocusCalls++;
}
