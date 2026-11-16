import Foundation
import Testing
@testable import SnugSystem

@Suite("PocketDesk 单实例进程锁")
struct SingleInstanceLockTests {
    private func temporaryDirectory() throws -> URL {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("PocketDesk-lock-tests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory
    }

    private func remove(_ directory: URL) {
        do { try FileManager.default.removeItem(at: directory) }
        catch { Issue.record("临时锁目录清理失败：\(error)") }
    }

    @Test("同文件第二把锁立即失败，且不修改锁文件")
    func rejectsSecondAcquisition() throws {
        let directory = try temporaryDirectory()
        defer { remove(directory) }
        let url = directory.appendingPathComponent("instance.lock")
        let first = try SingleInstanceLock(url: url)
        try Data("inode sentinel".utf8).write(to: url)
        withExtendedLifetime(first) {
            #expect(throws: SingleInstanceLockError.alreadyRunning) {
                _ = try SingleInstanceLock(url: url)
            }
        }
        #expect(try String(contentsOf: url, encoding: .utf8) == "inode sentinel")
    }

    @Test("close 释放后可再获取，锁文件保持同一 inode")
    func reacquiresAfterRelease() throws {
        let directory = try temporaryDirectory()
        defer { remove(directory) }
        let url = directory.appendingPathComponent("instance.lock")
        var first: SingleInstanceLock? = try SingleInstanceLock(url: url)
        let before = try FileManager.default.attributesOfItem(atPath: url.path)[.systemFileNumber] as? NSNumber
        withExtendedLifetime(first) {
            #expect(throws: SingleInstanceLockError.alreadyRunning) {
                _ = try SingleInstanceLock(url: url)
            }
        }
        first = nil
        #expect(FileManager.default.fileExists(atPath: url.path))
        let second = try SingleInstanceLock(url: url)
        let after = try FileManager.default.attributesOfItem(atPath: url.path)[.systemFileNumber] as? NSNumber
        withExtendedLifetime(second) {
            #expect(before != nil && before == after)
        }
    }

    @Test("不同路径互不影响")
    func independentPaths() throws {
        let directory = try temporaryDirectory()
        defer { remove(directory) }
        let first = try SingleInstanceLock(url: directory.appendingPathComponent("one.lock"))
        let second = try SingleInstanceLock(url: directory.appendingPathComponent("two.lock"))
        withExtendedLifetime((first, second)) {}
    }

    @Test("拒绝符号链接且不修改其目标")
    func rejectsSymlink() throws {
        let directory = try temporaryDirectory()
        defer { remove(directory) }
        let target = directory.appendingPathComponent("target")
        let url = directory.appendingPathComponent("instance.lock")
        try Data("untouched".utf8).write(to: target)
        try FileManager.default.createSymbolicLink(at: url, withDestinationURL: target)
        do {
            _ = try SingleInstanceLock(url: url)
            Issue.record("符号链接不能取得实例锁")
        } catch let error as SingleInstanceLockError {
            guard case .fileSystem(let operation, let code, let path) = error else {
                Issue.record("符号链接错误不应伪装成已有实例：\(error)")
                return
            }
            #expect(operation == "open")
            #expect(code != 0)
            #expect(path == url.path)
        }
        #expect(try String(contentsOf: target, encoding: .utf8) == "untouched")
    }

    @Test("父目录缺失作为可见文件系统错误，不伪装成已有实例")
    func reportsFileError() throws {
        let directory = try temporaryDirectory()
        defer { remove(directory) }
        let url = directory.appendingPathComponent("missing/instance.lock")
        do {
            _ = try SingleInstanceLock(url: url)
            Issue.record("父目录不存在时必须报告错误")
        } catch let error as SingleInstanceLockError {
            guard case .fileSystem(let operation, let code, let path) = error else {
                Issue.record("文件错误不应伪装成已有实例：\(error)")
                return
            }
            #expect(operation == "open")
            #expect(code != 0)
            #expect(path == url.path)
            #expect(error.description.contains(path))
        }
    }
}
