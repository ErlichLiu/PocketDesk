// 启动参数一次解析成启动模式，main.swift 只按模式组装。
// 只认双横线参数；单横线的（-psn_…、-AppleLanguages …）是系统或 Cocoa 塞进来的，忽略。
// 未知的双横线参数直接报错：拼错一个参数时宁可不启动，也不要悄悄按正常模式开始监控
import Foundation

public enum RenderTarget: String, CaseIterable, Equatable, Sendable {
    case settings, welcome, about, legacy
    /// 查到新版本时的更新窗口（几种状态）
    case update
    /// 手机屏上的窗口列表（几种状态）
    case phone
}

public enum LaunchMode: Equatable, Sendable {
    case app
    /// 正常启动，但不等手机连上，直接加手机屏（开发自测用：没有手机也能看手机屏和上面的列表）
    case previewPhone
    /// 只把界面渲染成 PNG
    case render(RenderTarget, output: URL)
    /// 打印从 UU远程 读到的控制端与屏幕
    case dumpUU
    /// 只读首屏目录基准：不建控制器、不启用监控、不申请权限。
    case benchmarkWindows
    /// 帮手模式：把手机屏换到 width 那一档（2 倍）后立即退出。
    /// 主程序亲自切模式，这块虚拟屏释放时会拖 40 秒以上才下线；交给短命的子进程切就没有这个问题
    case setMode(display: DisplayID, width: Int)
    /// 布局帮手：只接收已登记的完整宽高组合，不能通过任意参数修改外部显示器。
    case setSize(display: DisplayID, width: Int, height: Int)

    public static let usage = """
    用法：PocketDesk [模式]
      （无参数）                      正常启动
      --preview-phone                 正常启动，不等手机连上就加手机屏（开发自测用）
      --render-<界面> <目录>           把界面渲染成 PNG（界面：\(RenderTarget.allCases.map(\.rawValue).joined(separator: " / "))）
      --dump-uu                       打印从 UU远程 读到的表格行、控制端与屏幕
      --benchmark-windows             只读窗口目录基准，不建屏、不移动窗口
      --set-mode <屏幕编号> <宽>        原通用竖屏帮手（内部用）
      --set-size <屏幕编号> <宽> <高>   切换已登记布局（点，2 倍；内部用）
    """

    public static func parse(_ arguments: [String]) throws -> LaunchMode {
        var rest = arguments.filter { $0.hasPrefix("--") || !$0.hasPrefix("-") }[...]
        var mode: (flag: String, value: LaunchMode)?
        while let flag = rest.popFirst() {
            guard flag.hasPrefix("--") else { continue }
            let value = try parseMode(flag, &rest)
            if let first = mode { throw LaunchModeError.conflicting(first.flag, flag) }
            mode = (flag, value)
        }
        return mode?.value ?? .app
    }

    private static func parseMode(_ flag: String, _ rest: inout ArraySlice<String>) throws -> LaunchMode {
        func value() throws -> String {
            guard let v = rest.first, !v.hasPrefix("--") else { throw LaunchModeError.missingValue(flag) }
            rest.removeFirst()
            return v
        }
        func number<N: FixedWidthInteger>(_: N.Type) throws -> N {
            let raw = try value()
            guard let n = N(raw), n > 0 else { throw LaunchModeError.invalidNumber(flag, raw) }
            return n
        }
        if flag.hasPrefix("--render-"), let target = RenderTarget(rawValue: String(flag.dropFirst("--render-".count))) {
            return .render(target, output: URL(fileURLWithPath: try value()))
        }
        switch flag {
        case "--preview-phone":
            return .previewPhone
        case "--dump-uu":
            return .dumpUU
        case "--benchmark-windows":
            return .benchmarkWindows
        case "--set-size":
            let display = try number(DisplayID.self)
            let width = try number(Int.self), height = try number(Int.self)
            guard PhoneLayout.supports(width: width, height: height) else { throw LaunchModeError.unsupportedSize(width, height) }
            return .setSize(display: display, width: width, height: height)
        case "--set-mode":
            let display = try number(DisplayID.self)
            let width = try number(Int.self)
            guard ScreenFit.widths.contains(width) else { throw LaunchModeError.unsupportedWidth(width) }
            return .setMode(display: display, width: width)
        default:
            throw LaunchModeError.unknownFlag(flag)
        }
    }
}

public enum LaunchModeError: Error, Equatable, Sendable, CustomStringConvertible {
    case unknownFlag(String)
    case missingValue(String)
    case invalidNumber(String, String)
    case conflicting(String, String)
    case unsupportedWidth(Int)
    case unsupportedSize(Int, Int)

    public var description: String {
        switch self {
        case .unknownFlag(let f): "不认识的参数 \(f)"
        case .missingValue(let f): "\(f) 缺少参数值"
        case .invalidNumber(let f, let v): "\(f) 的参数「\(v)」不是正整数"
        case .conflicting(let a, let b): "\(a) 和 \(b) 不能一起用"
        case .unsupportedWidth(let width): "不支持的手机屏档位 \(width)，只允许540至1080、间隔10点"
        case .unsupportedSize(let width, let height): "未登记的布局 \(width)×\(height)，拒绝切换"
        }
    }
}
