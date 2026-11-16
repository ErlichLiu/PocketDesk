import CoreGraphics
import Foundation
import Testing
@testable import SnugCore

@Suite("Find N5 屏幕布局")
struct PhoneLayoutTests {
    @Test("展开后是真正更宽的工作区，不是拉伸原竖屏")
    func unfoldedContent() {
        let standard = PhoneLayout.standard.size(width: 540)
        let inner = PhoneLayout.findN5Unfolded.size(width: 1080)
        #expect(inner == CGSize(width: 1080, height: 1191))
        #expect(inner.width == standard.width * 2)
        #expect(inner.width * inner.height > standard.width * standard.height * 2.4)
        #expect(abs(inner.width / inner.height - 2248.0 / 2480) < 0.001)
    }

    @Test("外屏基准2倍像素对应官方1140×2616")
    func coverContent() {
        let outer = PhoneLayout.findN5Cover.size(width: 570)
        #expect(outer == CGSize(width: 570, height: 1308))
    }

    @Test("所有登记模式有界、去重且支持2倍", arguments: PhoneLayout.allCases)
    func boundedModes(layout: PhoneLayout) throws {
        #expect(layout.widths.first == layout.baseWidth)
        for mode in layout.modes {
            #expect(mode.width >= 540)
            #expect(mode.width <= 1440)
            #expect(mode.width * 2 <= Int(PhoneLayout.maximumPixels.width))
            #expect(mode.height * 2 <= Int(PhoneLayout.maximumPixels.height))
            #expect(PhoneLayout.supports(width: mode.width, height: mode.height))
            #expect(try LaunchMode.parse(["--set-size", "123", String(mode.width), String(mode.height)])
                == .setSize(display: 123, width: mode.width, height: mode.height))
        }
        #expect(Set(PhoneLayout.allModes).count == PhoneLayout.allModes.count)
        #expect(PhoneLayout.allModes.count < 100)
    }

    @Test("只允许完整宽高组合，不接受任意比例、交换值或溢出")
    func helperModes() throws {
        #expect(try LaunchMode.parse(["--set-size", "123", "1080", "1191"]) == .setSize(display: 123, width: 1080, height: 1191))
        for pair in [(1080, 1901), (1191, 1080), (540, 540), (Int.max, Int.max)] {
            #expect(throws: (any Error).self) {
                try LaunchMode.parse(["--set-size", "123", String(pair.0), String(pair.1)])
            }
        }
    }

    @Test("旧配置缺字段保持原布局，新配置可回读；未来未知布局回落而不是整份隔离")
    func configCompatibility() throws {
        #expect(try JSONDecoder().decode(AppConfig.self, from: Data("{}".utf8)).phoneLayout == .standard)
        var config = AppConfig()
        config.phoneLayout = .findN5Unfolded
        #expect(try JSONDecoder().decode(AppConfig.self, from: JSONEncoder().encode(config)).phoneLayout == .findN5Unfolded)
        #expect(try JSONDecoder().decode(AppConfig.self, from: Data(#"{"phoneLayout":"future-layout"}"#.utf8)).phoneLayout == .standard)
    }

    @Test("N5名称是手机线索，但冲突电脑标记仍拒绝")
    func controllerKind() {
        for name in ["OPPO Find N5", "FindN5", "PKH110", "CPH2671"] {
            #expect(ControllerKind(name: name) == .mobile)
        }
        #expect(ControllerKind(name: "Windows Find N5").wantsPhoneScreen == false)
    }
}
