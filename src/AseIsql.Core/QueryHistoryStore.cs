using System.Text;
using System.Text.Json;

namespace AseIsql;

public sealed record QueryHistoryEntry(DateTimeOffset ExecutedAt, string Sql);

public sealed class QueryHistoryStore(string path)
{
    public const int Capacity = 100;
    private const int MaxBytes = 32 * 1024 * 1024;

    public IReadOnlyList<QueryHistoryEntry> Load()
    {
        // File.Exists also returns false for directories and access failures: do not mistake those for a new store.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) throw new IOException("Query history exceeds 32 MiB.");
            var entries = JsonSerializer.Deserialize<List<QueryHistoryEntry>>(stream)
                ?? throw new JsonException("Invalid query history.");
            return Ordered(entries);
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    public void Save(IEnumerable<QueryHistoryEntry> entries)
    {
        string json = JsonSerializer.Serialize(Ordered(entries), new JsonSerializerOptions { WriteIndented = true });
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new IOException("Query history exceeds 32 MiB.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFiles.SaveUtf8(path, json);
    }

    private static IReadOnlyList<QueryHistoryEntry> Ordered(IEnumerable<QueryHistoryEntry> entries)
    {
        var materialized = entries.ToList();
        if (materialized.Any(entry => entry is null || entry.ExecutedAt == default || string.IsNullOrWhiteSpace(entry.Sql)))
            throw new JsonException("Invalid query history entry.");
        return materialized.OrderByDescending(entry => entry.ExecutedAt).Take(Capacity).ToArray();
    }
}
