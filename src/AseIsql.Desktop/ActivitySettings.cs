using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AseIsql.Desktop;

// Activity monitor preferences, kept across restarts. OnlyActive hides idle sessions (blockers and blocked ones stay).
public sealed record ActivitySettings(bool OnlyActive = false);

public static class ActivitySettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string PathName => DataFolder.File("activity.json");
    public static ActivitySettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new IOException("Activity settings exceed 64 KiB.");
        return JsonSerializer.Deserialize<ActivitySettings>(File.ReadAllText(path), Options) ?? throw new JsonException("Empty activity settings.");
    }
    public static void Save(string path, ActivitySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFiles.SaveUtf8(path, JsonSerializer.Serialize(settings, Options));
    }
}
