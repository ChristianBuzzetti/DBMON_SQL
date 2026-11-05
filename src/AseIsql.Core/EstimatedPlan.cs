using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// The estimated plan of a script, compiled and optimized by SAP ASE but never executed. The exact sequence sent on the
// session, each line its own batch:
//
//   set flushmessage off                                  first: with it on, ASE flushes the plan and the buffer reads empty
//   [set noexec off]                                      only when the session already had NOEXEC on
//   set plan for show_final_plan_xml to message on
//   set noexec on
//   <the user's script, batch by batch, as typed>         compiled only: noexec runs none of it
//   /* dbmon_execio */ set noexec off                     the one statement noexec still executes
//   /* dbmon_execio */ select showplan_in_xml(0)          then (-1) when 0 returns an empty buffer
//   set plan for show_final_plan_xml off
//   set flushmessage on                                   the login default again (noexec is already off)
//   [set plan for show_execio_xml to message on]          only when Statistics IO was on, re-asserted
//   [set noexec on]                                       only when the session had NOEXEC on
//
// show_final_plan_xml carries the optimizer's est counts and no act section, so the viewer shows estimates and flags
// nothing. The script is refused when it mentions noexec itself: a "set noexec off" inside it would run what follows.
public static class EstimatedPlan
{
    public const string FlushOff = "set flushmessage off";
    public const string FlushOn = "set flushmessage on";
    public const string Option = "set plan for show_final_plan_xml";
    private static readonly Regex NoExec = new(@"\bnoexec\b", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    public static string? Refusal(string? sql) =>
        string.IsNullOrWhiteSpace(sql) ? "No SQL to explain."
        : NoExec.IsMatch(sql) ? "The script mentions noexec. The estimated plan runs it under set noexec on, which the script could turn off; remove that statement first."
        : null;

    public static IReadOnlyList<string> Prepare(AseSessionOptions current) =>
        [FlushOff, .. current.NoExec ? new[] { "set noexec off" } : [], Option + " to message on", "set noexec on"];

    public static string Lift => "/* " + ExecutionIo.InternalMarker + " */ set noexec off";
    public static string Read(int index) => "/* " + ExecutionIo.InternalMarker + " */ select showplan_in_xml(" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";

    // Run with noexec already off: the session goes back to the options the window tracks for it.
    public static IReadOnlyList<string> Restore(AseSessionOptions current) =>
        [Option + " off", FlushOn, .. current.StatisticsIo ? new[] { "set plan for show_execio_xml to message on" } : [], .. current.NoExec ? new[] { "set noexec on" } : []];

    public static string Batches(IEnumerable<string> statements) => string.Join(Environment.NewLine + "go" + Environment.NewLine, statements);

    // showplan_in_xml returns its buffer as text cells, possibly split across rows.
    public static string Xml(IEnumerable<DataTable> tables)
    {
        var xml = new StringBuilder();
        foreach (var table in tables)
            foreach (DataRow row in table.Rows)
                foreach (object? value in row.ItemArray)
                    if (value is string text && text.Length > 0) xml.AppendLine(text);
        return xml.ToString().Trim();
    }

    // The user's statements only: the app's own SET and read-back batches, and statements without an operator tree
    // (a SET compiles to no plan worth showing), are left out.
    public static IReadOnlyList<ExecutionIoPlan> Plans(string? xml) =>
        ExecutionIo.Parse(xml).Plans.Where(p => p.Tree.Count > 0 && !p.Text.Contains(Option, StringComparison.OrdinalIgnoreCase)).ToList();
}
