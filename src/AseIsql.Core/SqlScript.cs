using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// Owns lexical boundaries. Server batches retain source verbatim; the demo
// additionally strips comments and splits constant SELECT statements.
public static class SqlScript
{
    public static IReadOnlyList<string> Split(string source, bool demoStatements = false, CancellationToken cancellation = default)
    {
        var result = new List<string>(); var text = new StringBuilder();
        char quote = '\0'; int commentDepth = 0;
        void Flush() { string value = text.ToString(); if (!string.IsNullOrWhiteSpace(value)) result.Add(demoStatements ? value.Trim() : value); text.Clear(); }
        for (int start = 0; start < source.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            int end = start;
            while (end < source.Length && source[end] is not '\r' and not '\n') end++;
            int nextLine = end;
            if (nextLine < source.Length && source[nextLine] == '\r') nextLine++;
            if (nextLine < source.Length && source[nextLine] == '\n') nextLine++;
            string line = source[start..end];
            if (quote == '\0' && commentDepth == 0 && Regex.IsMatch(line, @"^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking))
            { Flush(); start = nextLine; continue; }
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
}
