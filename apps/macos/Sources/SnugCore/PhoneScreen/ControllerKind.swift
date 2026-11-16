// 控制端是什么设备：只看 UU 表格里的名字。手机、平板才加手机屏；电脑控制端不加。
// 未知类型不授权；同时连入时至少一台明确手机/平板才通过设备类型条件。
// Name classification alone is not local control evidence; automatic activation also needs a scoped local table and service presence.
import Foundation

public enum ControllerKind: Equatable, Sendable {
    case mobile, computer, unknown

    private static let mobileHints = ["iphone", "ipad", "matepad", "android", "手机", "平板", "find n5", "findn5", "pkh110", "cph2671"]
    /// 单独成词的「pad」才算平板（OPPO Pad、Xiaomi Pad）：ThinkPad 是笔记本
    private static let tabletWord = "pad"
    /// Windows 默认的机器名是 DESKTOP-xxxx / LAPTOP-xxxx；Mac 的名字里带 Mac（MacBook、iMac、Mac mini）；
    /// 国产和其它品牌的笔记本常用出厂名（HUAWEI MateBook、HONOR MagicBook、RedmiBook、Galaxy Book、Surface）
    private static let computerHints = ["mac", "desktop-", "thinkpad", "pc", "windows", "matebook", "magicbook", "redmibook",
                                        "xiaomi book", "galaxy book", "surface", "电脑", "笔记本", "台式"]
    /// 单独成词才算（LAPTOP-AB12、Surface Laptop 5）
    private static let computerWords: Set<Substring> = ["laptop", "notebook"]

    public init(name: String?) {
        guard let n = name?.lowercased(), !n.isEmpty else {
            self = .unknown
            return
        }
        let words = n.split { !$0.isLetter && !$0.isNumber }
        let mobile = Self.mobileHints.contains(where: n.contains)
            || words.contains(Substring(Self.tabletWord)) || words.contains("phone")
        let computer = Self.computerHints.contains(where: n.contains) || words.contains(where: Self.computerWords.contains)
        // 冲突的自定义名称不猜设备类型；phone 只认独立词，避免 Headphones 等词误判。
        switch (mobile, computer) {
        case (true, false): self = .mobile
        case (false, true): self = .computer
        default: self = .unknown
        }
    }

    public var wantsPhoneScreen: Bool { self == .mobile }

    /// 至少一台明确手机/平板；空集合、未知设备均不授权
    public static func wantsPhoneScreen(_ names: [String]) -> Bool {
        names.contains { ControllerKind(name: $0).wantsPhoneScreen }
    }
}
