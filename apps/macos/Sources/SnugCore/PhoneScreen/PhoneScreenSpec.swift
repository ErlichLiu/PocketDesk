// 手机屏的独立身份与继承的物理参数。尺寸和档位归 ScreenFit；新身份尚未做真实显示器验证。
// 通用、Find N5外屏/展开模式在同一目录预登记；选布局只换模式，不重建当前虚拟屏。
import Foundation

public enum PhoneScreenSpec {
    /// 在「显示器」设置和 UU远程 的屏幕列表里看到的名字
    public static let name = "PocketDesk 手机屏"
    /// 'PD'：独立于上游 Snug 的屏幕身份，不接管其虚拟屏或系统缓存
    public static let vendorID: UInt32 = 0x5044
    /// 模式目录v2：含折叠屏比例，使用新身份避免复用0.1.0的旧模式缓存。
    public static let productID: UInt32 = 0x0002
    /// 建好不到这么久就释放会让 WindowServer 卡死（原作者实测）
    public static let minimumLifetime: TimeInterval = 2.5

    /// 物理尺寸按像素上限、约 220ppi 折算。按手机的真实密度（460ppi）算，全新身份的屏永远不上线，
    /// 而且这个身份从此被系统「拉黑」，改回来也不行
    public static let millimeters = (width: PhoneLayout.maximumPixels.width / 220 * 25.4, height: PhoneLayout.maximumPixels.height / 220 * 25.4)

    /// 身份（序列号）：固定一个，建了不上线就依次换备用的。
    /// 固定身份能让系统记住这块屏的设置；每多用一个新身份，系统就多存一份 ICC 文件。
    /// 使用独立于 Snug 的全新序列，避免复用其系统缓存；这组标识尚未实机验证上线。
    public static let serials: [UInt32] = [310_000_201, 310_000_211, 310_000_221]

    /// 帮手只能切自己的屏；不能只凭屏幕编号、分辨率或相同vendor判断归属。
    public static func owns(vendorID: UInt32, productID: UInt32, serial: UInt32) -> Bool {
        vendorID == Self.vendorID && productID == Self.productID && serials.contains(serial)
    }
}
