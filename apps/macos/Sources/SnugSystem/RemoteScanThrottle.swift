// 失败保留成功缓存，但也必须按最近尝试时间节流；不能每1.5秒再全量扫一次。
import Foundation

struct RemoteScanThrottle {
    private var attemptedAt: [Int32: TimeInterval] = [:]

    mutating func begin(pid: Int32, now: TimeInterval, interval: TimeInterval) -> Bool {
        if let last = attemptedAt[pid], now - last < interval { return false }
        attemptedAt[pid] = now
        return true
    }

    mutating func keep(pids: Set<Int32>) {
        attemptedAt = attemptedAt.filter { pids.contains($0.key) }
    }
}
