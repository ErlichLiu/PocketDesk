// 手动选择远控工作区比例；设备名称、推流事件都不代表手机已展开。
// Find N5 官方物理像素：内屏2248×2480，外屏1140×2616。
// https://www.oppo.com/cn/smartphones/series-find-n/find-n5/specs/
import CoreGraphics

public struct PhoneDisplayMode: Hashable, Sendable {
    public let width: Int
    public let height: Int
    public init(width: Int, height: Int) { self.width = width; self.height = height }
    public var size: CGSize { CGSize(width: width, height: height) }
}

public enum PhoneLayout: String, Codable, CaseIterable, Sendable {
    case standard
    case findN5Cover
    case findN5Unfolded

    public var title: String {
        switch self {
        case .standard: "通用竖屏"
        case .findN5Cover: "Find N5 · 外屏"
        case .findN5Unfolded: "Find N5 · 展开"
        }
    }

    public var baseWidth: Int {
        switch self {
        case .standard: 540
        case .findN5Cover: 570
        case .findN5Unfolded: 1080
        }
    }

    public var widths: [Int] {
        switch self {
        case .standard: Array(stride(from: 540, through: 1080, by: 10))
        case .findN5Cover: Array(stride(from: 570, through: 810, by: 20))
        case .findN5Unfolded: Array(stride(from: 1080, through: 1440, by: 40))
        }
    }

    public func size(width: Int) -> CGSize {
        let ratio: CGFloat = switch self {
        case .standard: 950.0 / 540
        case .findN5Cover: 2616.0 / 1140
        case .findN5Unfolded: 2480.0 / 2248
        }
        return CGSize(width: CGFloat(width), height: (CGFloat(width) * ratio).rounded())
    }

    public var modes: [PhoneDisplayMode] {
        widths.map { PhoneDisplayMode(width: $0, height: Int(size(width: $0).height)) }
    }

    /// 稳定去重；同宽不同高的模式分别登记，不把展开与竖屏混成同一档。
    public static var allModes: [PhoneDisplayMode] {
        var seen = Set<PhoneDisplayMode>()
        return allCases.flatMap(\.modes).filter { seen.insert($0).inserted }
    }

    public static var maximumPixels: CGSize {
        CGSize(width: (allModes.map(\.width).max() ?? 1080) * 2,
               height: (allModes.map(\.height).max() ?? 1900) * 2)
    }

    /// 真实屏幕可能仍是切换失败前的模式，底栏按实际比例布局，不按尚未生效的偏好猜。
    public static func matching(size: CGSize) -> PhoneLayout? {
        allCases.first { layout in
            layout.modes.contains { abs(CGFloat($0.width) - size.width) < 1 && abs(CGFloat($0.height) - size.height) < 1 }
        }
    }

    public static func supports(width: Int, height: Int) -> Bool {
        // 先比较整数，不对外部输入做尺寸放大或CGFloat→Int转换，避免溢出。
        allModes.contains(PhoneDisplayMode(width: width, height: height))
    }
}
