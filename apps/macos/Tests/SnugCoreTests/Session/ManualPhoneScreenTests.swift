import Testing
@testable import SnugCore

@Suite("手动手机屏是有界的本次授权")
struct ManualPhoneScreenTests {
    private func signal(_ overlay: Bool) -> UUSignals {
        UUSignals(overlayVisible: overlay, controllers: .unreadable, captureActive: nil, serverProcessID: 10)
    }

    @Test("默认关闭；只在用户请求后启用，重建状态不继承")
    func explicitOnly() {
        var manual = ManualPhoneScreenAuthorization()
        #expect(!manual.isEnabled(at: at(0)))
        manual.begin(at: at(0))
        #expect(manual.isEnabled(at: at(0)))
        #expect(!ManualPhoneScreenAuthorization().isEnabled(at: at(0)))
        manual.cancel()
        #expect(!manual.isEnabled(at: at(1)))
    }

    @Test("未连接时仅允许30秒准备时间，不能无限挂着")
    func waitingTimeout() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0))
        #expect(manual.isEnabled(at: at(30)))
        #expect(!manual.isEnabled(at: at(30.1)))
    }

    @Test("本机连接消失15秒后授权结束；后续新连接不能复活它")
    func localDisconnect() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0))
        manual.update(signal(true), now: at(10))
        #expect(manual.isEnabled(at: at(25)))
        manual.update(signal(false), now: at(25.1))
        #expect(!manual.isEnabled(at: at(25.1)))
        manual.update(signal(true), now: at(26))
        #expect(!manual.isEnabled(at: at(26)))
    }

    @Test("首次补齐未知设备名保留手动授权，真正换人则撤销")
    func firstNameIsNotReplacement() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0), serverProcessID: 100, controllers: [])
        manual.update(UUSignals(overlayVisible: true, controllers: .shown(["Xiaomi 15"]), captureActive: true,
                                 serverProcessID: 100), now: at(1))
        #expect(manual.isEnabled(at: at(1)))
        manual.update(UUSignals(overlayVisible: true, controllers: .shown(["DESKTOP-123"]), captureActive: true,
                                 serverProcessID: 100), now: at(2))
        #expect(!manual.isEnabled(at: at(2)))
    }

    @Test("已绑定服务的手动授权不能跨PID继承，即使覆盖窗口和设备名不变")
    func processReplacement() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0), serverProcessID: 100, controllers: ["Xiaomi 15"])
        manual.update(UUSignals(overlayVisible: true, controllers: .shown(["Xiaomi 15"]), captureActive: true,
                                 serverProcessID: 200), now: at(0.5))
        #expect(!manual.isEnabled(at: at(0.5)))
    }

    @Test("开始时没有服务，首个连接可绑定；绑定后的服务消失则撤销")
    func bindFirstServer() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0))
        manual.update(UUSignals(overlayVisible: true, controllers: .unreadable, captureActive: nil,
                                 serverProcessID: 100), now: at(1))
        #expect(manual.isEnabled(at: at(1)))
        manual.update(UUSignals(overlayVisible: false, controllers: .unreadable, captureActive: nil,
                                 serverProcessID: nil), now: at(2))
        #expect(!manual.isEnabled(at: at(2)))
    }

    @Test("旧会话结束和新手动授权独立：迟到ended不能撤销新请求")
    func oldEpisodeDoesNotOwnNewRequest() {
        var session = RemoteSession()
        let absent = UUSignals(overlayVisible: false, controllers: .unreadable, captureActive: false, serverProcessID: 100)
        _ = session.update(UUSignals(overlayVisible: true, controllers: .shown(["Xiaomi 15"]), captureActive: true,
                                     serverProcessID: 100), now: at(0))
        #expect(session.update(absent, now: at(2)) == [.interrupted])
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(17.1), serverProcessID: 100)
        #expect(session.update(absent, now: at(17.5)) == [.ended])
        manual.update(absent, now: at(17.5))
        #expect(manual.isEnabled(at: at(17.5)))
        let reconnected = UUSignals(overlayVisible: true, controllers: .shown(["Xiaomi 15"]), captureActive: true,
                                    serverProcessID: 100)
        _ = session.update(reconnected, now: at(18))
        manual.update(reconnected, now: at(18))
        #expect(manual.isEnabled(at: at(18)))
        #expect(!session.wantsPhoneScreen(now: at(18)))
    }

    @Test("无浮层的本次明确授权可由同一服务推流续期，失联到期后不复活")
    func captureKeepsExplicitIntent() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0), serverProcessID: 100)
        let present = UUSignals(overlayVisible: false, controllers: .unrecognized,
                                captureActive: true, serverProcessID: 100)
        for t in stride(from: 1.0, through: 60, by: 1) { manual.update(present, now: at(t)) }
        #expect(manual.isEnabled(at: at(60)))
        let absent = UUSignals(overlayVisible: false, controllers: .unrecognized,
                               captureActive: false, serverProcessID: 100)
        manual.update(absent, now: at(75.1))
        #expect(!manual.isEnabled(at: at(75.1)))
        manual.update(present, now: at(76))
        #expect(!manual.isEnabled(at: at(76)))
    }

    @Test("没有服务的推流状态不能无限延长准备时间")
    func noServerDoesNotRenew() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0))
        manual.update(UUSignals(overlayVisible: false, controllers: .unreadable,
                                captureActive: true), now: at(20))
        #expect(!manual.isEnabled(at: at(31)))
    }

    @Test("即使连接一直在，手动授权30分钟到期")
    func hardTimeout() {
        var manual = ManualPhoneScreenAuthorization()
        manual.begin(at: at(0))
        for t in stride(from: 1.0, through: 1801.0, by: 1) {
            manual.update(signal(true), now: at(t))
        }
        #expect(!manual.isEnabled(at: at(1801)))
    }
}
