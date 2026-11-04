using System.Data;
using System.Text.RegularExpressions;

namespace AseIsql;

public static class RsMonitor
{
    public static IReadOnlyList<(string Label, string Sql)> Commands { get; } = Array.AsReadOnly(new[] {
        ("Threads", "admin who"), ("Threads down", "admin who_is_down"), ("Health", "admin health"),
        ("Disk space", "admin disk_space"), ("Connections", "admin show_connections"), ("Queues (SQM)", "admin who, sqm") });

    public static bool NeedsAttention(string? state) => state != null &&
        (Regex.IsMatch(state, @"\bDOWN\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
         state.Contains("suspend", StringComparison.OrdinalIgnoreCase));

    public static DataTable Threads(DataTable source)
    {
        var table = source.Copy();
        var state = MetadataRows.Require(table, "State", "Thread State");
        const string warning = "Needs attention";
        if (ResultColumns.Find(table, warning) != null) throw new FormatException("Duplicate thread warning column.");
        var column = table.Columns.Add(warning, typeof(bool));
        foreach (DataRow row in table.Rows) row[column] = NeedsAttention(row[state] is DBNull ? null : Convert.ToString(row[state]));
        return table;
    }
    public static DataTable DiskSpace(DataTable source)
    {
        var table = source.Copy();
        var total = MetadataRows.Require(table, "Total Segs", "Total Segments");
        var used = MetadataRows.Require(table, "Used Segs", "Used Segments");
        MetadataRows.AddPercent(table, "Usage %", row => MetadataRows.Percent(row[used], row[total]));
        return table;
    }
    // RCL identifiers are deliberately restricted here: never concatenate arbitrary prompt/driver text into a command.
    public static string ConnectionCommand(bool suspend, string server, string database)
    {
        static string Name(string value) => Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_$#@]*\z", RegexOptions.CultureInvariant)
            ? value : throw new ArgumentException("Use a simple RCL server/database identifier (letters, digits, _, $, # or @; no spaces).");
        return (suspend ? "suspend" : "resume") + " connection to " + Name(server) + "." + Name(database);
    }
}
