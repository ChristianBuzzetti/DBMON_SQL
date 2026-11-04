using System.Data;

namespace AseIsql;

public static class IqMetrics
{
    public static IReadOnlyList<(string Label, string Sql)> Commands { get; } = Array.AsReadOnly(new[] {
        ("Status", "sp_iqstatus"), ("Dbspaces", "sp_iqdbspace"),
        ("Connections", "sp_iqconnection"), ("Transactions", "sp_iqtransaction") });

    public static DataTable Status(DataTable source)
    {
        var key = MetadataRows.Find(source, "Name", "Description", "Key");
        var value = MetadataRows.Find(source, "Value");
        // Some releases return two unnamed columns. Only that exact shape has a positional interpretation.
        if (key == null || value == null) {
            if (source.Columns.Count != 2) throw new FormatException("sp_iqstatus must return key/value columns.");
            key = source.Columns[0]; value = source.Columns[1];
        }
        var result = new DataTable("IQ status"); result.Columns.Add("Key"); result.Columns.Add("Value");
        foreach (DataRow row in source.Rows) result.Rows.Add(row[key], row[value]);
        return result;
    }
    public static DataTable Dbspaces(DataTable source)
    {
        var result = source.Copy();
        var usage = MetadataRows.Find(result, "Usage", "Usage Percent", "Percent Used");
        if (usage != null && usage.ColumnName == "Usage %") { usage.ColumnName = "Usage (server)"; usage.Caption = "Usage (server)"; }
        if (usage != null) MetadataRows.AddPercent(result, "Usage %", row => {
            var number = MetadataRows.Number(row[usage]); return number is >= 0 and <= 100 ? number : null;
        });
        else {
            var used = MetadataRows.Require(result, "Used Blocks", "UsedBlocks");
            var total = MetadataRows.Require(result, "Total Blocks", "TotalBlocks");
            MetadataRows.AddPercent(result, "Usage %", row => MetadataRows.Percent(row[used], row[total]));
        }
        return result;
    }
    public static DataTable Connections(DataTable source)
    {
        MetadataRows.Require(source, "ConnHandle", "Connection Handle", "Number");
        return source.Copy();
    }
    public static DataTable TransactionSummary(DataTable source)
    {
        var state = MetadataRows.Require(source, "State");
        var workspace = MetadataRows.Find(source, "TempWorkSpaceKB");
        var result = new DataTable("Transaction summary");
        result.Columns.Add("State"); result.Columns.Add("Transactions", typeof(int)); result.Columns.Add("Temp workspace KB", typeof(decimal));
        foreach (var group in source.Rows.Cast<DataRow>().GroupBy(row => row[state] is DBNull ? "(unknown)" :
            Convert.ToString(row[state])?.Trim().ToUpperInvariant() ?? "(unknown)")) {
            decimal? sum = workspace == null ? null : 0m;
            foreach (var row in group) {
                var number = workspace == null ? null : MetadataRows.Number(row[workspace]);
                if (number == null || number < 0) { sum = null; break; }
                sum += number;
            }
            result.Rows.Add(group.Key, group.Count(), sum is { } value ? value : DBNull.Value);
        }
        return result;
    }
}
