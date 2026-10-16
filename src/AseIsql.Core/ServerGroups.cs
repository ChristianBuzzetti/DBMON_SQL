using System.Text.Json;
using System.Text.Json.Serialization;

namespace AseIsql;

// A named set of connection profiles for multi-server runs. Profiles never carry a password: opening a group asks
// for each one at login, as a single profile does.
public sealed record ServerGroup(string Name, IReadOnlyList<ConnectionProfile> Profiles);

public static class ServerGroupStore
{
    public const int MaxGroups = 50;
    public const long MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static IReadOnlyList<ServerGroup> Load(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > MaxBytes) throw new IOException("The server groups file exceeds 1 MiB.");
        var groups = JsonSerializer.Deserialize<List<ServerGroup>>(File.ReadAllText(path), Json) ?? throw new JsonException("Empty server groups file.");
        Validate(groups);
        return groups;
    }

    public static void Save(string path, IReadOnlyList<ServerGroup> groups)
    {
        Validate(groups);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFiles.SaveUtf8(path, JsonSerializer.Serialize(groups, Json));
    }

    // Replaces a group with the same name (case-insensitive) or adds it, keeping the others in their order.
    public static IReadOnlyList<ServerGroup> Upsert(IReadOnlyList<ServerGroup> groups, ServerGroup group)
    {
        var result = groups.ToList();
        int index = result.FindIndex(g => g.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) result[index] = group; else result.Add(group);
        Validate(result);
        return result;
    }

    public static IReadOnlyList<ServerGroup> Remove(IReadOnlyList<ServerGroup> groups, string name) =>
        groups.Where(g => !g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

    // Structure only: a CA file that moved since the group was saved must not make every group unreadable.
    // Each profile is fully validated by the login when the group is opened.
    public static void Validate(IReadOnlyList<ServerGroup> groups)
    {
        if (groups.Count > MaxGroups) throw new ArgumentException($"Local limit: {MaxGroups} server groups.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 80) throw new ArgumentException("Each server group needs a name of up to 80 characters.");
            if (!names.Add(group.Name.Trim())) throw new ArgumentException("Server group names must be unique: " + group.Name);
            if (group.Profiles == null || group.Profiles.Count is 0 or > MultiRun.MaxTargets) throw new ArgumentException($"Server group {group.Name}: 1 to {MultiRun.MaxTargets} servers.");
            foreach (var profile in group.Profiles)
                if (profile == null || string.IsNullOrWhiteSpace(profile.Server) || string.IsNullOrWhiteSpace(profile.User) || string.IsNullOrWhiteSpace(profile.Database) || profile.Port is < 1 or > 65535)
                    throw new ArgumentException($"Server group {group.Name} contains an incomplete profile.");
        }
    }
}
