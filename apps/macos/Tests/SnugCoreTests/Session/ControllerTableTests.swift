import Foundation
import Testing
@testable import SnugCore

/// 行文字是照 UU 的界面构造的样本（大部分取自 PR #2，作者 Feng GAO）；「远程控制 / 远程协助 / 副屏模式」取自 UU 4.46.0 的界面文案
@Suite("控制端表格 ControllerTable")
struct ControllerTableTests {
    @Test("没认出控制端行就是未识别，不是没人：没有行、首页或错误页的行、远程协助或副屏的行、有标记没名字", arguments: [
        [[String]](),
        [["我的设备", "MacBook Pro"], ["无法连接至服务器", "错误码：2003"]],
        [["iPad", "副屏模式", "连接时长 00:10"], ["iPhone", "远程协助"]],
        [["远程控制", "连接时长 00:10", ""]],
    ])
    func unrecognized(rows: [[String]]) {
        #expect(ControllerTable(rows: rows) == .unrecognized)
    }

    @Test("认出控制端：只认带「远程控制」的行，名字跳过标记、连接时长和空文字，按顺序去重")
    func shown() {
        #expect(ControllerTable(rows: [
            ["我的设备", "MacBook Pro"],
            ["", "连接时长 00:10", "iPhone", "远程控制"],
            ["iPhone", "远程控制"],
            ["iPad", "远程控制", "连接时长 00:20"],
        ]) == .shown(["iPhone", "iPad"]))
    }

    @Test("自动授权只接收独立本机被控分组", arguments: [
        [String](), ["我的设备"], ["本机被控中", "我的设备"], ["正在控制"], ["本机被控中 1"]
    ])
    func rejectUnscoped(labels: [String]) {
        #expect(ControllerTable(localSectionLabels: labels, tableCount: 1,
                                rows: [["iPhone", "远程控制"]]) == .unrecognized)
    }

    @Test("多个表格或无表格不能猜入站方向", arguments: [0, 2, 3])
    func rejectAmbiguousTables(count: Int) {
        #expect(ControllerTable(localSectionLabels: ["本机被控中"], tableCount: count,
                                rows: [["iPhone", "远程控制"]]) == .unrecognized)
    }

    @Test("限定区域仍需远程控制标记，副屏/协助不算；标题周围空白允许")
    func scopedRows() {
        #expect(ControllerTable(localSectionLabels: ["  本机被控中\n"], tableCount: 1,
                                rows: [["iPhone", "远程控制"]]) == .shown(["iPhone"]))
        #expect(ControllerTable(localSectionLabels: ["本机被控中"], tableCount: 1,
                                rows: [["iPhone", "副屏模式"], ["iPad", "远程协助"]]) == .unrecognized)
    }

    @Test("names 只在认出时有内容；日志描述三态各不相同")
    func namesAndDescription() {
        #expect(ControllerTable.unreadable.names == [])
        #expect(ControllerTable.unrecognized.names == [])
        #expect(ControllerTable.shown(["iPhone", "iPad"]).names == ["iPhone", "iPad"])
        #expect(ControllerTable.unreadable.description == "读不到")
        #expect(ControllerTable.unrecognized.description == "未识别")
        #expect(ControllerTable.shown(["iPhone", "iPad"]).description == "iPhone,iPad")
    }
}
