using System.Runtime.InteropServices;
using PocketDesk.Core;
using PocketDesk.Core.PhoneScreen;
using PocketDesk.Win;

if (args.Contains("--inspect"))
{
    Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);
    foreach (var screen in DisplayService.Monitors())
    {
        Console.WriteLine($"SCREEN {screen} primary={screen.IsPrimary} phone={screen.IsPhoneScreen} bounds={screen.Bounds} work={screen.Work}");
        if (screen.IsPhoneScreen)
            Console.WriteLine("MODES " + string.Join(";", DisplayService.AvailableModes(screen).Select(m => $"{m.Width}x{m.Height}")));
    }
    var (windows, _) = WindowService.List();
    foreach (var window in windows.Where(w => w.App.Equals("PocketDesk", StringComparison.OrdinalIgnoreCase)
        || w.Title.Contains("PocketDesk") || w.Title.Contains("验收")))
    {
        Native.GetWindowRect((IntPtr)window.Key.Handle, out var rect);
        Console.WriteLine($"WINDOW {window.Label} {window.Key} rect={rect}");
    }
    return;
}
var count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name); count++;
}
Check(Marshal.SizeOf<DisplayService.DisplayMode>() == 220, "DEVMODEW size 220");
Check(Marshal.OffsetOf<DisplayService.DisplayMode>("Width").ToInt32() == 172, "width offset");
Check(Marshal.OffsetOf<DisplayService.DisplayMode>("Height").ToInt32() == 176, "height offset");
Check(DisplayService.DisplayMode.Create().Size == 220, "mode is initialized");
Check(ScreenProfile.Fold.Size(1080) == (1080, 1191), "N5 unfolded aspect");
Check(ScreenProfile.Phone.Size(1080) == (1080, 1900), "portrait aspect");
Check(ScreenProfile.Fold.HomeBarHeight(1080) == 80, "touch-sized bottom bar");
var modes = new[] {(800, 600), (1080, 1900), (1080, 1191), (1240, 1368)};
Check(DisplayService.SelectSupportedMode(ScreenProfile.Fold, 1080, modes) == (1080,1191), "select exact fold mode");
Check(DisplayService.SelectSupportedMode(ScreenProfile.Fold, 1100, modes) == (1240,1368), "select only advertised wider mode");
Check(DisplayService.SelectSupportedMode(ScreenProfile.Phone, 1080, modes) == (1080,1900), "do not mix aspect ratios");
Check(DisplayService.SelectSupportedMode(ScreenProfile.Fold, 1080, new[] {(1920,1080)}) is null, "reject wrong aspect");
var display = new DisplayInfo(IntPtr.Zero, "test", "test", "test", new Native.RECT {Left=3840,Top=0,Right=4920,Bottom=1191},
    new Native.RECT {Left=3840,Top=0,Right=4920,Bottom=1151}, false,true);
var area = DisplayService.WindowArea(display, 80);
Check(area.Bottom == 1071 && area.Top == 0 && area.Width ==1080, "reserve taskbar plus return bar");
Check(new AppConfig().Profile == "fold" && new AppConfig().KeepScreenActive, "N5 and manual persistent defaults");
Check(new AppConfig().DefaultZoom == 0 && !new AppConfig().ExperimentalGestures, "safe zoom and gesture defaults");
var directory=Path.Combine(Path.GetTempPath(), "pocketdesk-config-tests-" + Guid.NewGuid());
try
{
    Directory.CreateDirectory(directory);
    var file=Path.Combine(directory,"config.json");
    var config=new AppConfig { Profile="phone", PhoneScreenEnabled=false };
    config.Save(file);
    var read=AppConfig.Load(file);
    Check(read.Profile=="phone" && !read.PhoneScreenEnabled, "settings round-trip");
    File.WriteAllText(file,"{\"Profile\":\"invalid\",\"DefaultZoom\":999}");
    read=AppConfig.Load(file);
    Check(read.Profile=="phone" && read.DefaultZoom==8, "validate persisted settings");
}
finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
Console.WriteLine($"{count} passed");
