using Application = System.Windows.Application;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using PocketDesk.Core;

namespace PocketDesk.App;

public partial class App : Application
{
    private AppController? _controller;
    private Mutex? _instance;
    private bool _ownsMutex;
    private readonly CancellationTokenSource _stop = new();
    private const string PipeName = "PocketDesk.Workspace.Control.v2";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.UsePath(PocketDeskPaths.LogFile);
        if (e.Args.Contains("--quit"))
        {
            _ = SendCommandAsync("quit").ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }
        using (var current = System.Diagnostics.Process.GetCurrentProcess())
        {
            foreach (var legacy in System.Diagnostics.Process.GetProcessesByName(LegacyDataMigration.LegacyName))
            {
                using (legacy)
                {
                    if (legacy.SessionId != current.SessionId) continue;
                    System.Windows.MessageBox.Show("请先从旧版 Snug 的设置中安全退出，再启动 PocketDesk，避免两个程序同时搬移窗口。", "PocketDesk 升级");
                    Shutdown();
                    return;
                }
            }
        }
        var legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyDataMigration.LegacyName);
        if (!LegacyDataMigration.TryMigrate(legacyRoot, PocketDeskPaths.Root, out var migrationError))
        {
            System.Windows.MessageBox.Show(migrationError, "PocketDesk 升级");
            Shutdown();
            return;
        }
        try { _instance = new Mutex(true, "Local\\PocketDesk.Workspace.Instance.v2", out _ownsMutex); }
        catch (UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show("已有另一权限级别的 PocketDesk 在运行，请使用相同权限打开设置或先退出原实例。", "PocketDesk 已运行");
            Shutdown();
            return;
        }
        if (!_ownsMutex)
        {
            _ = SendCommandAsync("settings").ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }
        PocketDesk.Win.Native.SetProcessDpiAwarenessContext(PocketDesk.Win.Native.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error($"界面异常，准备退出并保留恢复记录：{args.Exception}");
            args.Handled = true;
            _controller?.Quit();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error($"后台未处理异常：{args.ExceptionObject}");
        var quiet = e.Args.Contains("--quiet") || e.Args.Contains("--force-phone");
        _controller = new AppController(!quiet, e.Args.Contains("--force-phone"));
        _controller.Start();
        _ = ListenAsync();
        Log.Info($"PocketDesk 实验版已启动 pid={Environment.ProcessId} build={typeof(App).Assembly.GetName().Version}");
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                // 只接受当前用户的控制请求，不向本机其它用户暴露退出能力。
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var command = await reader.ReadLineAsync(timeout.Token);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (command == "quit") _controller?.Quit();
                    else if (command == "settings") _controller?.OpenSettings();
                });
            }
            catch (OperationCanceledException) { if (_stop.IsCancellationRequested) return; }
            catch (IOException error) { Log.Warn($"控制连接已断开：{error.Message}"); }
        }
    }

    private static async Task SendCommandAsync(string command)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(command);
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        { Log.Warn($"无法连接已运行的 PocketDesk：{error.Message}"); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stop.Cancel();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        // 退出仅由控制器在 await 还原后触发，不从 OnExit 递归调用 Shutdown。
        base.OnExit(e);
    }
}
