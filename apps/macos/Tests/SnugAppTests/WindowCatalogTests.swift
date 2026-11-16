import Foundation
import Testing
import SnugCore
@testable import Snug

private let firstWindow = WindowInfo(key: WindowKey(pid: 101, id: 1), app: "Fixture", bundleID: "test.fixture", title: "First")
private let nextWindow = WindowInfo(key: WindowKey(pid: 101, id: 2), app: "Fixture", bundleID: "test.fixture", title: "Next")

/// No system enumeration, App startup, icons, windows or permission APIs.
private final class FakeListing: WindowCatalogReading, @unchecked Sendable {
    private struct State {
        var reads = 0
        var enrichmentEnabled = true
        var readEnrichment: [Bool] = []
        var starts = 0
        var stops = 0
        var permanentlyStopped = false
        var hold = false
        var pending: [CheckedContinuation<[WindowInfo], Never>] = []
    }
    private let lock = NSLock()
    private var state = State()
    var reads: Int { lock.withLock { state.reads } }
    var readEnrichment: [Bool] { lock.withLock { state.readEnrichment } }
    var starts: Int { lock.withLock { state.starts } }
    var permanentlyStopped: Bool { lock.withLock { state.permanentlyStopped } }
    var pending: Int { lock.withLock { state.pending.count } }
    var hold: Bool {
        get { lock.withLock { state.hold } }
        set { lock.withLock { state.hold = newValue } }
    }
    func list() async -> [WindowInfo] {
        await withCheckedContinuation { continuation in
            let held = lock.withLock {
                state.reads += 1
                state.readEnrichment.append(state.enrichmentEnabled)
                if state.hold { state.pending.append(continuation) }
                return state.hold
            }
            if !held { continuation.resume(returning: [firstWindow]) }
        }
    }
    func startListing() { lock.withLock { state.starts += 1; state.enrichmentEnabled = true } }
    func stopListing(permanently: Bool) {
        lock.withLock {
            state.stops += 1
            state.enrichmentEnabled = false
            state.permanentlyStopped = state.permanentlyStopped || permanently
        }
    }
    func finishFirst(_ result: [WindowInfo]) {
        let pending = lock.withLock { state.pending.isEmpty ? nil : state.pending.removeFirst() }
        pending?.resume(returning: result)
    }
    func finish(_ result: [WindowInfo]) {
        let pending = lock.withLock {
            let value = state.pending
            state.pending = []
            return value
        }
        for continuation in pending { continuation.resume(returning: result) }
    }
}

@Suite("Window catalog: actual app model with fake listing only")
@MainActor
struct WindowCatalogTests {
    private func eventually(_ predicate: () -> Bool) async -> Bool {
        for _ in 0..<250 {
            if predicate() { return true }
            try? await Task.sleep(for: .milliseconds(2))
        }
        return predicate()
    }
    private func settle() async {
        for _ in 0..<8 { await Task.yield() }
        try? await Task.sleep(for: .milliseconds(10))
    }
    private func presentation(streamed: DisplayID? = nil) -> AppState {
        let state = AppState()
        state.phone.screen = .ready(42)
        state.phone.listVisible = true
        state.streamedDisplay = streamed
        return state
    }

    @Test("Explicit open refreshes for unknown, matching or mismatched stream", arguments: [nil, 42, 1] as [DisplayID?])
    func explicitOpenAlwaysRefreshes(streamed: DisplayID?) async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        let state = presentation(streamed: streamed)
        catalog.updatePresentation(state)
        #expect(await eventually { service.reads == 2 })
        #expect(service.starts == 1)
    }

    @Test("Hide/reset preserves in-flight one-shot startup warmup")
    func hidingDoesNotCancelWarmup() async {
        let service = FakeListing()
        service.hold = true
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { service.pending == 1 })
        catalog.reset()
        service.finish([firstWindow])
        #expect(await eventually { catalog.loaded })
        #expect(catalog.windows == [firstWindow])
        #expect(service.reads == 1)
        #expect(service.starts == 0)
    }

    @Test("Hidden or busy presentation does not start periodic reads")
    func hiddenAndBusy() async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        let state = presentation()
        state.phone.listVisible = false
        catalog.updatePresentation(state)
        state.phone.listVisible = true
        state.phone.work = .placing(firstWindow)
        catalog.updatePresentation(state)
        await settle()
        #expect(service.reads == 1)
        #expect(service.starts == 0)
        state.phone.work = nil
        catalog.updatePresentation(state)
        #expect(await eventually { service.reads == 2 })
    }

    @Test("One-shot warmup does not enable hidden AX enrichment")
    func warmupIsReadOnly() async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        #expect(service.readEnrichment == [false])
    }

    @Test("Repeated panel updates do not duplicate an active refresh")
    func repeatedOpenCoalesces() async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        service.hold = true
        let state = presentation()
        for _ in 0..<5 { catalog.updatePresentation(state) }
        #expect(await eventually { service.pending == 1 })
        #expect(service.reads == 2)
        #expect(service.starts == 1)
    }

    @Test("Closing ignores late canceled reads; reopen keeps cache then refreshes")
    func lateReadCannotReplaceCache() async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        service.hold = true
        let state = presentation()
        catalog.updatePresentation(state)
        #expect(await eventually { service.pending == 1 })
        state.phone.listVisible = false
        catalog.updatePresentation(state)
        service.finish([nextWindow])
        await settle()
        #expect(catalog.loaded)
        #expect(catalog.windows == [firstWindow])
        state.phone.listVisible = true
        catalog.updatePresentation(state)
        #expect(catalog.windows == [firstWindow])
        #expect(await eventually { service.pending == 1 })
        service.finish([nextWindow])
        #expect(await eventually { catalog.windows == [nextWindow] })
    }

    @Test("Active refresh to busy to ready starts immediately and rejects the old read")
    func activeReadPausesAndResumes() async {
        let service = FakeListing()
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { catalog.loaded })
        service.hold = true
        let state = presentation()
        catalog.updatePresentation(state)
        #expect(await eventually { service.pending == 1 })
        state.phone.work = .placing(firstWindow)
        catalog.updatePresentation(state)
        state.phone.work = nil
        catalog.updatePresentation(state)
        #expect(await eventually { service.pending == 2 })
        #expect(service.reads == 3)
        service.finishFirst([nextWindow])
        await settle()
        #expect(catalog.windows == [firstWindow])
        service.finishFirst([nextWindow])
        #expect(await eventually { catalog.windows == [nextWindow] })
    }

    @Test("Shutdown cancels warmup and prevents subsequent reads")
    func shutdownIsPermanent() async {
        let service = FakeListing()
        service.hold = true
        let catalog = WindowCatalog(service: service)
        defer { catalog.shutdown(); service.finish([]) }
        #expect(await eventually { service.pending == 1 })
        catalog.shutdown()
        service.finish([firstWindow])
        await settle()
        catalog.updatePresentation(presentation())
        await settle()
        #expect(!catalog.loaded)
        #expect(catalog.windows.isEmpty)
        #expect(service.reads == 1)
        #expect(service.permanentlyStopped)
    }
}
