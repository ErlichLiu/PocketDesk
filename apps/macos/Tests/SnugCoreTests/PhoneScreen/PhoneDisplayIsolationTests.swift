import Testing
@testable import SnugCore

@Suite("显示器帮手隔离")
struct PhoneDisplayIsolationTests {
    @Test("只接收本产品的完整显示器身份", arguments: PhoneScreenSpec.serials)
    func ownSerial(serial: UInt32) {
        #expect(PhoneScreenSpec.owns(vendorID: PhoneScreenSpec.vendorID, productID: PhoneScreenSpec.productID, serial: serial))
    }

    @Test("上游/其他屏/仅碰巧同vendor都不准切换")
    func foreignDisplays() {
        #expect(!PhoneScreenSpec.owns(vendorID: 0x534E, productID: 0x00F0, serial: 200_000_108))
        #expect(!PhoneScreenSpec.owns(vendorID: 0, productID: 0, serial: 0))
        #expect(!PhoneScreenSpec.owns(vendorID: 0x5044, productID: 0x0001, serial: 310_000_101))
        #expect(!PhoneScreenSpec.owns(vendorID: PhoneScreenSpec.vendorID, productID: 0xFF, serial: PhoneScreenSpec.serials[0]))
        #expect(!PhoneScreenSpec.owns(vendorID: PhoneScreenSpec.vendorID, productID: PhoneScreenSpec.productID, serial: 42))
    }

    @Test("帮手只允许已登记档位，拒绝溢出或非法档位", arguments: [1, 539, 541, 1081, Int.max])
    func invalidWidth(width: Int) {
        #expect(throws: (any Error).self) { try LaunchMode.parse(["--set-mode", "123", String(width)]) }
    }

    @Test("帮手保留全部合法档位", arguments: ScreenFit.widths)
    func legalWidth(width: Int) throws {
        #expect(try LaunchMode.parse(["--set-mode", "123", String(width)]) == .setMode(display: 123, width: width))
    }
}
