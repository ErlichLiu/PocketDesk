// 先持久化原始位置，再移动；只有实际复原且销账落盘成功，才算完成。
namespace PocketDesk.Core.PhoneScreen;

/// <summary>WINDOWPLACEMENT 的原始值，保留工作区坐标、最大化/最小化及 restore-to-maximized 标记。</summary>
public sealed record SavedWindowPlacement(
    int Flags, int ShowCommand, int MinX, int MinY, int MaxX, int MaxY,
    int Left, int Top, int Right, int Bottom);

public sealed record PhoneRecord(
    int Pid, long Handle, string App, string Label,
    int X, int Y, int Width, int Height, bool WasFullScreen, bool WasMinimized,
    SavedWindowPlacement? Placement = null,
    int AppliedZoom = 0,
    long? ProcessStartTimeUtcTicks = null)
{
    public WindowKey Key => new(Pid, Handle);
}

public sealed class RestoreRecord
{
    private readonly string _path;
    private readonly object _lock = new();
    private List<PhoneRecord> _records;

    /// <summary>损坏/不可读的文件不得当成空账本覆盖；修复文件并重启后才允许继续。</summary>
    public bool IsReadable { get; private set; } = true;

    public RestoreRecord(string path)
    {
        _path = Path.GetFullPath(path);
        _records = Load();
    }

    public IReadOnlyList<PhoneRecord> All
    {
        get { lock (_lock) return _records.ToArray(); }
    }

    public PhoneRecord? Find(WindowKey key)
    {
        lock (_lock) return _records.FirstOrDefault(r => r.Key == key);
    }

    /// <summary>不能覆盖同一窗口尚未恢复的原始记录，失败时内存与磁盘都不变。</summary>
    public bool Add(PhoneRecord record)
    {
        lock (_lock)
        {
            if (!IsReadable || !Valid(record) || _records.Any(r => r.Key == record.Key)) return false;
            return Commit(_records.Append(record).ToList());
        }
    }

    /// <summary>只允许更新实际字号增量，不能重写原始位置/身份。</summary>
    public bool Update(PhoneRecord record)
    {
        lock (_lock)
        {
            var index = _records.FindIndex(r => r.Key == record.Key);
            if (!IsReadable || index < 0 || !Valid(record)
                || _records[index] with { AppliedZoom = record.AppliedZoom } != record) return false;
            var next = _records.ToList();
            next[index] = record;
            return Commit(next);
        }
    }

    /// <summary>销账失败保留内存记录，下次继续恢复，而不是误认为已经清空。</summary>
    public bool Remove(WindowKey key)
    {
        lock (_lock)
        {
            if (!IsReadable) return false;
            var next = _records.Where(r => r.Key != key).ToList();
            return next.Count == _records.Count || Commit(next);
        }
    }

    private static bool Valid(PhoneRecord r) => r.Pid > 0 && r.Handle != 0
        && r.Width > 0 && r.Height > 0
        && (long)r.X + r.Width <= int.MaxValue && (long)r.Y + r.Height <= int.MaxValue
        && r.AppliedZoom is >= -100 and <= 100
        && (r.Placement is not { } p || (p.Right > p.Left && p.Bottom > p.Top
            && p.ShowCommand is >= 1 and <= 11));

    private List<PhoneRecord> Load()
    {
        try
        {
            // File.Exists 会把无权限/目录路径也伪装成“不存在”，不能据此允许覆盖。
            var records = System.Text.Json.JsonSerializer.Deserialize<List<PhoneRecord>>(File.ReadAllText(_path));
            if (records is null || records.Any(r => r is null || !Valid(r))
                || records.Select(r => r.Key).Distinct().Count() != records.Count)
                throw new System.Text.Json.JsonException("还原记录为空、身份重复或位置无效");
            return records;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            IsReadable = false;
            Log.Error($"还原记录无法安全读取，禁止搬窗口且保留原文件：{e.Message}");
            return new();
        }
    }

    private bool Commit(List<PhoneRecord> next)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = System.Text.Json.JsonSerializer.Serialize(next,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, _path, overwrite: true);
            _records = next;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"还原记录写不进去，保留原账本：{e.Message}");
            return false;
        }
        finally
        {
            if (temp is not null)
                try { File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
