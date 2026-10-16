using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// Words that make a statement change data, objects, sessions or the server. Used to ask for an explicit
// acknowledgement before SQL is repeated on a timer; it is a warning aid, not a parser or a permission check.
// Comments, quoted strings and [bracketed] names are blanked first, so 'delete' inside a literal does not count.
public static class SqlRisk
{
    // Temporary tables (#name) are session scratch space: creating, filling or dropping them does not count.
    private static readonly Regex StateWords = new(
        @"\b(?:insert(?!\s+(?:into\s+)?#)|update|delete|merge|truncate|drop(?!\s+table\s+#)|alter|create(?!\s+table\s+#)|kill|dump|load|shutdown|grant|revoke|reorg|setuser|suspend|resume|reconfigure|writetext)\b|\bsp_configure\s+,|\bselect\b[^;]*?\binto\s+(?!#)\w",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The distinct state-changing words found in code, lower-cased, in order of appearance.
    public static IReadOnlyList<string> StateChanges(string sql) =>
        StateWords.Matches(CodeOnly(sql)).Select(m => m.Value.StartsWith("select", StringComparison.OrdinalIgnoreCase) ? "select into" :
            m.Value.StartsWith("sp_configure", StringComparison.OrdinalIgnoreCase) ? "sp_configure" : m.Value.ToLowerInvariant()).Distinct().ToList();

    public static string CodeOnly(string sql)
    {
        var text = new StringBuilder(sql.Length);
        char quote = '\0'; int depth = 0; bool line = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char ch = sql[i], next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (line) { if (ch is '\r' or '\n') { line = false; text.Append(ch); } else text.Append(' '); continue; }
            if (depth > 0)
            {
                if (ch == '/' && next == '*') { depth++; text.Append("  "); i++; }
                else if (ch == '*' && next == '/') { depth--; text.Append("  "); i++; }
                else text.Append(ch is '\r' or '\n' ? ch : ' ');
                continue;
            }
            if (quote != '\0')
            {
                if (ch == quote) { if (next == quote) { text.Append("  "); i++; continue; } quote = '\0'; }
                text.Append(' '); continue;
            }
            if (ch == '-' && next == '-') { line = true; text.Append("  "); i++; continue; }
            if (ch == '/' && next == '*') { depth = 1; text.Append("  "); i++; continue; }
            if (ch is '\'' or '"') { quote = ch; text.Append(' '); continue; }
            if (ch == '[') { quote = ']'; text.Append(' '); continue; }
            text.Append(ch);
        }
        return text.ToString();
    }
}
