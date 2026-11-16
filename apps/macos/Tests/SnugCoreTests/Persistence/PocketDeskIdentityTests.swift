import Foundation
import Testing
@testable import SnugCore

@Suite("PocketDesk 独立身份")
struct PocketDeskIdentityTests {
    @Test("数据与日志不复用 Snug")
    func independentPaths() {
        let paths = SnugPaths.standard
        #expect(paths.dataDirectory.lastPathComponent == "PocketDesk")
        #expect(paths.dataDirectory.deletingLastPathComponent().lastPathComponent == "Application Support")
        #expect(paths.logFile.lastPathComponent == "PocketDesk.log")
        #expect(paths.config.deletingLastPathComponent() == paths.dataDirectory)
        #expect(paths.phoneRecord.deletingLastPathComponent() == paths.dataDirectory)
        #expect(paths.zoomBook.deletingLastPathComponent() == paths.dataDirectory)
    }

    @Test("日志和公开命令使用独立身份")
    func publicIdentity() {
        #expect(Log.subsystem == "cool.proma.pocketdesk")
        #expect(LaunchMode.usage.contains("用法：PocketDesk"))
    }

    @Test("虚拟屏不接管上游身份")
    func independentDisplay() {
        #expect(PhoneScreenSpec.name == "PocketDesk 手机屏")
        #expect(PhoneScreenSpec.vendorID == 0x5044)
        #expect(PhoneScreenSpec.productID == 0x0002)
        #expect(PhoneScreenSpec.serials == [310_000_201, 310_000_211, 310_000_221])
        #expect(Set(PhoneScreenSpec.serials).isDisjoint(with: [200_000_108, 200_000_118, 200_000_128]))
    }

    @Test("所有存量更新源都没有网络或上游下载入口")
    func disabledUpdateSources() {
        for source in UpdateSource.allCases {
            #expect(!source.isEnabled)
            let urls = [source.feedURL, source.downloadPage, source.url(of: "Snug.pack", in: Version("9.9.9")!)]
            for url in urls {
                #expect(url.scheme == "about")
                #expect(url.host == nil)
            }
        }
    }
}
