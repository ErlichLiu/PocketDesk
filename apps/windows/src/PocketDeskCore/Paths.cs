// 本机数据的位置与设置。与 macOS 版一一对应：
//   %LOCALAPPDATA%\PocketDesk\config.json         设置
//   %LOCALAPPDATA%\PocketDesk\phone-screen.json   手机屏上窗口的还原记录
//   %LOCALAPPDATA%\PocketDesk\zoom.json           各 App 记忆的字号级数
//   %LOCALAPPDATA%\PocketDesk\pocketdesk.log            日志
namespace PocketDesk.Core;

public static class PocketDeskPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketDesk");

    public static string Config => Path.Combine(Root, "config.json");
    public static string PhoneRecord => Path.Combine(Root, "phone-screen.json");
    public static string ZoomBook => Path.Combine(Root, "zoom.json");
    public static string LogFile => Path.Combine(Root, "pocketdesk.log");
}

public sealed class AppConfig
{
    /// <summary>设置里的默认字号级数：App 没记过用它的。</summary>
    public int DefaultZoom { get; set; } = 0;

    /// <summary>总开关：关掉后不再为 UU 连接创建手机屏。</summary>
    public bool PhoneScreenEnabled { get; set; } = true;

    /// <summary>手机屏形态：phone（手机竖屏）或 fold（折叠屏展开）。</summary>
    public string Profile { get; set; } = "fold";

    /// <summary>Windows 版暂缺可靠断连信号，默认手动常驻，不因静止不操作而误还原。</summary>
    public bool KeepScreenActive { get; set; } = true;

    /// <summary>无法可靠区分虚拟 HID 来源，默认保持 UU 原生手势。</summary>
    public bool ExperimentalGestures { get; set; } = false;

    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppConfig();
            var json = File.ReadAllText(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            config.DefaultZoom = Math.Clamp(config.DefaultZoom, -4, 8);
            config.Profile = PhoneScreen.ScreenProfile.FromId(config.Profile).Id;
            return config;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"设置读不出来，用默认值：{e.Message}");
            return new AppConfig();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"设置写不进去：{e.Message}");
        }
    }
}
