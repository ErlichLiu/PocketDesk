// 搬窗口、调字号：辅助功能接口 + 按 pid 投递的按键。
// 辅助功能的调用会阻塞：交互与还原保持串行；只读目录另走低优先级队列，不让列表挡住操作。
// 搬窗口时按「进程号 + 系统窗口编号」现找，不留辅助功能对象（App 重建窗口后旧对象会失效）；
// 只有列表留着别的空间里找出来的对象，因为找一遍太贵（见 scheduleEnrichment）。
// 不管窗口在哪块屏、哪个桌面空间、是否全屏或最小化都找得到（见 AX.allWindows）；
// 窗口在不在问窗口服务器（exists），不经过 App：App 卡住时辅助功能读不到，不能当成窗口关了
import AppKit
import ApplicationServices
import SnugCore

public final class WindowService: WindowControl {
    /// 每次重试之间等的时间、最多试几次：跨屏改大小时 App 会按它以为的那块屏夹紧尺寸，刚加的屏 App 也可能还没认到
    static let retryDelay: TimeInterval = 0.25
    static let maxTries = 10
    /// kVK_ANSI_Equal、kVK_ANSI_Minus（不为两个常量引入 Carbon：它的 WindowKey 会和我们的重名）
    static let equalKey: CGKeyCode = 0x18
    static let minusKey: CGKeyCode = 0x1B
    /// 别的桌面空间里的窗口隔这么久才整个重新试一遍
    static let remoteRescanInterval: TimeInterval = 10

    private let queue = DispatchQueue(label: "cool.proma.pocketdesk.windows", qos: .userInitiated)
    /// CG 首屏不能排在逐 App AX / remote token 扫描后面。
    private let serverQueue = DispatchQueue(label: "cool.proma.pocketdesk.window-server", qos: .utility)
    private let catalogQueue = DispatchQueue(label: "cool.proma.pocketdesk.window-catalog", qos: .utility)
    private let directory = Locked(WindowDirectory())
    private let interactiveWork = Locked(0)
    private let remoteAttempts = Locked(RemoteScanThrottle())
    /// 每个 App 用远程令牌试出来的窗口对象和试的时刻（单调时钟）。只在 catalogQueue 上使用
    private let remote = Locked<[pid_t: (windows: [AXUIElement], scannedAt: TimeInterval)]>([:])

    public init() {}

    // MARK: WindowControl

    // 窗口服务器知道每个窗口（别的桌面空间、最小化、藏起来的也在），不用等 App 响应，也不要权限

    public func exists(_ window: WindowKey) async -> Bool { Self.serverInfo(window) != nil }

    public func instanceIdentity(of window: WindowKey) async -> String? {
        guard Self.serverInfo(window) != nil else { return nil }
        let token = await MainActor.run { () -> String? in
            guard let app = NSRunningApplication(processIdentifier: window.pid), !app.isTerminated,
                  let launched = app.launchDate, let bundle = app.bundleIdentifier else { return nil }
            return "\(window.pid):\(bundle):\(launched.timeIntervalSince1970.bitPattern)"
        }
        return Self.serverInfo(window) != nil ? token : nil
    }

    public func isShown(_ window: WindowKey) async -> Bool {
        (Self.serverInfo(window)?[kCGWindowIsOnscreen as String] as? Bool) == true
    }

    public func look(_ window: WindowKey) async -> WindowLook? {
        await run {
            guard let element = AX.element(for: window), let frame = AX.frame(element) else { return nil }
            return WindowLook(frame: frame, fullScreen: AX.flag(element, AX.fullScreen), minimized: AX.flag(element, kAXMinimizedAttribute))
        }
    }

    public func move(_ window: WindowKey, to frame: CGRect) async -> CGRect? {
        await run {
            guard let element = AX.element(for: window) else { return nil }
            return Self.move(element, to: frame)
        }
    }

    public func setFullScreen(_ window: WindowKey, _ on: Bool) async {
        await run {
            guard let element = AX.element(for: window) else { return }
            AX.set(element, AX.fullScreen, on as CFBoolean)
            Self.wait(seconds: 5) { AX.flag(element, AX.fullScreen) == on }
            // 等全屏动画走完，不然紧接着的挪动会被动画盖掉
            Thread.sleep(forTimeInterval: 1.2)
        }
    }

