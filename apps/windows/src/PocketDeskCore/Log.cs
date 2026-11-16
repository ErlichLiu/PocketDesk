// 日志：写 %LOCALAPPDATA%\PocketDesk\pocketdesk.log。超过 2 MB 轮转一次（pocketdesk.log.1），只留一份历史。
// 排查时最要紧的是「哪一路信号在变」「窗口挪成了什么样」，所以调用方要把上下文写进消息里。
namespace PocketDesk.Core;

public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    private static string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketDesk", "pocketdesk.log");

    public static void UsePath(string path) => _path = path;

    public static string FilePath => _path;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                RotateIfNeeded();
                File.AppendAllText(_path, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 日志写不进去不能把主流程拖下水，也不静默：留一条到调试输出
                System.Diagnostics.Debug.WriteLine($"PocketDesk 日志写不进去：{e.Message} {line}");
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes) return;
        var previous = _path + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(_path, previous);
    }
}
