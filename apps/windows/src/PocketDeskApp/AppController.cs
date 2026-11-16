using Application = System.Windows.Application;
using System.Windows;
using System.Windows.Threading;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

namespace PocketDesk.App;

public sealed class AppController
{
    private readonly AppConfig _config = AppConfig.Load(PocketDeskPaths.Config);
    private readonly ZoomBook _zoomBook = new(PocketDeskPaths.ZoomBook);
    private readonly RestoreRecord _ledger = new(PocketDeskPaths.PhoneRecord);
    private readonly PhoneDesk _desk;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly DispatcherTimer _listTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _verifyTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private PhoneListWindow? _list;
    private HomeBarWindow? _bar;
    private IReadOnlyList<WindowInfo> _lastWindows = Array.Empty<WindowInfo>();
    private bool _listShowing;
    private bool _refreshing;
    private bool _quitting;
    private readonly bool _showSettingsOnStart;

    public AppController(bool showSettingsOnStart, bool forcePhoneScreen = false)
    {
        _showSettingsOnStart = showSettingsOnStart;
        if (forcePhoneScreen) _config.KeepScreenActive = true;
        _desk = new PhoneDesk(_config, _zoomBook, _ledger);
        _desk.Changed += OnDeskChanged;
        _listTimer.Tick += async (_, _) => await RefreshWindowsAsync(false);
        _verifyTimer.Tick += (_, _) => _desk.Verify();
    }

    public event Action? StatusChanged;
    public bool PhoneScreenEnabled => _config.PhoneScreenEnabled;
    public int DefaultZoom => _config.DefaultZoom;
    public string ProfileId => _config.Profile;
    public bool IsQuitting => _quitting;
    public string StatusText => !_config.PhoneScreenEnabled ? "手机工作区已关闭"
        : _desk.State.IsReady ? "手机工作区已开启 · 手动常驻" : "等待可用的虚拟显示器";
    public string ScreenText => _desk.State.Display is { } display
        ? $"{display.AdapterName} · {display.DeviceName}\n{display.Width} × {display.Height} · {ScreenProfile.FromId(ProfileId).Name}"
        : "未发现唯一的非主虚拟屏。保留现有显示设置，不更改主屏。";
    public string NoticeText => _desk.State.Notice switch
    {
        DeskNotice.None => "手势保持 UU 原生行为。连接静止时不会误关工作区。",
        DeskNotice.WindowGone => "目标窗口已关闭，未移动其它窗口。",
        DeskNotice.WindowUnresponsive => "窗口未到达目标位置或无法还原，恢复记录已保留。",
        DeskNotice.RecordUnwritable => "无法保存恢复记录，未继续移动窗口。",
        DeskNotice.ScreenNotReady => "虚拟屏尚不可用，请稍后重试。",
        _ => "操作未完成，请查看日志。",
    };

    public void Start()
    {
        _config.DefaultZoom = 0;
        _config.ExperimentalGestures = false;
        _config.KeepScreenActive = true;
        _config.Save(PocketDeskPaths.Config);
        _tray = new TrayIcon(ShowSettings, () => SetPhoneScreenEnabled(!_config.PhoneScreenEnabled), Quit,
            () => _config.PhoneScreenEnabled, () => StatusText);
        // Windows 端缺少可靠的 UU 断连与输入来源信号：本版明确使用手动常驻，绝不按“无人操作”判断断连。
        _desk.Recover();
        _desk.SetWanted(_config.PhoneScreenEnabled);
        _verifyTimer.Start();
        if (_showSettingsOnStart) ShowSettings();
    }

    public void SetPhoneScreenEnabled(bool enabled)
    {
        _config.PhoneScreenEnabled = enabled;
        _config.Save(PocketDeskPaths.Config);
        _desk.SetWanted(enabled);
        PublishStatus();
    }

