import Foundation

/// Automatic display intent is separate from episode presence and remembered names.
public struct PhoneScreenAuthorization: Sendable {
    public static let evidenceLifetime: TimeInterval = 2
    /// A transport restart is not a disconnect, but cannot keep stale control alive forever.
    public static let transportRestartGrace: TimeInterval = 15
    private var confirmedAt: Date?
    private var transportPresentAt: Date?
    private var controllerGroup: Set<String>?
    private var serverProcessID: Int32?

    public init() {}

    public mutating func update(_ signals: UUSignals, now: Date) {
        if signals.serverProcessID != serverProcessID {
            clearEvidence()
            serverProcessID = signals.serverProcessID
        }
        guard let server = signals.serverProcessID, server > 0 else {
            clearEvidence()
            return
        }
        guard case .shown(let names) = signals.controllers else { return }
        guard ControllerKind.wantsPhoneScreen(names) else {
            clearEvidence()
            return
        }
        let nextGroup = Set(names)
        if let controllerGroup, controllerGroup != nextGroup { clearEvidence() }
        controllerGroup = nextGroup
        if signals.overlayVisible || signals.captureActive == true {
            confirmedAt = now
            transportPresentAt = now
            return
        }
        // Continuation requires uninterrupted fresh local control AND the same identities.
        // A stale AX table cannot start a display or resurrect expired authorization.
        guard allowsAutomaticScreen(at: now), let transportPresentAt else {
            confirmedAt = nil
            return
        }
        let gap = now.timeIntervalSince(transportPresentAt)
        guard gap >= 0, gap <= Self.transportRestartGrace else {
            confirmedAt = nil
            return
        }
        confirmedAt = now
    }

    public func allowsAutomaticScreen(at now: Date) -> Bool {
        guard let confirmedAt else { return false }
        let age = now.timeIntervalSince(confirmedAt)
        return age >= 0 && age <= Self.evidenceLifetime
    }

    private mutating func clearEvidence() {
        confirmedAt = nil
        transportPresentAt = nil
        controllerGroup = nil
    }
}
