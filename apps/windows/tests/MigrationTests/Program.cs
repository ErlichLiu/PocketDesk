using PocketDesk.Core;

var root = Path.Combine(Path.GetTempPath(), "pocketdesk-migration-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var count = 0;
try
{
    Test("missing legacy directory is a no-op", (old, next) =>
    {
        Directory.Delete(old);
        Check(LegacyDataMigration.TryMigrate(old, next, out _) && !Directory.Exists(next));
    });
    Test("copies validated settings, never logs or the recovery ledger", (old, next) =>
    {
        File.WriteAllText(Path.Combine(old, "config.json"), "{\"Profile\":\"fold\"}");
        File.WriteAllText(Path.Combine(old, "zoom.json"), "{\"Code\":2}");
        File.WriteAllText(Path.Combine(old, "phone-screen.json"), "[]");
        File.WriteAllText(Path.Combine(old, "private.log"), "not for migration");
        Check(LegacyDataMigration.TryMigrate(old, next, out _));
        Check(Directory.GetFiles(next).Length == 2);
        Check(File.ReadAllText(Path.Combine(old, "config.json")) == File.ReadAllText(Path.Combine(next, "config.json")));
        Check(!File.Exists(Path.Combine(next, "phone-screen.json")));
    });
    Test("pending legacy recovery prevents migration", (old, next) =>
    {
        File.WriteAllText(Path.Combine(old, "phone-screen.json"), "[{\"Pid\":42}]");
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        Check(!LegacyDataMigration.TryMigrate(old, next, out var error) && error is not null);
        Check(!Directory.Exists(next));
    });
    Test("malformed legacy ledger is not treated as empty", (old, next) =>
    {
        File.WriteAllText(Path.Combine(old, "phone-screen.json"), "not-json");
        Check(!LegacyDataMigration.TryMigrate(old, next, out _));
        Check(!Directory.Exists(next));
    });
    Test("existing destination config is never overwritten", (old, next) =>
    {
        Directory.CreateDirectory(next);
        File.WriteAllText(Path.Combine(old, "config.json"), "{\"Profile\":\"phone\"}");
        File.WriteAllText(Path.Combine(next, "config.json"), "{\"Profile\":\"fold\"}");
        Check(LegacyDataMigration.TryMigrate(old, next, out _));
        Check(File.ReadAllText(Path.Combine(next, "config.json")) == "{\"Profile\":\"fold\"}");
    });
    Test("all inputs are validated before copying any settings", (old, next) =>
    {
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        File.WriteAllText(Path.Combine(old, "zoom.json"), "[]");
        Check(!LegacyDataMigration.TryMigrate(old, next, out _));
        Check(!Directory.Exists(next));
    });
    Test("existing destination zoom is retained", (old, next) =>
    {
        Directory.CreateDirectory(next);
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        File.WriteAllText(Path.Combine(old, "zoom.json"), "{\"Code\":2}");
        File.WriteAllText(Path.Combine(next, "zoom.json"), "{\"Code\":0}");
        Check(LegacyDataMigration.TryMigrate(old, next, out _));
        Check(File.ReadAllText(Path.Combine(next, "zoom.json")) == "{\"Code\":0}");
    });
    Test("destination write failure leaves originals intact", (old, next) =>
    {
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        File.WriteAllText(next, "blocking file");
        Check(!LegacyDataMigration.TryMigrate(old, next, out _));
        Check(File.ReadAllText(Path.Combine(old, "config.json")) == "{}");
    });
    Test("directory in place of a ledger fails closed", (old, next) =>
    {
        Directory.CreateDirectory(Path.Combine(old, "phone-screen.json"));
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        Check(!LegacyDataMigration.TryMigrate(old, next, out _));
        Check(!Directory.Exists(next));
    });
    Test("interrupted copy can retry without losing the remaining settings", (old, next) =>
    {
        Directory.CreateDirectory(Path.Combine(next, "config.json"));
        File.WriteAllText(Path.Combine(old, "config.json"), "{}");
        File.WriteAllText(Path.Combine(old, "zoom.json"), "{\"Code\":2}");
        Check(!LegacyDataMigration.TryMigrate(old, next, out _));
        Check(File.Exists(Path.Combine(next, "zoom.json")));
        Directory.Delete(Path.Combine(next, "config.json"));
        Check(LegacyDataMigration.TryMigrate(old, next, out _));
        Check(File.Exists(Path.Combine(next, "config.json")));
        Check(File.ReadAllText(Path.Combine(next, "zoom.json")) == "{\"Code\":2}");
    });
    Console.WriteLine($"{count}/10 migration tests passed; real user state was not accessed.");
}
finally { Directory.Delete(root, recursive: true); }

void Test(string name, Action<string, string> test)
{
    var testRoot = Path.Combine(root, Guid.NewGuid().ToString());
    var old = Path.Combine(testRoot, "legacy");
    Directory.CreateDirectory(old);
    test(old, Path.Combine(testRoot, "new"));
    count++;
    Console.WriteLine("PASS " + name);
}
static void Check(bool condition) { if (!condition) throw new Exception("Migration assertion failed"); }