    public void SetProfile(ScreenProfile profile) => _desk.SetProfile(profile);
    public void SetDefaultZoom(int steps)
    {
        _config.DefaultZoom = steps;
        _config.Save(PocketDeskPaths.Config);
        _zoomBook.ForgetAll();
        PublishStatus();
    }

    public void OpenSettings() => ShowSettings();

    private void ShowSettings()
    {
        if (_settings is { IsVisible: true }) { _settings.Activate(); return; }
        _settings = new SettingsWindow(this);
        _settings.Show();
    }

    private void OnDeskChanged(DeskState state)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() =>
        {
            if (_quitting) return;
            SyncPanels(_desk.State);
            PublishStatus();
        }));
    }

    private void PublishStatus()
    {
        _tray?.SetStatus(StatusText);
        StatusChanged?.Invoke();
    }

    private void SyncPanels(DeskState state)
    {
        if (state.Display is not { } display || !state.IsReady)
        {
            _listTimer.Stop();
            _listShowing = false;
            _list?.Hide();
            _bar?.Hide();
            return;
        }
        EnsurePanels();
        var showList = state.ListVisible || state.Placed is null;
        if (showList)
        {
            _bar!.Hide();
            _list!.SetState(state, ScreenProfile.FromId(ProfileId), NoticeText);
            _list.FitTo(display.Bounds);
            var first = !_listShowing;
            _listShowing = true;
            _listTimer.Start();
            _ = RefreshWindowsAsync(first);
        }
        else
        {
            _listTimer.Stop();
            _listShowing = false;
            _list!.Hide();
            var work = display.Work;
            var height = ScreenProfile.FromId(ProfileId).HomeBarHeight(display.Width);
            var bar = new Native.RECT
            {
                Left = Math.Max(display.Bounds.Left, work.Left), Top = Math.Min(display.Bounds.Bottom, work.Bottom) - height,
                Right = Math.Min(display.Bounds.Right, work.Right), Bottom = Math.Min(display.Bounds.Bottom, work.Bottom),
            };
            _bar!.FitTo(bar);
        }
    }

    private void EnsurePanels()
    {
        if (_list is null)
        {
            _list = new PhoneListWindow();
            _list.WindowChosen += window => _desk.Pull(window);
            _list.ReturnRequested += () => _desk.ReturnToPlaced();
            _list.RestoreRequested += () => _desk.PutBack();
            _list.SettingsRequested += ShowSettings;
        }
        if (_bar is null)
        {
            _bar = new HomeBarWindow();
            _bar.Tapped += () => _desk.ShowList();
            _bar.RestoreRequested += () => _desk.PutBack();
        }
    }

    private async Task RefreshWindowsAsync(bool reorder)
    {
        if (_refreshing || _quitting || _list is not { IsVisible: true }) return;
        _refreshing = true;
        try
        {
            var (windows, order) = await Task.Run(WindowService.List);
            if (_quitting || _list is not { IsVisible: true }) return;
            var ordered = WindowList.Ordered(windows, order);
            var next = reorder ? ordered : WindowList.KeepingOrder(_lastWindows, ordered);
            if (next.SequenceEqual(_lastWindows)) return;
            _lastWindows = next;
            _list.ShowRows(next);
        }
        catch (Exception error) { Log.Error($"读取窗口列表失败：{error}"); }
        finally { _refreshing = false; }
    }

    public async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _listTimer.Stop();
        _verifyTimer.Stop();
        try
        {
            if (!await _desk.ShutdownAsync())
            {
                _quitting = false;
                System.Windows.MessageBox.Show("窗口未能完全还原。PocketDesk 已保留恢复记录并取消退出，请关闭占用窗口后重试。",
                    "PocketDesk：尚未退出", MessageBoxButton.OK, MessageBoxImage.Warning);
                PublishStatus();
                return;
            }
            _tray?.Dispose();
            _list?.Close();
            _bar?.Close();
            _settings?.Close();
            Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Log.Error($"退出前还原失败：{error}");
            _quitting = false;
            PublishStatus();
        }
    }
}
