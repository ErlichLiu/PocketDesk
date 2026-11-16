import CoreGraphics
import Foundation
import SnugCore
import Testing
@testable import SnugSystem

@Suite("窗口目录：快照、不可读与取消")
struct WindowDirectoryTests {
    private func window(_ id: UInt32, title: String = "CG 标题") -> WindowInfo {
        WindowInfo(key: WindowKey(pid: 42, id: id), app: "Editor", bundleID: "test.editor", title: title)
    }

    @Test("首屏只需要窗口服务器快照，不等 AX 补全")
    func serverFirst() {
        var directory = WindowDirectory()
        let first = window(1)
        #expect(directory.snapshot(server: [first], liveKeys: [first.key]) == [first])
        #expect(directory.beginRefresh() != nil)
    }

    @Test("AX 不能读取不代表窗口已关闭，已补全标题继续可用")
    func unreadableKeepsWindow() throws {
        var directory = WindowDirectory()
        let fallback = window(1, title: "")
        _ = directory.snapshot(server: [fallback], liveKeys: [fallback.key])
        let nextToken = directory.beginRefresh()
        let token = try #require(nextToken)
        directory.record(window(1, title: "AX 标题"), standard: true, token: token)
        directory.record(fallback, standard: nil, token: token)
        #expect(directory.snapshot(server: [fallback], liveKeys: [fallback.key]).first?.title == "AX 标题")
    }

    @Test("新的有效CG标题不能被旧缓存永久遮住")
    func freshServerTitle() {
        var directory = WindowDirectory()
        let first = window(1, title: "A")
        _ = directory.snapshot(server: [first], liveKeys: [first.key])
        let next = window(1, title: "B")
        #expect(directory.snapshot(server: [next], liveKeys: [first.key]).first?.title == "B")
    }

    @Test("AX成功返回空标题可以清掉旧标题，未变化的旧CG标题不能复活它")
    func successfulEmptyTitle() throws {
        var directory = WindowDirectory()
        let server = window(1, title: "旧标题")
        _ = directory.snapshot(server: [server], liveKeys: [server.key])
        let nextToken = directory.beginRefresh()
        let token = try #require(nextToken)
        directory.record(window(1, title: ""), standard: true, token: token)
        #expect(directory.snapshot(server: [server], liveKeys: [server.key]).first?.title == "")
    }

    @Test("AX标题读取失败不当作成功空标题")
    func failedTitleReadKeepsCache() throws {
        var directory = WindowDirectory()
        let first = window(1, title: "原标题")
        _ = directory.snapshot(server: [first], liveKeys: [first.key])
        let nextToken = directory.beginRefresh()
        let token = try #require(nextToken)
        directory.record(window(1, title: ""), standard: true, token: token, titleRead: false)
        #expect(directory.snapshot(server: [first], liveKeys: [first.key]).first?.title == "原标题")
    }

    @Test("失败的远程扫描仍有10秒尝试节流，互不阻塞其它App")
    func failedScanThrottle() {
        var throttle = RemoteScanThrottle()
        let initial = throttle.begin(pid: 1, now: 0, interval: 10)
        #expect(initial)
        for now in [1.5, 3, 4.5, 9.99] {
            let attempted = throttle.begin(pid: 1, now: now, interval: 10)
            #expect(!attempted)
        }
        let other = throttle.begin(pid: 2, now: 5, interval: 10)
        #expect(other)
        let retry = throttle.begin(pid: 1, now: 10, interval: 10)
        #expect(retry)
        throttle.keep(pids: [2])
        let freshProcess = throttle.begin(pid: 1, now: 11, interval: 10)
        #expect(freshProcess)
    }

    @Test("CG 读取失败保留缓存，明确缺失才移除")
    func serverFailureIsNotClosure() {
        var directory = WindowDirectory()
        let first = window(1)
        _ = directory.snapshot(server: [first], liveKeys: [first.key])
        #expect(directory.snapshot(server: [], liveKeys: nil) == [first])
        #expect(directory.snapshot(server: [], liveKeys: []) == [])
    }

    @Test("只有成功读到非标准子角色才排除候选窗口")
    func excludesKnownPanels() throws {
        var directory = WindowDirectory()
        let first = window(1)
        _ = directory.snapshot(server: [first], liveKeys: [first.key])
        let nextToken = directory.beginRefresh()
        let token = try #require(nextToken)
        directory.record(first, standard: nil, token: token)
        #expect(directory.snapshot(server: [first], liveKeys: [first.key]) == [first])
        directory.record(first, standard: false, token: token)
        #expect(directory.snapshot(server: [first], liveKeys: [first.key]).isEmpty)
    }

    @Test("重复列表读取只排一个 AX 补全任务")
    func coalescesRefresh() throws {
        var directory = WindowDirectory()
        let nextToken = directory.beginRefresh()
        let token = try #require(nextToken)
        #expect(directory.beginRefresh() == nil)
        directory.finishRefresh(token)
        #expect(directory.beginRefresh() != nil)
    }

    @Test("停止后旧任务不能写缓存，重新开始不接受旧 generation")
    func cancellationRejectsLateResults() throws {
        var directory = WindowDirectory()
        let oldToken = directory.beginRefresh()
        let old = try #require(oldToken)
        directory.stop()
        directory.start()
        let currentToken = directory.beginRefresh()
        let current = try #require(currentToken)
        #expect(!directory.isCurrent(old))
        directory.record(window(1), standard: true, token: old)
        #expect(directory.snapshot(server: [], liveKeys: nil).isEmpty)
        directory.finishRefresh(old)
        #expect(directory.beginRefresh() == nil)
        #expect(directory.isCurrent(current))
    }

    @Test("退出永久停止，不会被面板观察回调重新开启")
    func shutdownCannotRestart() {
        var directory = WindowDirectory()
        directory.stop(permanently: true)
        directory.start()
        #expect(directory.beginRefresh() == nil)
    }

    @Test("CG 几何保持负坐标与逻辑尺寸，不把缺失、零尺寸或非有限值当有效快照")
    func validatesServerGeometry() {
        let valid: [String: Any] = ["X": -1920, "Y": 24, "Width": 800, "Height": 600]
        #expect(WindowServerGeometry.frame(valid) == CGRect(x: -1920, y: 24, width: 800, height: 600))
        let invalidBounds: [[String: Any]] = [
            ["Y": 24, "Width": 800, "Height": 600],
            ["X": 0, "Y": 0, "Width": 0, "Height": 600],
            ["X": 0, "Y": 0, "Width": -1, "Height": 600],
            ["X": Double.nan, "Y": 0, "Width": 800, "Height": 600],
            ["X": 0, "Y": 0, "Width": Double.infinity, "Height": 600],
        ]
        for invalid in invalidBounds { #expect(WindowServerGeometry.frame(invalid) == nil) }
    }

    @Test("普通停止保留缓存，重新显示无需清空首屏")
    func stopKeepsCache() {
        var directory = WindowDirectory()
        let first = window(1)
        _ = directory.snapshot(server: [first], liveKeys: [first.key])
        directory.stop()
        directory.start()
        #expect(directory.snapshot(server: [], liveKeys: nil) == [first])
    }
}
