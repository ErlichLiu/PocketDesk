// 控制端是什么设备：只看 UU 表格里的名字。手机、平板才加手机屏；电脑控制端不加。
// 同时连着几台时，有一台要就加：电脑那边照样看原来的屏，只是 UU 的屏幕列表里多一项。
// 读不到名字、认不出设备时都按手机处理——手机屏只是 UU 里多一个可选项，宁可多给，不要让手机用户等不到。
namespace PocketDesk.Core.Session;

public enum ControllerKind
{
    Mobile,
    Computer,
    Unknown,
}

public static class ControllerKindRules
{
    /// <summary>刚连上时控制端表格要慢一两拍才读到，先等这么久再按手机处理。</summary>
    public static readonly TimeSpan NameGrace = TimeSpan.FromSeconds(2);

    private static readonly string[] MobileHints =
        { "iphone", "ipad", "matepad", "android", "phone", "手机", "平板" };

    /// <summary>单独成词的「pad」才算平板（OPPO Pad）：ThinkPad 是笔记本。</summary>
    private const string TabletWord = "pad";

    private static readonly string[] ComputerHints =
    {
        "mac", "desktop-", "thinkpad", "pc", "windows", "matebook", "magicbook", "redmibook",
        "xiaomi book", "galaxy book", "surface", "电脑", "笔记本", "台式",
    };

    private static readonly HashSet<string> ComputerWords = new(StringComparer.Ordinal) { "laptop", "notebook" };

    public static ControllerKind Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return ControllerKind.Unknown;
        var n = name.ToLowerInvariant();
        var words = n.Split(new[] { ' ', '-', '_', '(', ')', '（', '）', '.', '/', '\\' },
                            StringSplitOptions.RemoveEmptyEntries);

        if (MobileHints.Any(n.Contains) || words.Contains(TabletWord)) return ControllerKind.Mobile;
        if (ComputerHints.Any(n.Contains) || words.Any(ComputerWords.Contains)) return ControllerKind.Computer;
        return ControllerKind.Unknown;
    }

    public static bool WantsPhoneScreen(ControllerKind kind) => kind != ControllerKind.Computer;

    /// <summary>同时连着的几台控制端要不要手机屏：全是电脑才不要。</summary>
    public static bool WantsPhoneScreen(IReadOnlyList<string> names) =>
        names.Count == 0 || names.Any(n => WantsPhoneScreen(Parse(n)));
}
