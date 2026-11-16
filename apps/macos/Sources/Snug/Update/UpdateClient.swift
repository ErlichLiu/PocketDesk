// 第二道边界：即使未来误接回 Updater，也不允许下载上游 feed 或更新包。
import Foundation
import SnugCore

struct UpdateClient: Sendable {
    func feed(from source: UpdateSource, waitForNetwork: Bool) async throws -> UpdateFeed {
        throw UpdateClientError.disabled
    }

    func fetch(_ url: URL, range: Range<Int>, progress: @escaping @MainActor (Int) -> Void) async throws -> Data {
        throw UpdateClientError.disabled
    }
}

enum UpdateClientError: Error, CustomStringConvertible {
    case disabled
    var description: String { "PocketDesk 在线更新已停用，禁止联网下载" }
}
