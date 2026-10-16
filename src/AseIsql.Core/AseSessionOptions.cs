namespace AseIsql;

// compatibility_mode starts at whatever the server configured for the login, so it is tri-state: ServerDefault means
// the app has not touched it and cannot claim to know its value. ASE has no statement that restores that state, so
// once the user chooses, the only moves are On and Off.
public enum CompatibilityMode { ServerDefault, On, Off }

// The SAP ASE SET options DBMON SQL drives from Tools. They are plain SET statements sent on the open session: they
// live in that connection only, disappear when it closes, and never reach the multi-server or repeat sessions, which
// open their own. The app tracks what it sent, never what the server was configured with.
public sealed record AseSessionOptions(bool NoExec = false, bool StatisticsIo = false, bool StatisticsTime = false, bool ShowPlan = false, CompatibilityMode Compatibility = CompatibilityMode.ServerDefault)
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
            if (StatisticsIo) tags.Add("STATS IO");
            if (StatisticsTime) tags.Add("STATS TIME");
            if (Compatibility != CompatibilityMode.ServerDefault) tags.Add("COMPAT " + (Compatibility == CompatibilityMode.On ? "ON" : "OFF"));
            return tags;
        }
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    // The SET statements that move an ASE session from current to next, in an order that keeps each one effective:
    // "set noexec off" leads, because under noexec the server compiles without running, and "set noexec on" trails,
    // so everything the user asked for is already in place when it takes hold. Options that do not change emit
    // nothing, and a return to ServerDefault emits nothing because ASE cannot express it.
    public static IReadOnlyList<string> Script(AseSessionOptions current, AseSessionOptions next)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(next);
        var lines = new List<string>();
        if (current.NoExec && !next.NoExec) lines.Add("set noexec off");
        if (current.ShowPlan != next.ShowPlan) lines.Add("set showplan " + OnOff(next.ShowPlan));
        if (current.StatisticsIo != next.StatisticsIo) lines.Add("set statistics io " + OnOff(next.StatisticsIo));
        if (current.StatisticsTime != next.StatisticsTime) lines.Add("set statistics time " + OnOff(next.StatisticsTime));
        if (current.Compatibility != next.Compatibility && next.Compatibility != CompatibilityMode.ServerDefault)
            lines.Add("set compatibility_mode " + OnOff(next.Compatibility == CompatibilityMode.On));
        if (!current.NoExec && next.NoExec) lines.Add("set noexec on");
        return lines;
    }
}
