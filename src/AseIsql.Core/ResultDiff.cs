using System.Data;
using System.Globalization;

namespace AseIsql;

public sealed record DiffResult(DataTable Table, int Keys, int DifferingKeys, string KeyDescription);

// Compares result k across servers by key. A key differs when it is missing on some server or when the servers
// return different rows for it; only the rows of differing keys are kept, so identical data yields an empty table.
public static class ResultDiff
{
    public const string ServerCaption = "server";
    public const string DifferenceCaption = "difference";

    public static DiffResult Compare(IReadOnlyList<(string Server, DataTable Table)> parts, IReadOnlyList<int> keyColumns)
    {
        if (parts.Count < 2) throw new ArgumentException("A comparison needs results from at least two servers.");
        var first = parts[0].Table;
        if (parts.Any(p => p.Table.Columns.Count != first.Columns.Count || Enumerable.Range(0, first.Columns.Count).Any(c =>
            p.Table.Columns[c].DataType != first.Columns[c].DataType || !string.Equals(p.Table.Columns[c].Caption, first.Columns[c].Caption, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("The servers returned different columns or types; compare their tabs instead.");
        if (keyColumns.Count == 0 || keyColumns.Distinct().Count() != keyColumns.Count || keyColumns.Any(k => k < 0 || k >= first.Columns.Count))
            throw new ArgumentException("Choose one or more distinct key columns.");
        if (parts.Sum(p => (long)p.Table.Rows.Count) > MultiRun.MaxCombinedRows) throw new ArgumentException($"The comparison stops at {Counts.Of(MultiRun.MaxCombinedRows, "row")} in total.");
        var valueColumns = Enumerable.Range(0, first.Columns.Count).Where(c => !keyColumns.Contains(c)).ToArray();

        // Key → server → rows, in first-seen key order so the output follows the data the servers returned.
        var order = new List<object[]>();
        var byKey = new Dictionary<object[], Dictionary<string, List<DataRow>>>(ValuesComparer.Instance);
        foreach (var (server, table) in parts)
            foreach (DataRow row in table.Rows)
            {
                var key = keyColumns.Select(c => row[c]).ToArray();
                if (!byKey.TryGetValue(key, out var servers)) { servers = []; byKey[key] = servers; order.Add(key); }
                if (!servers.TryGetValue(server, out var rows)) { rows = []; servers[server] = rows; }
                rows.Add(row);
            }

        var result = new DataTable();
        Add(result, "column_server", ServerCaption, typeof(string));
        Add(result, "column_difference", DifferenceCaption, typeof(string));
        foreach (DataColumn column in first.Columns) Add(result, column.ColumnName, column.Caption, column.DataType, column.ExtendedProperties["SourceName"]);
        int differing = 0;
        result.BeginLoadData();
        foreach (var key in order)
        {
            var servers = byKey[key];
            string? difference = Describe(parts.Select(p => p.Server).ToArray(), servers, valueColumns, first);
            if (difference == null) continue;
            differing++;
            foreach (var (server, _) in parts)
                if (servers.TryGetValue(server, out var rows))
                    foreach (var row in rows) result.Rows.Add([server, difference, .. row.ItemArray]);
        }
        result.EndLoadData();
        string keyDescription = string.Join(", ", keyColumns.Select(c => Caption(first.Columns[c])));
        return new DiffResult(result, order.Count, differing, keyDescription);
    }

    private static string? Describe(string[] allServers, Dictionary<string, List<DataRow>> servers, int[] valueColumns, DataTable shape)
    {
        var missing = allServers.Where(s => !servers.ContainsKey(s)).ToArray();
        if (missing.Length > 0) return "missing on " + string.Join(", ", missing);
        var present = allServers.Select(s => servers[s]).ToArray();
        if (present.Any(rows => rows.Count != present[0].Count)) return "row count differs";
        if (present[0].Count == 1)
        {
            var changed = valueColumns.Where(c => present.Any(rows => !ValuesComparer.Same(rows[0][c], present[0][0][c]))).Select(c => Caption(shape.Columns[c])).ToArray();
            return changed.Length == 0 ? null : "values differ: " + string.Join(", ", changed);
        }
        // Duplicate keys compare as multisets of their value tuples.
        var canonical = present.Select(rows => rows.Select(r => valueColumns.Select(c => r[c]).ToArray()).OrderBy(Sortable, StringComparer.Ordinal).ToArray()).ToArray();
        bool same = canonical.All(rows => rows.Zip(canonical[0]).All(pair => ValuesComparer.Instance.Equals(pair.First, pair.Second)));
        return same ? null : "rows differ";
    }

    private static string Caption(DataColumn column) => column.Caption.Length == 0 ? "(no column name)" : column.Caption;
    private static string Sortable(object[] values) => string.Join("\u001f", values.Select(v => v switch { DBNull => "\u0000", byte[] b => Convert.ToHexString(b), _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" }));

    private static void Add(DataTable table, string name, string caption, Type type, object? sourceName = null)
    {
        var column = table.Columns.Add(name, type); column.Caption = caption; column.ExtendedProperties["SourceName"] = sourceName ?? caption;
    }

    // Typed equality: DBNull matches DBNull, binary compares bytes, everything else uses the value's own Equals.
    private sealed class ValuesComparer : IEqualityComparer<object[]>
    {
        public static readonly ValuesComparer Instance = new();
        public static bool Same(object a, object b) => a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);
        public bool Equals(object[]? a, object[]? b) => a != null && b != null && a.Length == b.Length && a.Zip(b).All(pair => Same(pair.First, pair.Second));
        public int GetHashCode(object[] values)
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(value is byte[] bytes ? Convert.ToHexString(bytes).GetHashCode(StringComparison.Ordinal) : value.GetHashCode());
            return hash.ToHashCode();
        }
    }
}
