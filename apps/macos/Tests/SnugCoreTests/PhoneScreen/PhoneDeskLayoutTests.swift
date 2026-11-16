import CoreGraphics
import Foundation
import Testing
@testable import SnugCore

@Suite("PhoneDesk 原屏切换折叠布局")
@MainActor
struct PhoneDeskLayoutTests {
    @Test("切布局本身不授权建屏")
    func noImplicitCreation() async throws {
        let dir = try TempDir()
        let journal = Journal()
        let displays = FakeDisplays(journal: journal)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")))
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(desk.state.screen == .off)
        #expect(!journal.all.contains("create"))
        desk.setWanted(true)
        await desk.idle()
        #expect(displays.width(desk.state.display!) == 1080)
        #expect(displays.bounds(desk.state.display!)?.size == CGSize(width: 1080, height: 1191))
    }

    @Test("已放窗口在同一显示器内变宽，不覆盖原位置、不重复放大；退出仍准确归位")
    func preserveLedgerAndZoom() async throws {
        let dir = try TempDir()
        let paths = SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log"))
        let journal = Journal(), timeline = ManualTimeline()
        let displays = FakeDisplays(journal: journal)
        let windows = FakeWindows(journal: journal, ledger: paths.phoneRecord)
        let window = WindowInfo(key: WindowKey(pid: 101, id: 7), app: "Ghostty", bundleID: "com.mitchellh.ghostty", title: "foldable")
        let home = CGRect(x: 80, y: 60, width: 1200, height: 800)
        windows.add(window.key, frame: home)
        let desk = PhoneDesk(displays: displays, windows: windows, timeline: timeline, paths: paths)
        desk.setWanted(true)
        await desk.idle()
        let display = try #require(desk.state.display)
        desk.pull(window)
        await desk.idle()
        let record = RestoreRecord<PhoneRecord>(url: paths.phoneRecord).pending()
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(desk.state.display == display)
        #expect(windows.window(window.key)?.frame.width == 1080)
        #expect(windows.window(window.key)?.zoom == 2)
        #expect(RestoreRecord<PhoneRecord>(url: paths.phoneRecord).pending() == record)
        desk.setLayout(.findN5Cover)
        await desk.idle()
        #expect(desk.state.display == display)
        #expect(windows.window(window.key)?.frame.width == 570)
        #expect(journal.all.filter { $0 == "create" }.count == 1)
        await desk.shutdown()
        #expect(windows.window(window.key)?.frame == home)
        #expect(windows.window(window.key)?.zoom == 0)
        #expect(!FileManager.default.fileExists(atPath: paths.phoneRecord.path))
    }

    @Test("同宽不同高也必须换模式，快速请求只执行最后一个布局")
    func sameWidthAndCoalescing() async throws {
        let dir = try TempDir(), journal = Journal()
        let displays = FakeDisplays(journal: journal)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")))
        desk.setWanted(true)
        await desk.idle()
        let id = try #require(desk.state.display)
        displays.widths[id] = 1080
        journal.clear()
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(journal.all.contains("width \(id) 1080"))
        #expect(displays.bounds(id)?.height == 1191)
        journal.clear()
        desk.setLayout(.standard)
        desk.setLayout(.findN5Unfolded)
        desk.setLayout(.findN5Cover)
        await desk.idle()
        #expect(journal.all.filter { $0.hasPrefix("width ") } == ["width \(id) 570"])
        #expect(desk.state.display == id)
    }

    @Test("首次建屏返回旧模式也必须提示布局未生效")
    func initialModeFailureNotice() async throws {
        let dir = try TempDir(), journal = Journal()
        let displays = FakeDisplays(journal: journal)
        displays.initialMode = (.standard, 1080)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")),
                             layout: .findN5Unfolded)
        desk.setWanted(true)
        await desk.idle()
        #expect(desk.state.display != nil)
        #expect(desk.state.notice == .layoutUnavailable)
    }

    @Test("自动补偿成功只清除布局失败提示")
    func recoveryClearsLayoutFailure() async throws {
        let dir = try TempDir(), journal = Journal()
        let displays = FakeDisplays(journal: journal)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")))
        desk.setWanted(true)
        await desk.idle()
        displays.switchFails = true
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(desk.state.notice == .layoutUnavailable)
        displays.switchFails = false
        desk.displaysChanged()
        await desk.idle()
        #expect(displays.bounds(desk.state.display!)?.size == CGSize(width: 1080, height: 1191))
        #expect(desk.state.notice == nil)
    }

    @Test("关闭时不执行排队的无必要布局切换，仍走撤屏")
    func shutdownSkipsPendingLayout() async throws {
        let dir = try TempDir(), journal = Journal()
        let displays = FakeDisplays(journal: journal)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")))
        desk.setWanted(true)
        await desk.idle()
        journal.clear()
        desk.setLayout(.findN5Unfolded)
        await desk.shutdown()
        #expect(!journal.all.contains { $0.hasPrefix("width ") })
        #expect(journal.all.contains { $0.hasPrefix("retire ") })
    }

    @Test("换档失败不能谎报成功或销账，可用同一个布局重试")
    func retryFailedLayout() async throws {
        let dir = try TempDir(), journal = Journal()
        let displays = FakeDisplays(journal: journal)
        let desk = PhoneDesk(displays: displays, windows: FakeWindows(journal: journal), timeline: ManualTimeline(),
                             paths: SnugPaths(dataDirectory: dir.url, logFile: dir.url.appendingPathComponent("test.log")))
        desk.setWanted(true)
        await desk.idle()
        displays.switchFails = true
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(desk.state.notice == .layoutUnavailable)
        displays.switchFails = false
        desk.setLayout(.findN5Unfolded)
        await desk.idle()
        #expect(desk.state.notice == nil)
        #expect(displays.bounds(desk.state.display!)?.size == CGSize(width: 1080, height: 1191))
    }
}
