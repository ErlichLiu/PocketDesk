// 后台读 UU 的控制端表格：串行，同一时刻只有一次读取在跑（不堆积），读得太慢的结果丢弃。
// 结果放在加锁的盒子里，主线程每拍直接取，不经主队列转发：结果要是投回主线程、按到达时间判新鲜，
// 主线程一卡，后台早就读好的结果也会被当成过期，没有覆盖窗口的会话就会被误判断开
import Foundation
import os
import SnugCore
import SnugSystem

@MainActor
final class ControllerReader {
    /// 一次读取超过这么久就不可信（UU 卡住时 AX 每次调用最多等 1 秒，一棵树要调很多次）
    nonisolated static let maxReadDuration: TimeInterval = 2

    private struct Box: Sendable {
        var inFlightSince: TimeInterval?
        var latest = ControllerTable.unreadable
        var latestAt: TimeInterval?
        var processID: pid_t?
        var serverProcessID: pid_t?
        var generation = 0
    }

    private let queue = DispatchQueue(label: "cool.proma.pocketdesk.ax-reader", qos: .utility)
    private let box = OSAllocatedUnfairLock(initialState: Box())

    /// 每拍调用：返回此刻可信的控制端表格，没有读取在跑就发起下一次
    func poll(serverPID: pid_t?) -> ControllerTable {
        guard AX.isTrusted, let pid = UURemoteApp.pid(), let serverPID else {
            reset()
            return .unreadable
        }
        let now = Self.uptime()
        let (current, start, generation) = box.withLock { b -> (ControllerTable, Bool, Int) in
            if b.processID != pid || b.serverProcessID != serverPID {
                b.generation += 1
                b.processID = pid
                b.serverProcessID = serverPID
                b.inFlightSince = nil
                b.latest = .unreadable
                b.latestAt = nil
            }
            let fresh = b.latestAt.map { now - $0 <= Self.maxReadDuration } ?? false
            if let since = b.inFlightSince {
                // 读取卡住超过上限：手上的结果已经说明不了现在
                return (now - since > Self.maxReadDuration || !fresh ? .unreadable : b.latest, false, b.generation)
            }
            b.inFlightSince = now
            return (fresh ? b.latest : .unreadable, true, b.generation)
        }
        if start { read(pid: pid, generation: generation) }
        return current
    }

    private func read(pid: pid_t, generation: Int) {
        let box = box
        queue.async {
            let started = Self.uptime()
            let result = autoreleasepool { UUAccessibility.localControllers(pid: pid) }
            let took = Self.uptime() - started
            let tooSlow = took > Self.maxReadDuration
            if tooSlow {
                Log.warn(String(format: "读 UU 控制端表格用了 %.1fs，结果丢弃", took))
            }
            let table = tooSlow ? .unreadable : result
            box.withLock { b in
                guard b.generation == generation else { return }
                b.inFlightSince = nil
                b.latest = table
                // 记录读取起点，不把一份旧 AX 快照在完成时重新当成刚观测到。
                b.latestAt = started
            }
        }
    }

    /// 没授权或 UU 没在运行：清空，在途的读取结果作废
    private func reset() {
        box.withLock { b in
            b.generation += 1
            b.inFlightSince = nil
            b.latest = .unreadable
            b.latestAt = nil
            b.processID = nil
            b.serverProcessID = nil
        }
    }

    private nonisolated static func uptime() -> TimeInterval { ProcessInfo.processInfo.systemUptime }
}
