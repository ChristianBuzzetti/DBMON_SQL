using System.Data;
using System.Globalization;

namespace AseIsql;

// Live ResultReader tables use column_N names and keep server headings in Caption.
// Those headings vary in whitespace, punctuation and case across releases.
internal static class MetadataRows
{
    internal static string Key(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static DataColumn? Find(DataTable table, params string[] names) => table.Columns.Cast<DataColumn>()
        .FirstOrDefault(c => names.Any(n => Key(n) == Key(c.Caption)));
    internal static DataColumn Require(DataTable table, params string[] names) => Find(table, names) ??
        throw new FormatException("Missing metadata column: " + string.Join(" / ", names));
    internal static string Text(DataRow row, string name, bool required = true)
    {
        var column = required ? Require(row.Table, name) : Find(row.Table, name);
        string value = column == null || row[column] is DBNull ? "" : Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? "";
        if (required && string.IsNullOrWhiteSpace(value)) throw new FormatException("Missing metadata value: " + name);
        return value;
    }
    internal static int Int(DataRow row, string name) => Convert.ToInt32(Text(row, name), CultureInfo.InvariantCulture);
    internal static bool Bit(DataRow row, string name) => Text(row, name).Trim().ToLowerInvariant() switch {
        "1" or "true" => true, "0" or "false" => false, _ => throw new FormatException("Invalid metadata flag: " + name) };
    internal static decimal? Number(object value)
    {
        if (value is DBNull) return null;
        // Accept invariant numeric ODBC values and strings, never culture-dependent thousands separators.
        string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().TrimEnd('%').Trim() ?? "";
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
    internal static decimal? Percent(object used, object total)
    {
        decimal? u = Number(used), t = Number(total);
        return u is >= 0 && t is > 0 && u <= t ? Math.Round(u.Value / t.Value * 100m, 2) : null;
    }
    internal static void AddPercent(DataTable table, string name, Func<DataRow, decimal?> value)
    {
        if (table.Columns.Contains(name)) throw new FormatException("Duplicate derived metric: " + name);
        table.Columns.Add(name, typeof(decimal));
        foreach (DataRow row in table.Rows) row[name] = value(row) is { } number ? number : DBNull.Value;
    }
}
