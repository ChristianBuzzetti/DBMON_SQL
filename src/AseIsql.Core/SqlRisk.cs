using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// One state-changing word found in code: its kind ("delete", "dbcc reindex", "sp_addlogin"), the 1-based line it is
// on and that line's text as written, for a confirmation that shows what will run.
public sealed record SqlRiskFinding(string Kind, int Line, string Statement);

// The findings of a script and, for each state-changing batch that "go N" repeats, a line saying how many times.
public sealed record SqlRiskReview(IReadOnlyList<SqlRiskFinding> Findings, IReadOnlyList<string> Repetitions)
{
    public bool ChangesState => Findings.Count > 0;
}

// Words that make a statement change data, objects, sessions or the server. Used to ask for an explicit
// acknowledgement before SQL is repeated on a timer or run on a PROD session; it is a warning aid, not a parser or a
// permission check. Comments, quoted strings and [bracketed] names are blanked first, so 'delete' inside a literal
// does not count.
//
// What counts, beyond DML and DDL: sp_add*/sp_drop*, sp_dboption, sp_rename, sp_modifylogin, sp_password and
// sp_configure with a value; every dbcc except the read-only sqltext, help, stacktrace, showplan, memusage and
// traceflags (and traceon/traceoff of the output flags 3604/3605); checkpoint, online database, disk
// init/resize/mirror/remirror/unmirror/refit/reinit, mount/unmount database, quiesce, kill, dump, load, shutdown,
// truncate, reorg, grant, revoke, setuser, reconfigure and writetext. update statistics counts too: it rewrites the
// optimizer's statistics (plans can change) and reads whole tables or indexes under locks. Replication Server:
// sysadmin, ignore loss, rebuild queues, allow connections, suspend, resume, and create/alter/drop connection.
// Also: sp_bind*/sp_unbind*, login/role/ownership, replication, segment/threshold and object-setting procedures,
// sp_cacheconfig/sp_poolconfig with a value, lock table, sp_sysmon without noclear (it clears the counters), and RS
// switch active, configure replication server and admin quiesce_force_rsi.
public static class SqlRisk
{
    private const string ReadOnlyDbcc = @"(?:sqltext|help|stacktrace|showplan|memusage|traceflags)\b|trace(?:on|off)\s*\(\s*360[45]\s*\)";
    // Temporary tables (#name) are session scratch space: creating, filling or dropping them does not count.
    // Order matters where one word starts another: "update statistics" and "drop connection" are named before
    // "update" and "drop" so the confirmation says which one it saw.
    private static readonly Regex StateWords = new(
        @"\b(?:update\s+(?:all\s+|index\s+|table\s+)?statistics|(?:create|alter|drop)\s+connection|insert(?!\s+(?:into\s+)?#)|update|delete|merge|truncate|drop(?!\s+table\s+#)|alter|create(?!\s+table\s+#)" +
        @"|kill|dump|load|shutdown|grant|revoke|reorg|setuser|suspend|resume|reconfigure|writetext|checkpoint|quiesce|sysadmin" +
        @"|online\s+database|disk\s+(?:init|resize|mirror|remirror|unmirror|refit|reinit)|(?:un)?mount\s+database|ignore\s+loss|rebuild\s+queues|allow\s+connections" +
        @"|lock\s+table|switch\s+active|configure\s+replication\s+server|admin\s+quiesce_force_rsi" +
        @"|sp_(?:add|drop|bind|unbind)\w*|sp_(?:dboption|rename|modifylogin|password|locklogin|role|changedbowner|changegroup" +
        @"|setreptable|setrepproc|setrepdbmode|reptostandby|config_rep_agent|start_rep_agent|stop_rep_agent" +
        @"|extendsegment|placeobject|logdevice|modifythreshold|chgattribute|recompile|hidetext|tempdb|audit|logintrigger)" +
        @"|sp_sysmon|dbcc\s+(?!" + ReadOnlyDbcc + @")\w+)\b" +
        @"|\bsp_(?:configure|cacheconfig|poolconfig)\b[^\r\n;]*,|\bselect\b[^;]*?\binto\s+(?!#)\w",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex NoClear = new(@"\bnoclear\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LineComments = new(@"--.*|/\*(?:.*?\*/|.*)", RegexOptions.CultureInvariant);

    // The distinct state-changing words found in code, lower-cased, in order of appearance.
    public static IReadOnlyList<string> StateChanges(string sql) => Findings(sql).Select(f => f.Kind).Distinct().ToList();

    // Every state-changing word with its line. CodeOnly keeps every offset and line break of the source, so a match
    // in the blanked text points at the same place in the original.
    public static IReadOnlyList<SqlRiskFinding> Findings(string sql)
    {
        var findings = new List<SqlRiskFinding>();
        foreach (Match match in StateWords.Matches(CodeOnly(sql)))
        {
            int lineStart = match.Index == 0 ? 0 : sql.LastIndexOfAny(['\r', '\n'], match.Index - 1) + 1;
            int lineEnd = sql.IndexOfAny(['\r', '\n'], match.Index); if (lineEnd < 0) lineEnd = sql.Length;
            int line = 1 + sql.AsSpan(0, match.Index).Count('\n');
            string statement = sql[lineStart..lineEnd].Trim(), kind = Kind(match.Value);
            // sp_sysmon clears the monitor counters unless noclear is given (usually as a quoted argument).
            if (kind == "sp_sysmon" && NoClear.IsMatch(LineComments.Replace(sql[match.Index..lineEnd], " "))) continue;
            findings.Add(new SqlRiskFinding(kind, line, statement.Length > 160 ? statement[..160] + "…" : statement));
        }
        return findings;
    }

    // What a PROD confirmation shows: every state-changing statement, and each such batch that "go N" repeats.
    public static SqlRiskReview Review(string sql)
    {
        var findings = Findings(sql);
        if (findings.Count == 0) return new SqlRiskReview(findings, []);
        IReadOnlyList<SqlBatch> batches;
        try { batches = SqlScript.SplitBatches(sql); }
        catch (FormatException) { batches = []; }
        var repeated = new List<string>();
        for (int i = 0; i < batches.Count; i++)
            if (batches[i].Repeat > 1 && StateChanges(batches[i].Text) is { Count: > 0 } kinds)
                repeated.Add($"Batch {i + 1} runs {batches[i].Repeat} times (go {batches[i].Repeat}): {string.Join(", ", kinds)}");
        return new SqlRiskReview(findings, repeated);
    }

    private static string Kind(string word)
    {
        if (word.StartsWith("select", StringComparison.OrdinalIgnoreCase)) return "select into";
        if (word.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)) return word.Split((char[]?)null, 2)[0].TrimEnd(',').ToLowerInvariant();
        return Spaces.Replace(word, " ").ToLowerInvariant();
    }

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
                // Line breaks inside a literal stay, so line numbers keep matching the source.
                text.Append(ch is '\r' or '\n' ? ch : ' '); continue;
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
