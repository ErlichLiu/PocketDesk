import Foundation
import Testing
@testable import SnugCore

@Suite("PocketDesk 保守激活")
struct SafeActivationTests {
    private func sample(_ names: [String]?, overlay: Bool = true, capture: Bool? = nil) -> UUSignals {
        UUSignals(overlayVisible: overlay, controllers: names.map(ControllerTable.shown) ?? .unreadable,
                  captureActive: capture, serverProcessID: 10)
    }

    @Test("手机控制 Windows：本机没有入站证据，等多久都不建屏")
    func noLocalSession() {
        var session = RemoteSession()
        for t in [0.0, 2, 60, 14_400] {
            _ = session.update(sample(nil, overlay: false, capture: false), now: at(t))
            #expect(!session.wantsPhoneScreen(now: at(t)))
        }
    }

    @Test("仅推流（包括历史回放的 active=true）不是建屏授权")
    func streamOnly() {
        var session = RemoteSession()
        for t in [0.0, 2, 60, 14_400] {
            _ = session.update(sample(nil, overlay: false, capture: true), now: at(t))
            #expect(!session.wantsPhoneScreen(now: at(t)))
        }
    }

    @Test("未知控制端不会超时变成手机", arguments: [nil, [], ["Xiaomi 15"], ["MacBook Pro", "unknown"]] as [[String]?])
    func unknown(names: [String]?) {
        var session = RemoteSession()
        for t in [0.0, 2, 300] {
            _ = session.update(sample(names), now: at(t))
            #expect(!session.wantsPhoneScreen(now: at(t)))
        }
    }

    @Test("真实本机被控手机行与当前服务推流不依赖覆盖浮层")
    func localMobileWithoutOverlay() {
        var session = RemoteSession()
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
        #expect(!session.wantsPhoneScreen(now: at(3)))
    }

    @Test("没有当前本机服务不能授权，即使名字和浮层看似匹配")
    func missingServer() {
        var session = RemoteSession()
        _ = session.update(UUSignals(overlayVisible: true, controllers: .shown(["Find N5"]),
                                     captureActive: true), now: at(0))
        #expect(!session.wantsPhoneScreen(now: at(0)))
    }

    @Test("本机表仍需在场信号，纯表格旧快照或明确停流不能单独开启")
    func scopedTableNeedsPresence() {
        for capture: Bool? in [nil, false] {
            var session = RemoteSession()
            _ = session.update(sample(["Find N5"], overlay: false, capture: capture), now: at(0))
            #expect(!session.wantsPhoneScreen(now: at(0)))
        }
    }

    @Test("本机覆盖窗口和明确手机同时出现才自动建屏", arguments: [["iPhone"], ["MacBook Pro", "iPad"]])
    func confirmed(names: [String]) {
        var session = RemoteSession()
        _ = session.update(sample(names), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
    }

    @Test("本机手机证据持续新鲜时，6秒切屏停流不撤销已确认授权")
    func streamingRestartDoesNotDisconnectControl() {
        var session = RemoteSession()
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(0))
        for t in stride(from: 0.5, through: 6.0, by: 0.5) {
            _ = session.update(sample(["Find N5"], overlay: false, capture: false), now: at(t))
            #expect(session.wantsPhoneScreen(now: at(t)))
        }
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(6.5))
        #expect(session.wantsPhoneScreen(now: at(6.5)))
    }

    @Test("持续停流不能靠重复的表格永久续期")
    func boundedTransportPause() {
        var session = RemoteSession()
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(0))
        for t in stride(from: 0.5, through: 18.0, by: 0.5) {
            _ = session.update(sample(["Find N5"], overlay: false, capture: false), now: at(t))
        }
        #expect(!session.wantsPhoneScreen(now: at(18)))
    }

    @Test("本机证据丢失过期后，不用停流中的旧表复活")
    func expiredEvidenceCannotBorrowRestartGrace() {
        var session = RemoteSession()
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(0))
        _ = session.update(sample(nil, overlay: false, capture: true), now: at(3))
        _ = session.update(sample(["Find N5"], overlay: false, capture: false), now: at(3.5))
        #expect(!session.wantsPhoneScreen(now: at(3.5)))
    }

    @Test("停流宽限不能跨服务或新手机身份继承", arguments: [true, false])
    func restartGraceBoundToIdentity(replaceServer: Bool) {
        var session = RemoteSession()
        _ = session.update(sample(["Find N5"], overlay: false, capture: true), now: at(0))
        let next = UUSignals(overlayVisible: false,
            controllers: .shown(replaceServer ? ["Find N5"] : ["iPhone"]),
            captureActive: false, serverProcessID: replaceServer ? 20 : 10)
        _ = session.update(next, now: at(1))
        #expect(!session.wantsPhoneScreen(now: at(1)))
    }

    @Test("确认过的证据也会过期，旧手机名不能被永久复用")
    func evidenceExpires() {
        var session = RemoteSession()
        _ = session.update(sample(["iPhone"]), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
        _ = session.update(sample(nil, capture: true), now: at(1))
        #expect(!session.wantsPhoneScreen(now: at(3)))
    }

    @Test("电脑或未知控制端替换手机，立即撤销旧手机授权", arguments: [["MacBook Pro"], ["unknown"], []])
    func replacement(names: [String]) {
        var session = RemoteSession()
        _ = session.update(sample(["iPhone"]), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
        _ = session.update(sample(names), now: at(1))
        #expect(!session.wantsPhoneScreen(now: at(1)))
    }

    @Test("重连窗口内先来的是电脑，不能沿用旧手机身份")
    func reconnectComputer() {
        var session = RemoteSession()
        _ = session.update(sample(["iPhone"]), now: at(0))
        _ = session.update(sample(nil, overlay: false, capture: false), now: at(2))
        _ = session.update(sample(["MacBook Pro"]), now: at(3))
        #expect(!session.wantsPhoneScreen(now: at(3)))
    }

    @Test("服务进程更换即使还在抗抖期也不继承旧手机证据")
    func serverReplaced() {
        var session = RemoteSession()
        _ = session.update(UUSignals(overlayVisible: true, controllers: .shown(["iPhone"]), captureActive: true,
                                     serverProcessID: 10), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
        _ = session.update(UUSignals(overlayVisible: true, controllers: .unreadable, captureActive: true,
                                     serverProcessID: 11), now: at(0.5))
        #expect(!session.wantsPhoneScreen(now: at(0.5)))
    }

    @Test("冲突的电脑/手机标记和phone子串都不能授权", arguments: [
        "Headphones", "Windows iPhone Simulator", "MacBook iPhone", "ThinkPad Android", "my-phone-pc"
    ])
    func ambiguousNames(name: String) {
        #expect(!ControllerKind(name: name).wantsPhoneScreen)
    }

    @Test("新连接只有历史推流、没有控制端时，不能继承上一会话手机")
    func reconnectUnknown() {
        var session = RemoteSession()
        _ = session.update(sample(["iPhone"]), now: at(0))
        _ = session.update(sample(nil, overlay: false, capture: false), now: at(2))
        _ = session.update(sample(nil, overlay: false, capture: true), now: at(3))
        #expect(!session.wantsPhoneScreen(now: at(3)))
    }
}
