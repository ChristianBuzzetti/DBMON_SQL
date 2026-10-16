using System.Globalization;
using System.Text;

namespace AseIsql;

// Named placeholders (:name) in recipes and scripts. A placeholder is a colon followed by a letter or underscore and
// word characters, and not preceded by a word character or another colon: times ('00:00:10'), text such as
// 'Note:x' and scopes (a::b) are not placeholders. Comments and [bracketed] identifiers are ignored.
// A value inside a quoted string is escaped for that quote. Outside quotes only one identifier, qualified name or
// number is accepted, so a value can never add statements or change the shape of the SQL around it.
public static class SqlParameters
{
    public sealed record Placeholder(string Name, int Offset, int Length, char Quote);
    public const int MaxValueLength = 4000;

    public static IReadOnlyList<Placeholder> Scan(string sql)
    {
        var found = new List<Placeholder>();
        char quote = '\0'; int commentDepth = 0; bool lineComment = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char ch = sql[i], next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (lineComment) { if (ch is '\r' or '\n') lineComment = false; continue; }
            if (commentDepth > 0)
            {
                if (ch == '/' && next == '*') { commentDepth++; i++; }
                else if (ch == '*' && next == '/') { commentDepth--; i++; }
                continue;
            }
            if (quote == ']') { if (ch == ']') { if (next == ']') i++; else quote = '\0'; } continue; }
            if (quote != '\0' && ch == quote) { if (next == quote) i++; else quote = '\0'; continue; }
            if (quote == '\0')
            {
                if (ch == '-' && next == '-') { lineComment = true; i++; continue; }
                if (ch == '/' && next == '*') { commentDepth++; i++; continue; }
                if (ch is '\'' or '"') { quote = ch; continue; }
                if (ch == '[') { quote = ']'; continue; }
            }
            if (ch != ':' || !(char.IsLetter(next) || next == '_')) continue;
            if (i > 0 && (IsWord(sql[i - 1]) || sql[i - 1] == ':')) continue;
            int end = i + 1;
            while (end < sql.Length && IsWord(sql[end])) end++;
            found.Add(new Placeholder(sql[(i + 1)..end], i, end - i, quote));
            i = end - 1;
        }
        return found;
    }

    // Distinct names in order of first appearance; names compare case-insensitively.
    public static IReadOnlyList<string> Names(string sql) => Scan(sql).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Null when the value is acceptable where every occurrence of the placeholder sits; otherwise the reason.
    public static string? Problem(string sql, string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return ":" + name + " needs a value.";
        if (value.Length > MaxValueLength) return ":" + name + " exceeds " + MaxValueLength.ToString("N0", CultureInfo.InvariantCulture) + " characters.";
        foreach (var placeholder in Scan(sql).Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (placeholder.Quote != '\0') { if (value.Contains('\r') || value.Contains('\n')) return ":" + name + " is inside a quoted string and cannot span lines."; continue; }
            if (!IsBareToken(value)) return ":" + name + " is used outside quotes: enter one name, qualified name (db..table, [my table]) or number.";
        }
        return null;
    }

    public static string Apply(string sql, IReadOnlyDictionary<string, string> values)
    {
        var lookup = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        var placeholders = Scan(sql);
        foreach (string name in placeholders.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            lookup.TryGetValue(name, out string? value);
            if (Problem(sql, name, value) is { } problem) throw new ArgumentException(problem);
        }
        var text = new StringBuilder(sql);
        foreach (var placeholder in placeholders.OrderByDescending(p => p.Offset))
        {
            string value = lookup[placeholder.Name];
            if (placeholder.Quote is '\'' or '"') value = value.Replace(placeholder.Quote.ToString(), new string(placeholder.Quote, 2), StringComparison.Ordinal);
            text.Remove(placeholder.Offset, placeholder.Length).Insert(placeholder.Offset, value);
        }
        return text.ToString();
    }

    private static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    // One token: letters, digits, _ . # @ $ and a leading sign, plus [bracketed parts] that may hold spaces.
    private static bool IsBareToken(string value)
    {
        if (value.Trim().Length != value.Length) return false;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '[')
            {
                int close = i + 1;
                while (close < value.Length && !(value[close] == ']' && (close + 1 >= value.Length || value[close + 1] != ']'))) close += value[close] == ']' ? 2 : 1;
                if (close >= value.Length || close == i + 1 || value[(i + 1)..close].Any(c => c is '\r' or '\n')) return false;
                i = close; continue;
            }
            if (!(IsWord(ch) || ch is '.' or '#' or '@' or '$' || ch is '-' or '+' && i == 0)) return false;
        }
        return true;
    }
}
