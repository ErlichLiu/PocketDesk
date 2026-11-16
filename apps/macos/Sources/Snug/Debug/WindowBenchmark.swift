// 只读目录时延基准，不构建AppController/显示器/手势或窗口控制意图，不申请权限。
import AppKit
import SnugCore
import SnugSystem

@MainActor
enum WindowBenchmark {
    static func run() async -> Int32 {
        NSApplication.shared.setActivationPolicy(.prohibited)
        let service = WindowService()
        var samples: [Double] = []
        var counts: [Int] = []
        var identityProbes: [WindowKey] = []
        for _ in 0..<3 {
            let began = ProcessInfo.processInfo.systemUptime
            let list = await service.list()
            samples.append((ProcessInfo.processInfo.systemUptime - began) * 1000)
            counts.append(list.count)
            if identityProbes.isEmpty { identityProbes = Array(list.prefix(3).map(\.key)) }
            try? await Task.sleep(for: .milliseconds(150))
        }
        service.stopListing(permanently: true)
        var stableIdentities = 0
        for key in identityProbes {
            if let first = await service.instanceIdentity(of: key),
               await service.instanceIdentity(of: key) == first { stableIdentities += 1 }
        }
        do {
            let data = try JSONSerialization.data(withJSONObject: [
                "read_only": true, "samples_ms": samples, "window_counts": counts,
                "accessibility_trusted": AX.isTrusted,
                "resume_identity_probes": identityProbes.count, "resume_identity_stable": stableIdentities,
                "note": "CG首屏与缓存的真实只读调用；不证明手机端显示延迟或退出/还原时延"
            ], options: [.prettyPrinted, .sortedKeys])
            print(String(decoding: data, as: UTF8.self))
            return 0
        } catch {
            FileHandle.standardError.write(Data("目录基准输出失败：\(error)\n".utf8))
            return 1
        }
    }
}
