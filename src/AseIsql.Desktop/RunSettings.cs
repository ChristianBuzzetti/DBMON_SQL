using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AseIsql.Desktop;

// How scripts run, kept across restarts. ContinueOnError: a failed batch is recorded in Messages and the next batch
// runs, as isql does when told to continue; off (the default) stops at the first error.
public sealed record RunSettings(bool ContinueOnError = false);

public static class RunSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string PathName => DataFolder.File("run.json");
    public static RunSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new IOException("Run settings exceed 64 KiB.");
        return JsonSerializer.Deserialize<RunSettings>(File.ReadAllText(path), Options) ?? throw new JsonException("Empty run settings.");
    }
    public static void Save(string path, RunSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFiles.SaveUtf8(path, JsonSerializer.Serialize(settings, Options));
    }
}
