// 手机屏上列表里的窗口：列表露出来时每 1.5 秒刷新一次，收起就停；App 图标按 bundle ID 缓存。
// 每次露出来，第一次刷新按「最近用过的在前」重排，之后只增删、不换位置（见 WindowList.keepingOrder）。
// 正在放窗口时暂停；启动时预热一次，面板收起保留缓存，下次展示先用快照，退出永久停止。
import AppKit
import Observation
import SnugCore
import SnugSystem

@MainActor
@Observable
final class WindowCatalog {
    static let refreshInterval: TimeInterval = 1.5

    private(set) var windows: [WindowInfo]
    /// 第一次刷新完成前不显示「没有窗口」
    private(set) var loaded: Bool

    @ObservationIgnored private let service: WindowService?
    @ObservationIgnored private var refreshing: Task<Void, Never>?
    @ObservationIgnored private var warming: Task<Void, Never>?
    @ObservationIgnored private var stoppedForQuit = false
    @ObservationIgnored private var paused = false
    @ObservationIgnored private var icons: [String: NSImage] = [:]

    init(service: WindowService) {
        self.service = service
        windows = []
        loaded = false
        // 只预热一轮，不在手机屏不存在时持续轮询；首屏不必等建屏完成才开始读取。
        warming = Task { [weak self] in
            let began = ProcessInfo.processInfo.systemUptime
            let list = await service.list()
            guard let self, !Task.isCancelled, !stoppedForQuit else { return }
            if !loaded {
                windows = list
                loaded = true
                Log.info(String(format: "窗口目录预热：%d个窗口，%.0fms（CG首屏，标题后台补齐）", list.count,
                                (ProcessInfo.processInfo.systemUptime - began) * 1000))
            }
            warming = nil
        }
    }

    /// 渲染样例用：固定的窗口，不刷新
    init(sample: [WindowInfo], loaded: Bool = true) {
        service = nil
        windows = sample
        self.loaded = loaded
    }

    func startRefreshing() {
        guard !stoppedForQuit, refreshing == nil, let service else { return }
        if !paused { service.startListing() }
        refreshing = Task { [weak self] in
            var reorder = true
            while !Task.isCancelled {
                if self?.paused == true {
                    try? await Task.sleep(for: .seconds(Self.refreshInterval))
                    continue
                }
                let began = ProcessInfo.processInfo.systemUptime
                let list = await service.list()
                guard let self, !Task.isCancelled else { return }
                if reorder {
                    Log.info(String(format: "窗口列表首轮：%d个窗口，%.0fms（不等待AX全量扫描）", list.count,
                                    (ProcessInfo.processInfo.systemUptime - began) * 1000))
                }
                let next = reorder ? list : WindowList.keepingOrder(of: windows, updatedWith: list)
                if next != windows { windows = next }
                loaded = true
                reorder = false
                try? await Task.sleep(for: .seconds(Self.refreshInterval))
            }
        }
    }

    func stopRefreshing() {
        warming?.cancel()
        warming = nil
        refreshing?.cancel()
        refreshing = nil
        service?.stopListing()
    }

    /// 暂停、继续刷新：取消后台探测，保留已显示的顺序和缓存。
    func setPaused(_ on: Bool) {
        guard paused != on else { return }
        paused = on
        if on { service?.stopListing() }
        else if refreshing != nil, !stoppedForQuit { service?.startListing() }
    }

    /// 一次远程结束只停止轮询；下次立即展示缓存，由 CG 快照淘汰明确关闭的窗口。
    func reset() { stopRefreshing() }

    func shutdown() {
        stoppedForQuit = true
        stopRefreshing()
        service?.stopListing(permanently: true)
    }

    func icon(for window: WindowInfo) -> NSImage {
        let key = window.bundleID ?? "pid:\(window.key.pid)"
        if let cached = icons[key] { return cached }
        let icon = window.bundleID.flatMap { NSWorkspace.shared.urlForApplication(withBundleIdentifier: $0) }
            .map { NSWorkspace.shared.icon(forFile: $0.path) }
            ?? NSRunningApplication(processIdentifier: window.key.pid)?.icon
            ?? NSWorkspace.shared.icon(for: .application)
        icons[key] = icon
        return icon
    }
}
