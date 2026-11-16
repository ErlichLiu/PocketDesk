// 手动建屏只代表这次用户意图，不持久化，也不能被后来的一次连接重新激活。
import Foundation

public struct ManualPhoneScreenAuthorization: Sendable {
    private var requestedAt: Date?
    private var lastLocalPresenceAt: Date?
    private var serverProcessID: Int32?
    private var controllerGroup: Set<String>?

    public init() {}

    public mutating func begin(at now: Date, serverProcessID: Int32? = nil, controllers: [String] = []) {
        requestedAt = now
        lastLocalPresenceAt = nil
        self.serverProcessID = serverProcessID
        controllerGroup = controllers.isEmpty ? nil : Set(controllers)
    }

    public mutating func cancel() {
        requestedAt = nil
        lastLocalPresenceAt = nil
        serverProcessID = nil
        controllerGroup = nil
    }

    public mutating func update(_ signals: UUSignals, now: Date) {
        // Check expiry before new presence; expired intent must never revive.
        guard isEnabled(at: now) else {
            cancel()
            return
        }
        if let serverProcessID, signals.serverProcessID != serverProcessID {
            cancel()
            return
        }
        if serverProcessID == nil { serverProcessID = signals.serverProcessID }
        if case .shown(let names) = signals.controllers, !names.isEmpty {
            let next = Set(names)
            if let controllerGroup, controllerGroup != next {
                cancel()
                return
            }
            // 首次读到名字是补齐身份，不是换人。后续才比较是否真正替换。
            controllerGroup = next
        }
        // Only an explicit, nonpersistent user request may use streaming continuity.
        // Raw/replayed capture alone can never start automatic authorization.
        if let server = signals.serverProcessID, server > 0, signals.isPresent {
            lastLocalPresenceAt = now
        }
    }

    public func isEnabled(at now: Date) -> Bool {
        guard let requestedAt else { return false }
        let age = now.timeIntervalSince(requestedAt)
        guard age >= 0, age <= 30 * 60 else { return false }
        if let lastLocalPresenceAt {
            let gap = now.timeIntervalSince(lastLocalPresenceAt)
            return gap >= 0 && gap <= SessionEpisode.reconnectWindow
        }
        return age <= 30
    }
}