    public func setMinimized(_ window: WindowKey, _ on: Bool) async {
        await run {
            guard let element = AX.element(for: window) else { return }
            Self.setMinimized(element, on)
        }
    }

    public func focus(_ window: WindowKey) async {
        await run {
            guard let element = AX.element(for: window) else { return }
            if AX.flag(element, kAXMinimizedAttribute) { Self.setMinimized(element, false) }
            AX.raise(element)
        }
        await MainActor.run {
            if NSRunningApplication(processIdentifier: window.pid)?.activate() == false {
                Log.warn("激活 pid \(window.pid) 没成功，键盘输入可能进不了这个窗口")
            }
        }
    }

    public func zoom(_ window: WindowKey, steps: Int) async -> Bool {
        await run {
            guard steps != 0 else { return true }
            guard let element = AX.element(for: window) else { return false }
            // 按键投给 App，App 交给它的当前窗口：先把这个窗口设成当前窗口
            AX.raise(element)
            let key = steps > 0 ? Self.equalKey : Self.minusKey
            for _ in 0..<abs(steps) {
                for down in [true, false] {
                    guard let event = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: down) else { continue }
                    event.flags = .maskCommand
                    event.postToPid(window.pid)
                }
                Thread.sleep(forTimeInterval: 0.08)
            }
            return true
        }
    }

    public func snapshot() async -> [WindowKey: CGRect] {
        let apps = await Self.regularApps()
        return await run {
            // 拓扑变化前只需窗口几何：不为建屏逐 App 等 AX。
            // CG 失败才回到原 AX 路径，不能悄悄丢掉窗口保护快照。
            if let frames = Self.serverFrames(apps: apps) { return frames }
            Log.warn("窗口服务器快照不可读，回退辅助功能快照")
            var frames: [WindowKey: CGRect] = [:]
            for app in apps {
                for element in AX.respondingWindows(pid: app.pid) ?? [] where AX.isStandard(element) && !AX.flag(element, kAXMinimizedAttribute) {
                    guard let id = AX.windowID(element), let frame = AX.frame(element) else { continue }
                    frames[WindowKey(pid: app.pid, id: id)] = frame
                }
            }
            return frames
        }
    }

    public func restore(_ snapshot: [WindowKey: CGRect], except: WindowKey?) async {
        await run {
            for (window, saved) in snapshot where window != except {
                // 系统没有挪动的窗口不必重新向 App 取 AX；不可读时仍走原还原路径。
                if let current = Self.serverFrame(window), current.matches(saved) { continue }
                guard let element = AX.element(for: window), AX.isStandard(element), !AX.flag(element, kAXMinimizedAttribute),
                      let now = AX.frame(element), !now.matches(saved) else { continue }
                let got = Self.move(element, to: saved)
                Log.info("系统挪了 pid \(window.pid) 的窗口 \(now.short)，已放回 \(saved.short)"
                         + (got.map { $0.matches(saved) } == true ? "" : "（没对上，现在 \(got?.short ?? "?")）"))
            }
        }
    }

    // MARK: 窗口列表（手机屏上的卡片）

    /// 首屏只读 CGWindowList 与缓存：标题与跨空间窗口在独立队列补齐，绝不等逐 App AX。
    public func list() async -> [WindowInfo] {
        let apps = await Self.regularApps()
        let snapshot = await withCheckedContinuation { continuation in
            serverQueue.async {
                continuation.resume(returning: Self.serverSnapshot(apps: apps))
            }
        }
        let windows = directory.withLock { $0.snapshot(server: snapshot.windows, liveKeys: snapshot.liveKeys) }
        scheduleEnrichment(apps: apps)
        return WindowList.ordered(windows, frontToBack: snapshot.frontToBack)
    }

    public func startListing() { directory.withLock { $0.start() } }

    /// 不等待正在进行的 AX IPC；取消标记让扫描在下一次 IPC / token 前退出。
    /// 退出时永久停用，避免面板的迟到回调又启动目录，但不影响交互队列上的还原。
    public func stopListing(permanently: Bool = false) {
        directory.withLock { $0.stop(permanently: permanently) }
    }

    // MARK: 内部

    private func canEnrich(_ token: UInt64) -> Bool {
        directory.withLock { $0.isCurrent(token) } && interactiveWork.withLock { $0 == 0 }
    }

    private func scheduleEnrichment(apps: [App]) {
        guard interactiveWork.withLock({ $0 == 0 }),
              let token = directory.withLock({ $0.beginRefresh() }) else { return }
        catalogQueue.async { [self] in
            defer { directory.withLock { $0.finishRefresh(token) } }
            let running = Set(apps.map(\.pid))
            remote.withLock { $0 = $0.filter { running.contains($0.key) } }
            remoteAttempts.withLock { $0.keep(pids: running) }
            var responding: [App] = []
            // 先读各 App 的正常窗口，所有 App 都处理过后才探测远程令牌。
            for app in apps {
                guard canEnrich(token) else { return }
                guard let visible = AX.respondingWindows(pid: app.pid, readTimeout: AX.catalogTimeout) else { continue }
                responding.append(app)
                for element in visible {
                    guard canEnrich(token) else { return }
                    enrich(element, app: app, token: token)
                }
                for element in remote.withLock({ $0[app.pid]?.windows ?? [] }) {
                    guard canEnrich(token) else { return }
                    enrich(element, app: app, token: token)
                }
            }
            for app in responding {
                guard canEnrich(token) else { return }
                let now = ProcessInfo.processInfo.systemUptime
                guard remoteAttempts.withLock({ $0.begin(pid: app.pid, now: now, interval: Self.remoteRescanInterval) }) else { continue }
                let scan = AX.remoteWindows(pid: app.pid, readTimeout: AX.catalogTimeout) { self.canEnrich(token) }
                guard canEnrich(token) else { return }
                if scan.complete { remote.withLock { $0[app.pid] = (scan.windows, now) } }
                for element in scan.windows {
                    guard canEnrich(token) else { return }
                    enrich(element, app: app, token: token)
                }
            }
        }
    }

    private func enrich(_ element: AXUIElement, app: App, token: UInt64) {
        // 只改目录创建/获取的对象，搬窗、恢复、UU 感知仍沿用原超时。
        AXUIElementSetMessagingTimeout(element, AX.catalogTimeout)
        guard let id = AX.windowID(element), canEnrich(token) else { return }
        let subrole = AX.string(element, kAXSubroleAttribute)
        guard canEnrich(token) else { return }
        let standard = subrole.map { $0 == kAXStandardWindowSubrole }
        let title = standard == true ? AX.string(element, kAXTitleAttribute) : nil
        let info = WindowInfo(key: WindowKey(pid: app.pid, id: id), app: app.name, bundleID: app.bundleID,
                              title: WindowInfo.visibleTitle(title ?? ""))
        directory.withLock { $0.record(info, standard: standard, token: token, titleRead: title != nil) }
    }

    private struct ServerSnapshot: Sendable {
        let windows: [WindowInfo]
        /// nil 表示 CG 读取失败，而不是没有窗口。
        let liveKeys: Set<WindowKey>?
        let frontToBack: [UInt32]
    }

    private static func serverSnapshot(apps: [App]) -> ServerSnapshot {
        guard let list = CGWindowListCopyWindowInfo([.optionAll, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else {
            return ServerSnapshot(windows: [], liveKeys: nil, frontToBack: [])
        }
        let owners = Dictionary(uniqueKeysWithValues: apps.map { ($0.pid, $0) })
        var windows: [WindowInfo] = []
        var live = Set<WindowKey>()
        var front: [UInt32] = []
        for entry in list {
            guard let pid = (entry[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value,
                  let app = owners[pid], let id = (entry[kCGWindowNumber as String] as? NSNumber)?.uint32Value else { continue }
            let key = WindowKey(pid: pid, id: id)
            live.insert(key)
            // 普通 App 的正常窗口先展示；成功读到 AX 子角色后再过滤面板。标题可能因隐私权限为空，不能丢窗口。
            guard (entry[kCGWindowLayer as String] as? NSNumber)?.intValue == 0,
                  let bounds = entry[kCGWindowBounds as String] as? [String: Any],
                  let frame = WindowServerGeometry.frame(bounds), frame.width > 1, frame.height > 1 else { continue }
            if (entry[kCGWindowIsOnscreen as String] as? Bool) == true { front.append(id) }
            windows.append(WindowInfo(key: key, app: app.name, bundleID: app.bundleID,
                                      title: WindowInfo.visibleTitle(entry[kCGWindowName as String] as? String ?? "")))
        }
        return ServerSnapshot(windows: windows, liveKeys: live, frontToBack: front)
    }

    /// 连隐藏窗口也保留几何；真正需要还原时才用 AX 确认标准/非最小化，避免批量 IPC。
    private static func serverFrames(apps: [App]) -> [WindowKey: CGRect]? {
        guard let list = CGWindowListCopyWindowInfo([.optionAll, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return nil }
        let owners = Set(apps.map(\.pid))
        var frames: [WindowKey: CGRect] = [:]
        for entry in list {
            guard let pid = (entry[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value, owners.contains(pid),
                  let id = (entry[kCGWindowNumber as String] as? NSNumber)?.uint32Value,
                  (entry[kCGWindowLayer as String] as? NSNumber)?.intValue == 0,
                  let bounds = entry[kCGWindowBounds as String] as? [String: Any],
                  let frame = WindowServerGeometry.frame(bounds) else { continue }
            frames[WindowKey(pid: pid, id: id)] = frame
        }
        return frames
    }

    private static func serverFrame(_ window: WindowKey) -> CGRect? {
        guard let bounds = serverInfo(window)?[kCGWindowBounds as String] as? [String: Any] else { return nil }
        return WindowServerGeometry.frame(bounds)
    }

    /// 窗口服务器里这个窗口的信息；没有、或编号已经归了别的进程返回 nil
    private static func serverInfo(_ window: WindowKey) -> [String: Any]? {
        guard let info = (CGWindowListCopyWindowInfo([.optionIncludingWindow], window.id) as? [[String: Any]])?.first,
              (info[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value == window.pid else { return nil }
        return info
    }

    /// 等到条件成立，最多等 seconds 秒。App 卡住时每次读都要等满超时，所以按截止时间算，不按次数
    private static func wait(seconds: TimeInterval, until done: () -> Bool) {
        let deadline = ProcessInfo.processInfo.systemUptime + seconds
        while !done(), ProcessInfo.processInfo.systemUptime < deadline { Thread.sleep(forTimeInterval: 0.25) }
    }

    private func run<T: Sendable>(_ work: @escaping @Sendable () -> T) async -> T {
        interactiveWork.withLock { $0 += 1 }
        return await withCheckedContinuation { continuation in
            queue.async { [interactiveWork] in
                let result = work()
                interactiveWork.withLock { $0 -= 1 }
                continuation.resume(returning: result)
            }
        }
    }

    /// 设好后等它真的变过去，再等动画走完
    private static func setMinimized(_ element: AXUIElement, _ on: Bool) {
        AX.set(element, kAXMinimizedAttribute, on as CFBoolean)
        wait(seconds: 3) { AX.flag(element, kAXMinimizedAttribute) == on }
        Thread.sleep(forTimeInterval: 0.5)
    }

    /// 先改大小、再挪、再改大小，然后核对，对不上就重来。
    /// 位置对了、连着两次尺寸都一样，就是 App 限定了最小尺寸（系统设置、Safari……），再试也一样，不空等
    private static func move(_ element: AXUIElement, to target: CGRect) -> CGRect? {
        var got: CGRect?
        for attempt in 0..<maxTries {
            let previous = got
            AX.setSize(element, target.size)
            AX.setPosition(element, target.origin)
            AX.setSize(element, target.size)
            // 首轮同步响应就省掉固定 250ms；没对上仍保留原来的十轮延迟核对，避免每轮额外增加 AX IPC。
            if attempt == 0 {
                got = AX.frame(element)
                if got?.matches(target) == true { break }
            }
            Thread.sleep(forTimeInterval: retryDelay)
            got = AX.frame(element)
            guard let now = got, !now.matches(target) else { break }
            let placed = abs(now.minX - target.minX) < 2 && abs(now.minY - target.minY) < 2
            if let previous, now.matches(previous), placed { break }
        }
        return got
    }

    private struct App: Sendable {
        let pid: pid_t
        let name: String
        let bundleID: String?
    }

    /// 普通 App（有 Dock 图标的），不含 Snug 自己
    private static func regularApps() async -> [App] {
        await MainActor.run {
            NSWorkspace.shared.runningApplications
                .filter { $0.activationPolicy == .regular && $0 != NSRunningApplication.current }
                .map { App(pid: $0.processIdentifier, name: $0.localizedName ?? "未命名", bundleID: $0.bundleIdentifier) }
        }
    }

}
