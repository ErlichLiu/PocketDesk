// 状态文案：一个标题 + 一个短说明。把 AppState 翻成菜单栏和设置窗口共用的状态，也给手机屏上的提示用。只用短词，书面、克制
import SnugCore

struct StatusText: Equatable {
    var headline: String
    var detail: String?

    init(headline: String, detail: String?) {
        self.headline = headline
        self.detail = detail
    }

    @MainActor
    init(_ s: AppState) {
        headline = Self.headline(s)
        detail = Self.detail(s)
    }

    @MainActor
    private static func headline(_ s: AppState) -> String {
        if s.quitting { return "正在退出" }
        if !s.accessibilityTrusted { return "需要\(PermissionName.accessibility)权限" }
        switch s.connection {
        case .idle: return "等待连接"
        case .reconnecting: return "连接中断"
        case .connected: return s.controllers.isEmpty ? "远程中" : "\(s.controllers.joined(separator: "、")) · 远程中"
        }
    }

    @MainActor
    private static func detail(_ s: AppState) -> String? {
        if s.quitting { return "正在还原窗口" }
        if !s.accessibilityTrusted { return "用于移动窗口与转换手势" }
        if !s.config.phoneScreen { return "手机屏已关闭" }
        if s.manualPhoneScreen { return "本次手动保持 · 最长30分钟，不保存" }
        if s.connection == .idle { return "自动模式等待本机被控与手机证据；识别不到可手动启用" }
        if s.gesturesFailed { return "手势转换未启用" }
        switch s.phone.screen {
        case .preparing: return "正在准备手机屏"
        case .failed: return "手机屏创建失败"
        case .ready:
            guard s.phoneInUse else { return "自动模式 · 手机屏已就绪" }
            return s.phone.placed.map { "手机屏 · \($0.window.app)" } ?? "手机屏使用中"
        case .off:
            if s.automaticPhoneScreen { return "已暂停本次手机屏；可手动重新启用" }
            return "未确认手机正在控制本机；请打开 UU 被控页，或手动启用本次"
        }
    }
}

extension LoginItemIssue {
    /// 设置页底部的提示：写明去哪里处理
    var text: String {
        switch self {
        case .needsApproval: "开机启动待允许 · 系统设置 › 通用 › 登录项"
        case .notInApplications: "开机启动未生效 · 需位于「应用程序」"
        case .failed: "开机启动未生效 · 请稍后重试"
        }
    }
}

extension PhoneDesk.Notice {
    /// 手机屏列表顶部的提示：出了什么事，和接下来怎么办。不出现技术词
    var text: StatusText {
        switch self {
        case .windowGone(let app): StatusText(headline: "\(app) 窗口已关闭", detail: "请另选一个窗口")
        case .windowUnresponsive(let app): StatusText(headline: "\(app) 无响应", detail: "可能正忙，请稍后再试")
        case .recordUnwritable: StatusText(headline: "无法保存原位置", detail: "为确保能放回原处，窗口未移动")
        case .screenNotReady: StatusText(headline: "手机屏未就绪", detail: "屏幕正在调整，请稍后再试")
        case .layoutUnavailable: StatusText(headline: "布局切换未完成", detail: "可再次选择布局重试，窗口原位置仍保留")
        }
    }
}
