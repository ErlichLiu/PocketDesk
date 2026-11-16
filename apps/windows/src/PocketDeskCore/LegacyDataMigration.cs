using System.Text.Json;
using PocketDesk.Core.PhoneScreen;

namespace PocketDesk.Core;

/// <summary>只迁移设置，不接管旧版仍未恢复的窗口，也不修改旧文件。</summary>
public static class LegacyDataMigration
{
    public const string LegacyName = "Snug";

    public static bool TryMigrate(string legacyRoot, string destinationRoot, out string? error)
    {
        error = null;
        if (!Directory.Exists(legacyRoot) || File.Exists(Path.Combine(destinationRoot, "config.json"))) return true;
        try
        {
            var ledger = Path.Combine(legacyRoot, "phone-screen.json");
            string? ledgerText;
            try { ledgerText = File.ReadAllText(ledger); }
            catch (FileNotFoundException) { ledgerText = null; }
            catch (DirectoryNotFoundException) { ledgerText = null; }
            // 目录占位、权限错误等不是“空账本”；让外层捕获并停止迁移。
            if (ledgerText is not null)
            {
                var pending = JsonSerializer.Deserialize<List<PhoneRecord>>(ledgerText);
                if (pending is null || pending.Count > 0)
                {
                    error = "旧版仍有待还原窗口，请先用旧版恢复窗口并安全退出，再启动 PocketDesk。";
                    return false;
                }
            }
            // 先验证所有待复制文件。仅接受 JSON，不迁移日志、恢复事务或证书。
            var sources = new List<(string Source, string Name)>();
            foreach (var name in new[] { "config.json", "zoom.json" })
            {
                var source = Path.Combine(legacyRoot, name);
                if (!File.Exists(source)) continue;
                using var parsed = JsonDocument.Parse(File.ReadAllText(source));
                if (parsed.RootElement.ValueKind is not JsonValueKind.Object)
                    throw new JsonException("旧版设置必须是 JSON 对象");
                sources.Add((source, name));
            }
            if (sources.Count == 0) return true;
            Directory.CreateDirectory(destinationRoot);
            // config 最后复制：它存在代表此前待迁移文件已完成，失败重试不会漏掉字号文件。
            foreach (var (source, name) in sources.OrderBy(item => item.Name == "config.json"))
            {
                var destination = Path.Combine(destinationRoot, name);
                if (!File.Exists(destination)) File.Copy(source, destination, overwrite: false);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "旧版数据迁移未完成，原文件未删除：" + exception.Message;
            return false;
        }
    }
}
