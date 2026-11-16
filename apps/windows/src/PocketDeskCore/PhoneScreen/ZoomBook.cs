// 旧版字号设置的兼容存储；当前默认不发送全局快捷键，也不改系统 DPI。
namespace PocketDesk.Core.PhoneScreen;

public sealed class ZoomBook
{
    /// <summary>支持 Ctrl+= / Ctrl+- 调字号的程序（按可执行文件名，不含路径）。</summary>
    public static readonly HashSet<string> ZoomableApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "Code", "Cursor", "WezTerm", "alacritty", "Zed", "windsurf",
    };

    public const int MinSteps = -4;
    public const int MaxSteps = 8;

    private readonly string _path;
    private readonly Dictionary<string, int> _remembered = new(StringComparer.OrdinalIgnoreCase);

    public ZoomBook(string path)
    {
        _path = path;
        Load();
    }

    public static bool IsZoomable(string? exePath) =>
        exePath is not null && ZoomableApps.Contains(Path.GetFileNameWithoutExtension(exePath));

    /// <summary>放上手机屏时该放大几级；不支持调字号的 App 返回 0。</summary>
    public int StepsFor(string? exePath, int fallback)
    {
        if (!IsZoomable(exePath)) return 0;
        var name = Path.GetFileNameWithoutExtension(exePath)!;
        return Math.Clamp(_remembered.GetValueOrDefault(name, fallback), MinSteps, MaxSteps);
    }

    public void Remember(int steps, string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        if (name is null) return;
        _remembered[name] = Math.Clamp(steps, MinSteps, MaxSteps);
        Save();
    }

    /// <summary>设置里改了默认级数：各 App 记住的一起清掉，以新的默认为准。</summary>
    public void ForgetAll()
    {
        _remembered.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (loaded is null) return;
            foreach (var (k, v) in loaded) _remembered[k] = Math.Clamp(v, MinSteps, MaxSteps);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"字号记录读不出来，按空处理：{e.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(_remembered));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"字号记录写不进去：{e.Message}");
        }
    }
}
