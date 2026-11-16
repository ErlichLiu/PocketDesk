// Snug 与旧版 UU远程伴侣都可能管理虚拟屏。PocketDesk 不与它们并行启用，也绝不退出对方。
import AppKit
import SnugCore

@MainActor
enum LegacyApp {
    nonisolated static let bundleID = "com.corndog.uudisplay"
    private static let conflictingBundleIDs: Set<String> = [bundleID, "com.zephyr4123.snug"]

    private static func conflicts(_ app: NSRunningApplication) -> Bool {
        guard app.processIdentifier != ProcessInfo.processInfo.processIdentifier else { return false }
        return app.bundleIdentifier.map { conflictingBundleIDs.contains($0) } == true
            || app.executableURL?.lastPathComponent == "Snug"
            || app.bundleURL?.lastPathComponent == "Snug.app"
    }

    static var isRunning: Bool { NSWorkspace.shared.runningApplications.contains(where: conflicts) }

    /// 在构建 AppController 前检查；启动回调中再检查一次，缩小启动竞争窗口。
    static func allowStartup() -> Bool {
        guard isRunning else { return true }
        Log.warn("检测到 Snug 或旧版 UU远程伴侣，PocketDesk 不启动监控，也不退出对方")
        let alert = NSAlert()
        alert.messageText = "PocketDesk 暂未启动"
        alert.informativeText = "检测到 Snug 或旧版 UU远程伴侣正在运行。为避免两个程序同时管理显示器，请先自行退出对方，再重新打开 PocketDesk。本次不会创建屏幕、恢复窗口或退出其他应用。"
        alert.addButton(withTitle: "关闭 PocketDesk")
        alert.runModal()
        return false
    }

    /// 保留旧意图接口，但即使隐藏按钮误调用也不能退出另一个应用。
    static func quit() {
        Log.warn("PocketDesk 不会退出 Snug 或旧版伴侣；请用户自行操作")
    }

    /// 只有冲突应用确实退出才继续；超时不得沿用上游的放行行为。
    static func waitUntilGone(timeout: TimeInterval, then done: @escaping @MainActor () -> Void) {
        guard isRunning else { return done() }
        guard timeout > 0 else {
            Log.warn("冲突应用仍在运行，PocketDesk 保持停止")
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
            MainActor.assumeIsolated { waitUntilGone(timeout: timeout - 0.5, then: done) }
        }
    }

    static func observe(_ onChange: @escaping @MainActor (Bool) -> Void) -> [any NSObjectProtocol] {
        let center = NSWorkspace.shared.notificationCenter
        return [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification].map { name in
            center.addObserver(forName: name, object: nil, queue: .main) { _ in
                MainActor.assumeIsolated { onChange(isRunning) }
            }
        }
    }
}
