using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// One server batch and how many times it runs: "go 5" after it runs it five times in a row, as isql does.
public sealed record SqlBatch(string Text, int Repeat = 1);

// Owns lexical boundaries. Server batches retain source verbatim; the demo
// additionally strips comments and splits constant SELECT statements.
public static class SqlScript
{
    // isql accepts any count after go; a local cap keeps a typo such as "go 50000" from flooding a server.
    public const int MaxRepeat = 1000;
    private static readonly Regex GoLine = new(@"^\s*GO(?:\s+(?<count>[0-9]+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    public static IReadOnlyList<string> Split(string source, bool demoStatements = false, CancellationToken cancellation = default) =>
        SplitBatches(source, demoStatements, cancellation).Select(batch => batch.Text).ToList();

    // Every batch with its "go N" count. "go" alone, and the text after the last go, run once.
    public static IReadOnlyList<SqlBatch> SplitBatches(string source, bool demoStatements = false, CancellationToken cancellation = default)
    {
        var result = new List<SqlBatch>(); var text = new StringBuilder();
        // Demo statements split on ';' too; a "go N" applies to every statement since the previous go.
        int sinceGo = 0;
        char quote = '\0'; int commentDepth = 0;
        void Flush() { string value = text.ToString(); if (!string.IsNullOrWhiteSpace(value)) result.Add(new SqlBatch(demoStatements ? value.Trim() : value)); text.Clear(); }
        for (int start = 0; start < source.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            int end = start;
            while (end < source.Length && source[end] is not '\r' and not '\n') end++;
            int nextLine = end;
            if (nextLine < source.Length && source[nextLine] == '\r') nextLine++;
            if (nextLine < source.Length && source[nextLine] == '\n') nextLine++;
            string line = source[start..end];
            if (quote == '\0' && commentDepth == 0 && GoLine.Match(line) is { Success: true } go)
            {
                Flush();
                if (go.Groups["count"].Success)
                {
                    int repeat = ParseRepeat(go.Groups["count"].Value, source, start);
                    for (int i = sinceGo; i < result.Count; i++) result[i] = result[i] with { Repeat = repeat };
                }
                sinceGo = result.Count; start = nextLine; continue;
            }
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i], next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (commentDepth > 0)
                {
                    if (ch == '/' && next == '*') { commentDepth++; if (!demoStatements) text.Append("/*"); i++; }
                    else if (ch == '*' && next == '/') { commentDepth--; if (!demoStatements) text.Append("*/"); else text.Append(' '); i++; }
                    else if (!demoStatements) text.Append(ch);
                    continue;
                }
                if (quote != '\0')
                {
                    text.Append(ch);
                    if (ch == quote) { if (next == quote) { text.Append(next); i++; } else quote = '\0'; }
                    continue;
                }
                if (ch == '-' && next == '-') { if (!demoStatements) text.Append(line.AsSpan(i)); break; }
                if (ch == '/' && next == '*') { commentDepth++; text.Append(demoStatements ? " " : "/*"); i++; continue; }
                if (ch is '\'' or '"' or '[') quote = ch == '[' ? ']' : ch;
                if (demoStatements && ch == ';') Flush(); else text.Append(ch);
            }
            text.Append(source.AsSpan(end, nextLine - end)); start = nextLine;
        }
        if (quote != '\0') throw new FormatException("SQL: unterminated string or identifier.");
        if (commentDepth != 0) throw new FormatException("SQL: unterminated comment.");
        Flush(); return result;
    }

    private static int ParseRepeat(string digits, string source, int lineStart)
    {
        int lineNumber = 1 + source.AsSpan(0, lineStart).Count('\n');
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int repeat) || repeat is < 1 or > MaxRepeat)
            throw new FormatException($"SQL line {lineNumber}: go {digits} · the count after GO must be 1 to {MaxRepeat}.");
        return repeat;
    }

    // Times the batches run in total, counting every "go N" repetition.
    public static int Executions(IReadOnlyList<SqlBatch> batches) => batches.Sum(batch => batch.Repeat);
}
