using System.Data;
using System.Globalization;
using System.Text;

namespace AseIsql;

public enum CompareObjectType { Tables, Procedures, Views, Triggers }
public enum ObjectDifference { OnlyLeft, OnlyRight, Different, Same, Unavailable }
public enum LineChange { Same, Removed, Added }
public sealed record DiffLine(LineChange Change, int? LeftLine, int? RightLine, string Text);
public sealed record ObjectKey(string Owner, string Name);
public sealed record ObjectSnapshot(ObjectKey Key, string Definition, string? Error = null);
public sealed record ComparedObject(ObjectKey Key, ObjectDifference Difference, ObjectSnapshot? Left, ObjectSnapshot? Right)
{
    public string Owner => Key.Owner;
    public string Name => Key.Name;
    public string Status => Difference switch { ObjectDifference.OnlyLeft => "Only in left", ObjectDifference.OnlyRight => "Only in right", _ => Difference.ToString() };
}

// ODBC result columns have generated names; source captions are the catalog contract.
internal static class CatalogData
{
    public static bool TryValue(DataRow row, string name, out object value)
    {
        var column = ResultColumns.Find(row.Table, name);
        value = column == null ? DBNull.Value : row[column]; return column != null;
    }
    public static object Value(DataRow row, string name) => TryValue(row, name, out var value) ? value : throw new FormatException("Missing catalog column: " + name);
    public static string Text(DataRow row, string name) => Value(row, name) is DBNull ? throw new FormatException("Catalog value unavailable: " + name) : Convert.ToString(Value(row, name), CultureInfo.InvariantCulture) ?? "";
}

public static class ObjectCompare
{
    public const int MaxObjects = 2000;
    public const int MaxDiffCells = 4_000_000;
    public const int MaxDefinitionCharacters = 4 * 1024 * 1024;
    public const int MaxSnapshotCharacters = 32 * 1024 * 1024;

    public static IReadOnlyList<ComparedObject> Compare(IReadOnlyList<ObjectSnapshot> left, IReadOnlyList<ObjectSnapshot> right, bool normalizeWhitespace = false)
    {
        if (left.Count > MaxObjects || right.Count > MaxObjects) throw new ArgumentException($"Compare at most {MaxObjects} objects per side.");
        // Owner and name are separate, case-sensitive keys: a case-sensitive server can contain both Foo and foo.
        var a = left.ToDictionary(o => o.Key); var b = right.ToDictionary(o => o.Key);
        return a.Keys.Union(b.Keys).OrderBy(k => k.Owner, StringComparer.Ordinal).ThenBy(k => k.Name, StringComparer.Ordinal).Select(key => {
            a.TryGetValue(key, out var l); b.TryGetValue(key, out var r);
            var difference = l == null ? ObjectDifference.OnlyRight : r == null ? ObjectDifference.OnlyLeft :
                l.Error != null || r.Error != null ? ObjectDifference.Unavailable :
                Canonical(l.Definition, normalizeWhitespace) == Canonical(r.Definition, normalizeWhitespace) ? ObjectDifference.Same : ObjectDifference.Different;
            return new ComparedObject(key, difference, l, r);
        }).ToArray();
    }

    private static string Canonical(string text, bool whitespace) => whitespace ? NormalizeWhitespace(text) : text.Replace("\r\n", "\n").Replace('\r', '\n');

