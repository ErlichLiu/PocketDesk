import Darwin
import Foundation

public enum SingleInstanceLockError: Error, Equatable, Sendable, CustomStringConvertible {
    case alreadyRunning
    case fileSystem(operation: String, code: Int32, path: String)

    public var description: String {
        switch self {
        case .alreadyRunning:
            "PocketDesk 已在运行，请使用现有实例"
        case .fileSystem(let operation, let code, let path):
            "实例锁文件操作失败（\(operation)，\(path)，errno \(code)）：\(String(cString: strerror(code)))"
        }
    }
}

/// 持有对象即持有实例锁；调用方须用 withExtendedLifetime 覆盖整个 app.run()。
/// 父目录由调用方准备。锁文件永久保留，绝不 unlink，以免新旧实例锁到不同 inode。
public final class SingleInstanceLock {
    private let descriptor: Int32

    public init(url: URL) throws {
        let path = url.path
        guard url.isFileURL, !path.utf8.contains(0) else {
            throw SingleInstanceLockError.fileSystem(operation: "open", code: EINVAL, path: path)
        }
        // 不跟随符号链接、不继承到帮手进程；非阻塞打开也避免异常 FIFO 卡住启动。
        let fd = Darwin.open(path, O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK, mode_t(0o600))
        guard fd >= 0 else {
            throw SingleInstanceLockError.fileSystem(operation: "open", code: errno, path: path)
        }
        var status = stat()
        guard fstat(fd, &status) == 0 else {
            let code = errno
            Darwin.close(fd)
            throw SingleInstanceLockError.fileSystem(operation: "fstat", code: code, path: path)
        }
        guard (status.st_mode & mode_t(S_IFMT)) == mode_t(S_IFREG) else {
            Darwin.close(fd)
            throw SingleInstanceLockError.fileSystem(operation: "regular-file", code: EINVAL, path: path)
        }
        guard flock(fd, LOCK_EX | LOCK_NB) == 0 else {
            let code = errno
            Darwin.close(fd)
            if code == EWOULDBLOCK || code == EAGAIN { throw SingleInstanceLockError.alreadyRunning }
            throw SingleInstanceLockError.fileSystem(operation: "flock", code: code, path: path)
        }
        descriptor = fd
    }

    deinit {
        // close 随对象销毁/进程结束释放锁；不单独解锁、不删文件，也不重试可能已关闭的 fd。
        Darwin.close(descriptor)
    }
}
