// 手机屏的形态。macOS 版只做 iPhone 竖屏（540×950 点）；Windows 版多一档：
// 折叠屏展开后是接近方形的屏（OPPO Find N5 内屏 2248×2480），比例完全不同，
// 沿用竖屏比例会把画面压成细长条，白丢一半可用面积。
// 每种形态只描述宽高比与基准宽度，具体档位由 ScreenFit 推导。
namespace PocketDesk.Core.PhoneScreen;

public sealed record ScreenProfile(string Id, string Name, int AspectWidth, int AspectHeight, int BaseWidth, int MaxWidth, int Step)
{
    /// <summary>iPhone 竖屏：540×950 点，对应 1080×1900 像素。</summary>
    public static readonly ScreenProfile Phone = new("phone", "手机竖屏", 540, 950, 1080, 2160, 20);

    /// <summary>
    /// 折叠屏展开：OPPO Find N5 内屏 2248×2480，同样落在 1080 宽的基准上（高 1191）。
    /// 其它品牌的展开态比例略有差别，但 9:9.9 附近都可以用这一档。
    /// </summary>
    public static readonly ScreenProfile Fold = new("fold", "OPPO Find N5 展开",  2248, 2480, 1080, 2160, 20);

    public static readonly IReadOnlyList<ScreenProfile> All = new[] { Phone, Fold };

    public static ScreenProfile FromId(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Phone;

    /// <summary>这一档的宽高（物理像素）：高按宽高比算。</summary>
    public (int Width, int Height) Size(int width) => (width, Height(width));

    public int Height(int width) => (int)Math.Round(width * (double)AspectHeight / AspectWidth);

    public double Scale(int width) => width / (double)BaseWidth;

    public int HomeBarHeight(int width) => (int)Math.Round(80 * Scale(width));
}
