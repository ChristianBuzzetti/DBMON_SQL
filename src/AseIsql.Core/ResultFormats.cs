using System.Data;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AseIsql;

public static class ResultFormats
{
    private static string Display(object? value) => value switch { null or DBNull => "", byte[] bytes => "0x"+Convert.ToHexString(bytes), DateTime date => date.ToString("O",CultureInfo.InvariantCulture), bool bit => bit ? "1" : "0", _=>Convert.ToString(value,CultureInfo.InvariantCulture)??"" };
    // What a result cell shows on screen. Dates keep every digit the value carries: ASE datetime holds 1/300 of a
    // second and bigdatetime microseconds, and a cell that hides them makes two different rows look identical.
    public static string Cell(object? value) => value switch {
        null or DBNull => "", byte[] bytes => "0x" + Convert.ToHexString(bytes),
        // ASE bit columns hold 1 and 0; True/False is .NET's spelling, not the server's.
        bool bit => bit ? "1" : "0",
        DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Moment(date, " "),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
    // Seconds and the fraction are built separately: trimming the trailing zeros of the whole text would eat the
    // zero of a second such as 13:46:30.
    private static string Moment(DateTime date, string separator)
    {
        string text = date.ToString("yyyy-MM-dd" + separator + "HH:mm:ss", CultureInfo.InvariantCulture);
        long fraction = date.Ticks % TimeSpan.TicksPerSecond;
        return fraction == 0 ? text : text + "." + fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
    }
    private static readonly char[] TextEnd = ['\r', '\n'];
    // Several result sets as one block of text, each under its own label. Bounded: a run can hold far more than a
    // clipboard takes, so the caller is told how many sets fit rather than handing over a silently cut copy.
    public static (string Text, int Sets, bool Complete) Sets(IEnumerable<(string Label, string Text)> sets, int maxChars)
    {
        var output = new StringBuilder(); int count = 0; bool complete = true;
        foreach (var (label, text) in sets)
        {
            string block = (output.Length == 0 ? "" : Environment.NewLine) + "-- " + label + Environment.NewLine + text.TrimEnd(TextEnd) + Environment.NewLine;
            if (output.Length + block.Length > maxChars) { complete = false; break; }
            output.Append(block); count++;
        }
        return (output.ToString(), count, complete);
    }
    public static string Identifier(string name) => "[" + name.Replace("]", "]]") + "]";
    public static string Literal(object? value) => value switch {
        null or DBNull => "NULL", bool b => b ? "1" : "0", byte[] bytes => "0x" + Convert.ToHexString(bytes),
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        float f when float.IsFinite(f) => f.ToString("R", CultureInfo.InvariantCulture),
        double d when double.IsFinite(d) => d.ToString("R", CultureInfo.InvariantCulture),
        float or double => throw new ArgumentException("SQL literals cannot represent non-finite numbers."),
        DateTime date => "'" + Moment(date, "T") + "'",
        _ => "'" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("'", "''") + "'"
    };
    public static string Json(DataTable table) => JsonSerializer.Serialize(new {
        columns = table.Columns.Cast<DataColumn>().Select(c => new { name = c.Caption, type = c.DataType.Name }),
        rows = table.Rows.Cast<DataRow>().Select(r => r.ItemArray.Select(v => v is DBNull ? null : v is byte[] bytes ? (object)("0x" + Convert.ToHexString(bytes)) : v))
    }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    public static string Markdown(DataTable table)
    {
        static string Cell(object? value) => Display(value).Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
        var text = new StringBuilder();
        text.AppendLine("| " + string.Join(" | ", table.Columns.Cast<DataColumn>().Select(c => Cell(c.Caption))) + " |");
        text.AppendLine("| " + string.Join(" | ", table.Columns.Cast<DataColumn>().Select(_ => "---")) + " |");
        foreach (DataRow row in table.Rows) text.AppendLine("| " + string.Join(" | ", row.ItemArray.Select(Cell)) + " |");
        return text.ToString();
    }
    public static string Html(DataTable table, string sql, string source)
    {
        static string E(object? value) => WebUtility.HtmlEncode(Display(value));
        var text = new StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><title>ASE ISQL · Result</title><style>body{font:14px Segoe UI,sans-serif;margin:32px;color:#202c3c}table{border-collapse:collapse}td,th{padding:6px 12px;border:1px solid #dee4eb;text-align:left;white-space:pre-wrap}th{background:#f1f4f8}pre{white-space:pre-wrap}</style><h1>Query result</h1><p>");
        text.Append(E(source)).Append("</p><pre>").Append(E(sql)).Append("</pre><table><thead><tr>");
        foreach (DataColumn column in table.Columns) text.Append("<th>").Append(E(column.Caption)).Append("</th>");
        text.Append("</tr></thead><tbody>");
        foreach (DataRow row in table.Rows) { text.Append("<tr>"); foreach (object? value in row.ItemArray) text.Append("<td>").Append(E(value)).Append("</td>"); text.Append("</tr>"); }
        return text.Append("</tbody></table></html>").ToString();
    }
    public static string InList(DataTable table) => "(" + string.Join(", ", table.Rows.Cast<DataRow>().Select(r => Literal(r[0]))) + ")";
    // Splits table, owner.table or database.owner.table without breaking [bracketed] or "quoted" parts that contain dots.
    public static string[] QualifiedName(string target)
    {
        var parts = new List<string>(); var part = new StringBuilder(); bool quoted = false; int i = 0;
        void Close() { string value = quoted ? part.ToString() : part.ToString().Trim(); if (value.Length == 0) throw new ArgumentException("Enter table, owner.table or database.owner.table."); parts.Add(value); part.Clear(); quoted = false; }
        while (i < target.Length)
        {
            char c = target[i];
            if (char.IsWhiteSpace(c) && (part.Length == 0 || quoted)) { i++; continue; }
            if (c is '[' or '"' && part.Length == 0 && !quoted)
            {
                char end = c == '[' ? ']' : '"'; i++;
                while (true)
                {
                    if (i >= target.Length) throw new ArgumentException("Unterminated quoted identifier in target table name.");
                    if (target[i] == end) { if (i + 1 < target.Length && target[i + 1] == end) { part.Append(end); i += 2; continue; } i++; break; }
                    part.Append(target[i++]);
                }
                quoted = true; continue;
            }
            if (c == '.') { Close(); i++; continue; }
            if (quoted || c is '[' or ']' or '"') throw new ArgumentException("Unexpected character '" + c + "' in target table name.");
            part.Append(c); i++;
        }
        Close();
        return parts.ToArray();
    }    public static string Inserts(DataTable table, string target)
    {
        string[] parts = QualifiedName(target);
        if (parts.Length is < 1 or > 3 || parts.Any(string.IsNullOrEmpty)) throw new ArgumentException("Enter table, owner.table or database.owner.table.");
        if(table.Columns.Cast<DataColumn>().Select(c=>c.Caption).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=table.Columns.Count)throw new ArgumentException("Duplicate headers require column mapping before generating INSERT statements.");
        string name = string.Join(".", parts.Select(Identifier)), columns = string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => Identifier(c.Caption)));
        return string.Join("\r\n", table.Rows.Cast<DataRow>().Select(r => $"INSERT INTO {name} ({columns}) VALUES ({string.Join(", ", r.ItemArray.Select(Literal))});"));
    }
    public static DataTable Transpose(DataTable source)
    {
        if (source.Rows.Count > 1000) throw new ArgumentException("Select up to 1,000 rows to transpose.");
        var result = new DataTable(); result.Columns.Add("Field", typeof(string));
        for (int i = 0; i < source.Rows.Count; i++) result.Columns.Add("Row " + (i + 1), typeof(object));
        foreach (DataColumn column in source.Columns) result.Rows.Add(new object[] { column.Caption }.Concat(source.Rows.Cast<DataRow>().Select(r => r[column])).ToArray());
        return result;
    }
}
