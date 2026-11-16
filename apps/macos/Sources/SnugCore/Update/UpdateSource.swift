// 保留上游配置值的解码兼容性，但 PocketDesk 首版不启用任何更新渠道。
// 地址只能是无网络的占位符，不能因存量配置或隐藏入口重新连上 Snug 发布源。
import Foundation

public enum UpdateSource: String, Codable, CaseIterable, Sendable {
    case tencent
    case github

    public var isEnabled: Bool { false }
    public var title: String { "本地构建（更新已停用）" }

    private static let disabledURL = URL(string: "about:blank#pocketdesk-updates-disabled")!

    public var feedURL: URL { Self.disabledURL }
    public func url(of file: String, in version: Version) -> URL { Self.disabledURL }
    public var downloadPage: URL { Self.disabledURL }
}
