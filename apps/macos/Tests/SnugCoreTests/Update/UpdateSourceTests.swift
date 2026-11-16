import Foundation
import Testing
@testable import SnugCore

@Suite("更新源 UpdateSource：PocketDesk 禁用")
struct UpdateSourceTests {
    @Test("存量配置值可解码，但任何源都不得访问上游", arguments: UpdateSource.allCases)
    func disabled(source: UpdateSource) throws {
        let decoded = try JSONDecoder().decode(UpdateSource.self, from: JSONEncoder().encode(source))
        #expect(decoded == source)
        #expect(!decoded.isEnabled)
        #expect(decoded.feedURL.absoluteString == "about:blank#pocketdesk-updates-disabled")
        #expect(decoded.downloadPage == decoded.feedURL)
    }

    @Test("不因版本、包名或路径注入重启下载", arguments: ["Snug-2.1.0.pack", "../../update.json", "https://example.com/file"])
    func noPackageURL(file: String) {
        for source in UpdateSource.allCases {
            let url = source.url(of: file, in: Version(major: 9, minor: 9, patch: 9))
            #expect(url == source.feedURL)
            #expect(url.host == nil)
        }
    }
}