    // Collapse whitespace outside quoted values, identifiers and comments. Preserve literal contents and the
    // newline terminating a -- comment: normalization must never make different SQL literals compare equal.
    public static string NormalizeWhitespace(string source)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var text = new StringBuilder(); bool pending = false;
        for (int i = 0; i < source.Length;)
        {
            char ch = source[i];
            if (char.IsWhiteSpace(ch)) { pending = text.Length > 0 && text[^1] != '\n'; i++; continue; }
            if (pending) { text.Append(' '); pending = false; }
            if (ch is '\'' or '"' or '[')
            {
                char close = ch == '[' ? ']' : ch; text.Append(ch); i++;
                while (i < source.Length)
                {
                    char next = source[i++]; text.Append(next);
                    if (next != close) continue;
                    if (i < source.Length && source[i] == close) { text.Append(source[i++]); continue; }
                    break;
                }
            }
            else if (ch == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                while (i < source.Length && source[i] != '\n') text.Append(source[i++]);
                if (i < source.Length) { text.Append('\n'); i++; }
            }
            else if (ch == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int depth = 0;
                do
                {
                    if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*') { depth++; text.Append("/*"); i += 2; }
                    else if (i + 1 < source.Length && source[i] == '*' && source[i + 1] == '/') { depth--; text.Append("*/"); i += 2; }
                    else text.Append(source[i++]);
                } while (i < source.Length && depth > 0);
            }
            else { text.Append(ch); i++; }
        }
        return text.ToString().TrimEnd(' ', '\t');
    }

    // LCS line diff, with common ends stripped before allocating its bounded working matrix. Line numbers refer
    // to the original text; large differences explicitly report the limit instead of freezing the UI.
    public static IReadOnlyList<DiffLine> LineDiff(string left, string right)
    {
        static string[] Lines(string s) => s.Length == 0 ? [] : s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var a = Lines(left); var b = Lines(right);
        bool Equal(int x, int y) => a[x] == b[y];
        int start = 0, end = 0;
        while (start < a.Length && start < b.Length && Equal(start, start)) start++;
        while (end < a.Length - start && end < b.Length - start && Equal(a.Length - end - 1, b.Length - end - 1)) end++;
        int n = a.Length - start - end, m = b.Length - start - end;
        if ((long)(n + 1) * (m + 1) > MaxDiffCells) throw new ArgumentException("Line diff exceeds 4 million working cells; inspect the definitions side by side.");
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--) lcs[i, j] = Equal(start + i, start + j) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var result = new List<DiffLine>();
        for (int i = 0; i < start; i++) result.Add(new(LineChange.Same, i + 1, i + 1, a[i]));
        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && Equal(start + x, start + y)) { result.Add(new(LineChange.Same, start + x + 1, start + y + 1, a[start + x])); x++; y++; }
            else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) { result.Add(new(LineChange.Removed, start + x + 1, null, a[start + x])); x++; }
            else { result.Add(new(LineChange.Added, null, start + y + 1, b[start + y])); y++; }
        }
        for (int i = end; i > 0; i--) result.Add(new(LineChange.Same, a.Length - i + 1, b.Length - i + 1, a[a.Length - i]));
        return result;
    }

    public static string UnifiedDiff(string left, string right) => string.Join("\n", LineDiff(left, right).Select(line =>
        $"{line.LeftLine?.ToString(CultureInfo.InvariantCulture) ?? "",5} {line.RightLine?.ToString(CultureInfo.InvariantCulture) ?? "",5} " + (line.Change == LineChange.Added ? "+ " : line.Change == LineChange.Removed ? "- " : "  ") + line.Text));

    // Stable schema text is both the comparison payload and the table's readable line diff. Index metadata must
    // contain logical definitions (ordered key names and properties), never object IDs or physical page counts.
    public static string TableDefinition(DataTable columns, DataTable indexes)
    {
        string[] fields = ["name", "type", "length", "nullable", "identity"];
        fields = fields.Concat(new[] { "precision", "scale" }.Where(f => ResultColumns.Find(columns, f) != null)).ToArray();
        var lines = new List<string> { "COLUMNS (catalog order)" };
        foreach (DataRow row in columns.Rows)
        {
            var values = fields.Select(f => CatalogData.Text(row, f)).ToArray();
            lines.Add(string.Join(" | ", fields.Zip(values, (field, value) => field + "=" + Escape(value))));
        }
        if (columns.Rows.Count == 0) throw new FormatException("Table columns are unavailable.");
        lines.Add("INDEXES");
        string[] indexFields = ["index_name", "index_description", "index_keys"];
        lines.AddRange(indexes.Rows.Cast<DataRow>().Select(row => string.Join(" | ", indexFields.Select(f => f + "=" + Escape(CatalogData.Text(row, f))))).OrderBy(s => s, StringComparer.Ordinal));
        return string.Join("\n", lines);
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("|", "\\|");
    public static bool ColumnsEqual(DataTable left, DataTable right)
    {
        var indexes = new DataTable(); indexes.Columns.Add("name");
        return TableDefinition(left, indexes) == TableDefinition(right, indexes);
    }
}
