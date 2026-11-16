import Foundation
import Testing
@testable import SnugCore

@Suite("真实UU本机被控层级")
struct UUControllerInterfaceTests {
    typealias Node = UUInterfaceNode
    private func text(_ value: String) -> Node { Node(role: "AXStaticText", text: value) }
    private func table(_ name: String = "Find N5", mark: String = "远程控制") -> Node {
        Node(role: "AXTable", children: [Node(role: "AXRow", children: [
            Node(role: "AXCell", children: [Node(role: "AXImage", text: "icon Android 40"),
                text(name), text(mark), text("连接时长 00:00:12"),
                Node(role: "AXButton", text: "断开远控")])
        ])])
    }
    // Sanitized from the real incoming Mac sample, not an invented AXGroup.
    private func incoming(name: String = "Find N5", count: String = "当前有 1 台设备正在控制此电脑",
                          disconnect: String = "断开所有远控", mark: String = "远程控制") -> Node {
        Node(role: "AXWindow", children: [
            Node(role: "AXSplitGroup", children: [Node(role: "AXScrollArea", children: [
                text("本机被控中"), text(count), text("我的设备"), text("Desktop fixture")])]),
            Node(role: "AXImage", text: "icon controlled detail back"),
            text("本机被控中"), text("00:00:12"), text(count),
            Node(role: "AXButton", text: disconnect), Node(role: "AXButton"),
            text("本账号设备"), table(name, mark: mark),
            Node(role: "AXToolbar", children: [text("Desktop fixture")])
        ])
    }

    @Test("真实AXWindow直系详情含人数、断开和单表时可确认本机Find N5")
    func realIncoming() {
        #expect(UUControllerInterface.localControllers(in: [incoming()]) == .shown(["Find N5"]))
    }

    @Test("左栏本机被控提示不授权旁边的出站设备表")
    func sidebarDoesNotScopeOutgoing() {
        let outgoing = Node(role: "AXWindow", children: [Node(role: "AXSplitGroup", children: [
            Node(role: "AXScrollArea", children: [text("本机被控中"),
                text("当前有 1 台设备正在控制此电脑"), text("我的设备")]),
            Node(role: "AXScrollArea", children: [table("iPhone")])
        ])])
        #expect(UUControllerInterface.localControllers(in: [outgoing]) == .unrecognized)
    }

    @Test("真实Windows设备页里的Find N5被控表不等于本Mac入站，即使历史推流仍为true")
    func selectedWindowsDeviceIsNotThisMac() {
        var remoteTable = table()
        remoteTable.children.insert(Node(role: "AXRow", children: [Node(role: "AXCell", children: [text("本账号设备")])]), at: 0)
        let outgoing = Node(role: "AXWindow", children: [Node(role: "AXSplitGroup", children: [
            Node(role: "AXScrollArea", children: [text("我的设备"), text("Desktop fixture"),
                Node(role: "AXImage", text: "icon device controlling count"), text("1")]),
            Node(role: "AXScrollArea", children: [Node(role: "AXImage", text: "icon top controlled bg"), remoteTable])
        ]), Node(role: "AXToolbar", children: [text("Desktop fixture")])])
        let parsed = UUControllerInterface.localControllers(in: [outgoing])
        #expect(parsed == .unrecognized)
        var session = RemoteSession()
        for t in [0.0, 2, 60] {
            _ = session.update(UUSignals(overlayVisible: false, controllers: parsed,
                                         captureActive: true, serverProcessID: 100), now: at(t))
            #expect(!session.wantsPhoneScreen(now: at(t)))
        }
    }

    @Test("本机人数不能与表格矛盾，也不能接受零人/近似文案", arguments: [
        "当前有 2 台设备正在控制此电脑", "当前有 0 台设备正在控制此电脑",
        "当前有 1 台设备正在控制其他电脑", "1台设备", ""
    ])
    func invalidCounts(count: String) {
        #expect(UUControllerInterface.localControllers(in: [incoming(count: count)]) == .unrecognized)
    }

    @Test("缺失断开全部或未知按钮不能确认该表是本机入站", arguments: ["", "连接", "正在控制", "断开远控"])
    func missingDisconnect(disconnect: String) {
        #expect(UUControllerInterface.localControllers(in: [incoming(disconnect: disconnect)]) == .unrecognized)
    }

    @Test("协助和副屏不是此自动授权行", arguments: ["远程协助", "副屏模式", "远程控制中"])
    func otherModes(mark: String) {
        #expect(UUControllerInterface.localControllers(in: [incoming(mark: mark)]) == .unrecognized)
    }

    @Test("多个本机候选不同、单区域多表、未知夹杂标签都拒绝猜测")
    func ambiguity() {
        #expect(UUControllerInterface.localControllers(in: [incoming(), incoming(name: "iPhone")]) == .unrecognized)
        var duplicate = incoming()
        duplicate.children.insert(table("iPhone"), at: duplicate.children.count - 1)
        #expect(UUControllerInterface.localControllers(in: [duplicate]) == .unrecognized)
        var mixed = incoming()
        mixed.children.insert(text("我的设备"), at: 6)
        #expect(UUControllerInterface.localControllers(in: [mixed]) == .unrecognized)
    }

    @Test("嵌套或重复的本机标题不能把窗口全部表格变成入站")
    func fakeScope() {
        #expect(UUControllerInterface.localControllers(in: [Node(role: "AXWindow", children: [
            text("本机被控中"), table()
        ])]) == .unrecognized)
        var repeated = incoming()
        repeated.children.insert(text("本机被控中"), at: 5)
        #expect(UUControllerInterface.localControllers(in: [repeated]) == .unrecognized)
    }

    @Test("真实解析结果接入会话，无浮层仍自动激活；切走详情后证据过期")
    func realPipeline() {
        var session = RemoteSession()
        let table = UUControllerInterface.localControllers(in: [incoming()])
        _ = session.update(UUSignals(overlayVisible: false, controllers: table,
                                     captureActive: true, serverProcessID: 100), now: at(0))
        #expect(session.wantsPhoneScreen(now: at(0)))
        _ = session.update(UUSignals(overlayVisible: false, controllers: .unrecognized,
                                     captureActive: true, serverProcessID: 100), now: at(3))
        #expect(!session.wantsPhoneScreen(now: at(3)))
    }

    @Test("真正入站是电脑/未知名字时也不当手机", arguments: ["Desktop fixture", "unknown"])
    func nonMobile(name: String) {
        let table = UUControllerInterface.localControllers(in: [incoming(name: name)])
        #expect(table == .shown([name]))
        var session = RemoteSession()
        _ = session.update(UUSignals(overlayVisible: false, controllers: table,
                                     captureActive: true, serverProcessID: 100), now: at(0))
        #expect(!session.wantsPhoneScreen(now: at(0)))
    }
}
