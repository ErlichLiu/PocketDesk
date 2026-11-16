// PocketDesk 首版只接受显式本地构建。保留 UI 状态接口，但不创建网络、替换应用或重启任务。
import AppKit
import SnugCore

struct UpdateState: Equatable {
    enum Check: Equatable {
        case unknown
        case upToDate
        /// 查到的比当前新的版本
        case available(UpdateFeed)
    }

    enum Phase: Equatable {
        case idle
        case checking
        /// 已取到的字节占要取的比例，0...1
        case downloading(Double)
        case installing
    }

    var check = Check.unknown
    var phase = Phase.idle
    /// 最近一次检查或更新没成的原因
    var problem: UpdateProblem?

    var available: UpdateFeed? {
        if case .available(let feed) = check { feed } else { nil }
    }

    /// 有新版本：能自动更新的，或者只能手动下载的（update.json 的格式比这一版新）
    var hasNewVersion: Bool { available != nil || problem == .tooOld }
}

/// 给用户看的失败原因：短词，不带技术细节（细节在日志里）
enum UpdateProblem: Error, Equatable {
    /// 网络不通，或者更新源上取不到、读不懂 update.json
    case disabled
    case unreachable(UpdateSource)
    case corrupt
    /// update.json 的格式比这一版新，这一版拼不出新版本
    case tooOld
    /// 应用包所在的位置换不了：写不进去（不在「应用程序」里、在只读的磁盘映像里），或者那块磁盘不支持原子对调
    case cannotReplace
    case signature
    case failed

    var text: String {
        switch self {
        case .disabled: "PocketDesk 首版已停用在线更新，请使用本地构建"
        case .unreachable(let source): "无法从\(source.title)获取更新"
        case .corrupt: "下载内容校验失败"
        case .tooOld: "新版本需手动下载"
        case .cannotReplace: "无法替换当前位置的 PocketDesk"
        case .signature: "新版本签名不符"
        case .failed: "更新失败"
        }
    }

    /// 自动更新走不通，只能请用户去下载
    var needsDownload: Bool {
        switch self {
        case .disabled, .tooOld, .cannotReplace, .signature, .unreachable, .corrupt, .failed: false
        }
    }
}

@MainActor
final class Updater {
    nonisolated static let current = (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String)
        .flatMap(Version.init)

    private let state: AppState
    var onNewVersion: ((_ userInitiated: Bool) -> Void)?
    var onInstalled: (() -> Void)?

    init(state: AppState) { self.state = state }

    func check(userInitiated: Bool, waitForNetwork: Bool = false) { reject() }
    func install() { reject() }

    private func reject() {
        state.update = UpdateState()
        state.update.problem = .disabled
        Log.info("PocketDesk 在线更新已停用；未发送请求或替换应用")
    }
}
