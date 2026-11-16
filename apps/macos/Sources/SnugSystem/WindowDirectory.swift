// 目录缓存与取消的纯逻辑；由 WindowService 的锁保护，不持有任何 AX 对象。
// 只有窗口服务器明确报告缺失才移除，AX 不可读不意味着窗口已经关闭。
import CoreGraphics
import Foundation
import SnugCore

/// CG 与 AX 都使用左上原点的逻辑点；拒绝损坏数据，不能凭空构造可用于还原的矩形。
enum WindowServerGeometry {
    static func frame(_ bounds: [String: Any]) -> CGRect? {
        guard let x = bounds["X"] as? NSNumber, let y = bounds["Y"] as? NSNumber,
              let width = bounds["Width"] as? NSNumber, let height = bounds["Height"] as? NSNumber else { return nil }
        let values = [x.doubleValue, y.doubleValue, width.doubleValue, height.doubleValue]
        guard values.allSatisfy({ $0.isFinite }), width.doubleValue > 0, height.doubleValue > 0 else { return nil }
        return CGRect(x: x.doubleValue, y: y.doubleValue, width: width.doubleValue, height: height.doubleValue)
    }
}

struct WindowDirectory {
    private var windows: [WindowKey: WindowInfo] = [:]
    private var nonstandard: Set<WindowKey> = []
    private var lastServerTitles: [WindowKey: String] = [:]
    private var enabled = true
    private var permanentlyStopped = false
    private var generation: UInt64 = 0
    private var refreshing: UInt64?

    mutating func start() {
        guard !permanentlyStopped else { return }
        enabled = true
    }

    mutating func stop(permanently: Bool = false) {
        enabled = false
        permanentlyStopped = permanentlyStopped || permanently
        generation &+= 1
        refreshing = nil
    }

    mutating func beginRefresh() -> UInt64? {
        guard enabled, refreshing == nil else { return nil }
        refreshing = generation
        return generation
    }

    func isCurrent(_ token: UInt64) -> Bool { enabled && token == generation }

    mutating func finishRefresh(_ token: UInt64) {
        if refreshing == token { refreshing = nil }
    }

    /// nil 子角色表示不可读；不能借此把先前发现的窗口或标题抹掉。
    mutating func record(_ window: WindowInfo, standard: Bool?, token: UInt64, titleRead: Bool = true) {
        guard isCurrent(token), let standard else { return }
        if standard {
            nonstandard.remove(window.key)
            // 成功的空标题与读取失败不同，前者应覆盖旧标题。
            if titleRead || windows[window.key] == nil { windows[window.key] = window }
        } else {
            nonstandard.insert(window.key)
            windows.removeValue(forKey: window.key)
        }
    }

    /// liveKeys 为 nil 表示 CG 本轮失败，不能误当作「所有窗口都已关闭」。
    mutating func snapshot(server: [WindowInfo], liveKeys: Set<WindowKey>?) -> [WindowInfo] {
        if let liveKeys {
            windows = windows.filter { liveKeys.contains($0.key) }
            nonstandard.formIntersection(liveKeys)
            lastServerTitles = lastServerTitles.filter { liveKeys.contains($0.key) }
        }
        for window in server where !nonstandard.contains(window.key) {
            let changed = lastServerTitles[window.key] != window.title
            lastServerTitles[window.key] = window.title
            // 只接受CG的新有效标题；同一份旧CG快照不能覆盖刚刚读到的AX空标题。
            if windows[window.key] == nil || (changed && !window.title.isEmpty) { windows[window.key] = window }
        }
        let order = server.map(\.key)
        let shown = Set(order)
        let remainder = windows.keys.filter { !shown.contains($0) }.sorted {
            ($0.pid, $0.id) < ($1.pid, $1.id)
        }
        return (order + remainder).compactMap { windows[$0] }
    }
}
