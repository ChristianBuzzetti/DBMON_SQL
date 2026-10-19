namespace AseIsql;

// compatibility_mode starts at whatever the server configured for the login, so it is tri-state: ServerDefault means
// the app has not touched it and cannot claim to know its value. ASE has no statement that restores that state, so
// once the user chooses, the only moves are On and Off.
public enum CompatibilityMode { ServerDefault, On, Off }

// The SAP ASE SET options DBMON SQL drives from Tools. They are plain SET statements sent on the open session: they
// live in that connection only, disappear when it closes, and never reach the multi-server or repeat sessions, which
// open their own. The app tracks what it sent, never what the server was configured with.
//
// Statistics IO is two options at once. "set statistics io" still runs because its "Total writes for this command"
// record (3614) arrives whole through ODBC, but its substance, the per-table "Table: <name> scan count N, logical
// reads: ..." record (3615), reaches System.Data.Odbc with no body: verified against a real table on the user's ASE,
// and the predecessor AseIsql only shows it because it reads server messages through a CT-Library callback. The
// per-table counters therefore come from "set plan for show_execio_xml to message", which keeps the executed plan
// with its actual logical and physical reads in a session buffer that showplan_in_xml(0) returns as an ordinary
// result: same session, same run, no diagnostics channel involved. Verified on ASE 16.0 SP04: the actual logical
// reads it reports equal those of statistics io in isql. It carries no scan count and no regular/apf split.
public sealed record AseSessionOptions(bool NoExec = false, bool StatisticsTime = false, bool StatisticsIo = false, CompatibilityMode Compatibility = CompatibilityMode.ServerDefault)
{
    public static readonly AseSessionOptions Default = new();
    // Compatibility stays out of this: Default's ServerDefault is "untouched", which no SET can restore.
    public bool IsDefault => this == Default;

    // Short tags for the status bar, in the order they are shown. NOEXEC leads because it is the one that silently
    // changes what every later run does.
    public IReadOnlyList<string> Tags
    {
        get
        {
            var tags = new List<string>();
            if (NoExec) tags.Add("NOEXEC");
            if (StatisticsIo) tags.Add("STATS IO");
            if (StatisticsTime) tags.Add("STATS TIME");
            if (Compatibility != CompatibilityMode.ServerDefault) tags.Add("COMPAT " + (Compatibility == CompatibilityMode.On ? "ON" : "OFF"));
            return tags;
        }
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    // A statement that re-asserts an option already in force, so it changes nothing on the session. It exists to give
    // the ODBC driver one more call after a run: ASE emits the execution times after the last row, and System.Data.Odbc
    // hands over diagnostics only on calls that report them, while the final SQLMoreResults reports no data. Without it
    // "Execution Time" and the elapsed time never arrive at all. Only statistics time asks for it; statistics io reads
    // its plan buffer via showplan_in_xml(0), and noexec executes nothing.
    public string? TrailingFlushStatement =>
        StatisticsTime && !NoExec ? "set statistics time on" : null;

    // Whether runs should read the executed plans back. Under noexec nothing executes, so there are no actual counts
    // to read, and the read itself would not run either.
    public bool CapturesExecutionIo => StatisticsIo && !NoExec;

    // The SET statements that move an ASE session from current to next, in an order that keeps each one effective:
    // Every other change is bracketed by turning noexec off and back on. Under noexec the server compiles without
    // running, and whether a SET issued there takes hold is the server's call, not something to rely on: an option
    // switched on while noexec was already on would otherwise look applied here and do nothing on the session.
    // Options that do not change emit nothing, and a return to ServerDefault emits nothing because ASE cannot
    // express it.
    public static IReadOnlyList<string> Script(AseSessionOptions current, AseSessionOptions next)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(next);
        var others = new List<string>();
        if (current.StatisticsTime != next.StatisticsTime) others.Add("set statistics time " + OnOff(next.StatisticsTime));
        if (current.Compatibility != next.Compatibility && next.Compatibility != CompatibilityMode.ServerDefault)
            others.Add("set compatibility_mode " + OnOff(next.Compatibility == CompatibilityMode.On));
        if (current.StatisticsIo != next.StatisticsIo)
        {
            if (next.StatisticsIo) others.Add("set textsize 16777216");
            others.Add(next.StatisticsIo ? "set plan for show_execio_xml to message on" : "set plan for show_execio_xml off");
        }
        else if (next.StatisticsIo && others.Count > 0)
        {
            others.Add("set plan for show_execio_xml to message on");
        }
        if (others.Count == 0 && current.NoExec == next.NoExec) return [];
        var lines = new List<string>();
        bool lift = current.NoExec && (others.Count > 0 || !next.NoExec);
        if (lift) lines.Add("set noexec off");
        lines.AddRange(others);
        if (next.NoExec && (lift || !current.NoExec)) lines.Add("set noexec on");
        return lines;
    }

    // Both statistics report on execution, so under noexec they have nothing to measure.
    public bool StatisticsSilencedByNoExec => NoExec && (StatisticsTime || StatisticsIo);
}
