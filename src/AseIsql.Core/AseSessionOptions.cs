namespace AseIsql;

// compatibility_mode starts at whatever the server configured for the login, so it is tri-state: ServerDefault means
// the app has not touched it and cannot claim to know its value. ASE has no statement that restores that state, so
// once the user chooses, the only moves are On and Off.
public enum CompatibilityMode { ServerDefault, On, Off }

// statistics io is deliberately absent. Its substance is the per-table "Table: <name> scan count N, logical reads:
// ..." record, and the SAP ASE ODBC driver hands that one to System.Data.Odbc with no body: every shorter record of
// the same run arrives whole, that one arrives as a bare transport frame, so the option could only ever have shown
// "Total writes for this command". Verified against a real table, not inferred. Do not add it back through this
// channel; the per-statement counters live in ASE's MDA tables (monSysStatement.LogicalReads and PhysicalReads).
//
// The SAP ASE SET options DBMON SQL drives from Tools. They are plain SET statements sent on the open session: they
// live in that connection only, disappear when it closes, and never reach the multi-server or repeat sessions, which
// open their own. The app tracks what it sent, never what the server was configured with.
public sealed record AseSessionOptions(bool NoExec = false, bool StatisticsTime = false, bool ShowPlan = false, CompatibilityMode Compatibility = CompatibilityMode.ServerDefault)
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
            if (ShowPlan) tags.Add("SHOWPLAN");
            if (StatisticsTime) tags.Add("STATS TIME");
            if (Compatibility != CompatibilityMode.ServerDefault) tags.Add("COMPAT " + (Compatibility == CompatibilityMode.On ? "ON" : "OFF"));
            return tags;
        }
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    // A statement that re-asserts an option already in force, so it changes nothing on the session. It exists to give
    // the ODBC driver one more call after a run: ASE emits the execution times and the plan's tail after the last
    // row, and System.Data.Odbc hands over diagnostics only on calls that report them, while the final
    // SQLMoreResults reports no data. Without it "Execution Time" and the elapsed time never arrive at all. Only
    // the options that produce that trailing block ask for it; noexec alone produces none, because nothing ran.
    public string? TrailingFlushStatement =>
        StatisticsTime ? "set statistics time on"
        : ShowPlan ? "set showplan on"
        : null;

    // The SET statements that move an ASE session from current to next, in an order that keeps each one effective:
    // Every other change is bracketed by turning noexec off and back on. Under noexec the server compiles without
    // running, and whether a SET issued there takes hold is the server's call, not something to rely on: a showplan
    // switched on while noexec was already on would otherwise look applied here and do nothing on the session.
    // Options that do not change emit nothing, and a return to ServerDefault emits nothing because ASE cannot
    // express it.
    public static IReadOnlyList<string> Script(AseSessionOptions current, AseSessionOptions next)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(next);
        var others = new List<string>();
        if (current.ShowPlan != next.ShowPlan) others.Add("set showplan " + OnOff(next.ShowPlan));
        if (current.StatisticsTime != next.StatisticsTime) others.Add("set statistics time " + OnOff(next.StatisticsTime));
        if (current.Compatibility != next.Compatibility && next.Compatibility != CompatibilityMode.ServerDefault)
            others.Add("set compatibility_mode " + OnOff(next.Compatibility == CompatibilityMode.On));
        if (others.Count == 0 && current.NoExec == next.NoExec) return [];
        var lines = new List<string>();
        bool lift = current.NoExec && (others.Count > 0 || !next.NoExec);
        if (lift) lines.Add("set noexec off");
        lines.AddRange(others);
        if (next.NoExec && (lift || !current.NoExec)) lines.Add("set noexec on");
        return lines;
    }

    // statistics time reports on execution, so under noexec it has nothing to measure. showplan still produces its
    // plan, which is the point of the pair.
    public bool StatisticsSilencedByNoExec => NoExec && StatisticsTime;
}
