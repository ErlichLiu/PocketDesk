// 第三道边界：首版不替换、不重新启动任何应用包；不保留可误调用的原子替换或 shell 启动实现。
import Foundation
import SnugCore

enum AppBundle {
    static func verifySignature(of app: URL) throws { throw AppBundleError.disabled }

    static func version(of app: URL) -> Version? {
        let info = NSDictionary(contentsOf: app.appendingPathComponent("Contents/Info.plist"))
        return (info?["CFBundleShortVersionString"] as? String).flatMap(Version.init)
    }

    static func swap(_ a: URL, _ b: URL) throws { throw AppBundleError.disabled }
    static func relaunch(_ app: URL) throws { throw AppBundleError.disabled }
}

enum AppBundleError: Error, CustomStringConvertible {
    case disabled
    var description: String { "PocketDesk 首版不支持应用内替换或重启，请显式构建本地版本" }
}
