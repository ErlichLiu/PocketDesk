// 屏幕适配：手机屏的宽度跟着屏上的窗口走。
// macOS 版用「点」，短边 540 点是 2 倍高清门槛；Windows 的虚拟屏直接按物理像素登记，缩放固定 200%，
// 于是屏幕的物理宽度就是 macOS 版点数的 2 倍：540 点 → 1080 像素，1080 点 → 2160 像素。
// 宽高比由 ScreenProfile 决定（手机竖屏 / 折叠屏展开），不按 App 预设：
// 放窗口时先按基准宽度铺，App 卡在自己的最小尺寸上，读回来的大小就是它要的，据此选档。
namespace PocketDesk.Core.PhoneScreen;

public static class ScreenFit
{
    /// <summary>屏幕的缩放比例：物理像素 / 逻辑点。</summary>
    public const double Scaling = 2.0;

    /// <summary>某一档位下、某个形态的全部宽度档，从基准到最宽。</summary>
    public static IReadOnlyList<int> Widths(ScreenProfile profile)
    {
        var list = new List<int>();
        for (var w = profile.BaseWidth; w <= profile.MaxWidth; w += profile.Step) list.Add(w);
        return list;
    }

    /// <summary>
    /// 窗口该用哪一档。window 是按 area（当前这一档的可放区域）铺过之后读回的实际大小：
    /// 装得下就还是 current；App 不让缩到那么小，就按超出的比例往上找装得下的最窄一档，
    /// 超过最宽一档就用最宽一档。
    /// </summary>
    public static int WidthToFit(ScreenProfile profile, int windowWidth, int windowHeight,
                                 int areaWidth, int areaHeight, int current)
    {
        const int tolerance = 2;
        if (windowWidth <= areaWidth + tolerance && windowHeight <= areaHeight + tolerance) return current;

        var needed = Math.Max(current * (double)windowWidth / areaWidth,
                              current * (double)windowHeight / areaHeight);
        var rounded = (int)Math.Ceiling(needed);
        foreach (var w in Widths(profile)) if (w >= rounded) return w;
        return profile.MaxWidth;
    }
}
