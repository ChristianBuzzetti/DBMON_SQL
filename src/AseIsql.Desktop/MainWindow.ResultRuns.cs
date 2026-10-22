using System.Data;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // One execution's results: the SQL as it ran (parameters filled) and the session it ran on. Engine is null for a
    // multi-server run, which only Run on… can repeat. F9 on any tab of a run executes it again and replaces the run.
    private sealed record ResultRun(int Id, string Sql, IQueryEngine? Engine);
    private Dictionary<DataTable, ResultRun> resultRuns => activeSession.Runs;
    private int runCounter { get => activeSession.RunCounter; set => activeSession.RunCounter = value; }
    private int resultCounter { get => activeSession.ResultCounter; set => activeSession.ResultCounter = value; }
    // Results accumulate across executions; beyond this many tabs the oldest unpinned ones close.
    public const int MaxResultTabs = 40;
    private int lastRunId { get => activeSession.LastRunId; set => activeSession.LastRunId = value; }

    private ResultRun NewRun(string sql, IQueryEngine? session) => NewRun(activeSession, sql, session);
    private static ResultRun NewRun(SessionWorkspace owner, string sql, IQueryEngine? session) => new(++owner.RunCounter, sql, session);
    // Result tabs belong to their workspace. The active workspace's tabs are also mounted in the shell, right after
    // QueryTab, so a background run publishes into its own list without touching what is on screen.
    private IEnumerable<TabItem> ResultTabs => activeSession.Tabs;
    private SessionWorkspace? OwnerOf(TabItem? tab) => tab == null ? null : sessions.FirstOrDefault(session => session.Tabs.Contains(tab));
    private ResultRun? RunOf(TabItem? tab) => RunOf(OwnerOf(tab) ?? activeSession, tab);
    private static ResultRun? RunOf(SessionWorkspace owner, TabItem? tab) =>
        tab?.Tag is DataTable table && owner.Runs.TryGetValue(table, out var run) ? run : null;

    private void MountResultTab(SessionWorkspace owner, TabItem tab, int index)
    {
        index = Math.Clamp(index, 0, owner.Tabs.Count);
        owner.Tabs.Insert(index, tab);
        if (IsOnScreen(owner)) Workspace.Items.Insert(index + 1, tab);
    }
    private void UnmountResultTab(SessionWorkspace owner, TabItem tab)
    {
        if (!owner.Tabs.Remove(tab)) return;
        if (IsOnScreen(owner)) Workspace.Items.Remove(tab);
    }

    // Publishes one execution: a new run goes after the existing tabs; a re-run takes the place of its previous tabs
    // (pinned ones stay) and keeps their names, so a renamed result keeps its name across re-runs.
    private TabItem? PublishResults(IReadOnlyList<DataTable> tables, ResultRun run, int? replaceRun) => PublishResults(activeSession, tables, run, replaceRun);
    private TabItem? PublishResults(SessionWorkspace owner, IReadOnlyList<DataTable> tables, ResultRun run, int? replaceRun)
    {
        int insertAt = owner.Tabs.Count; var names = new List<string>();
        if (replaceRun is int id)
        {
            var previous = owner.Tabs.Where(t => RunOf(owner, t)?.Id == id && !owner.Pins.Contains((DataTable)t.Tag)).ToList();
            if (previous.Count > 0)
            {
                insertAt = owner.Tabs.IndexOf(previous[0]); names = previous.Select(ResultName).ToList();
                int nextNumber = owner.ResultCounter;
                foreach (var tab in previous) CloseResult(tab);
                // Replacement is not a user closing every result: retain the sequence and the saved names.
                owner.ResultCounter = nextNumber;
                insertAt = Math.Min(insertAt, owner.Tabs.Count);
            }
        }
        TabItem? first = null;
        for (int i = 0; i < tables.Count; i++)
        {
            string? preferred = tables[i].ExtendedProperties["ResultHeader"] as string;
            string header = i < names.Count ? names[i] : preferred ?? "Result " + (++owner.ResultCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tab = AddResult(tables[i], 0, header: header, insertAt: insertAt + i, owner: owner);
            owner.Runs[tables[i]] = run; first ??= tab;
        }
        TrimResults(owner, run.Id);
        return first;
    }

    // Keeps each workspace bounded: the oldest unpinned results of other runs close first, and Messages says so.
    private void TrimResults(int keepRun) => TrimResults(activeSession, keepRun);
    private void TrimResults(SessionWorkspace owner, int keepRun)
    {
        int excess = owner.Tabs.Count - MaxResultTabs; if (excess <= 0) return;
        var oldest = owner.Tabs.Where(t => !owner.Pins.Contains((DataTable)t.Tag) && RunOf(owner, t)?.Id != keepRun).Take(excess).ToList();
        foreach (var tab in oldest) CloseResult(tab);
        if (oldest.Count > 0) AddMessage(owner, 0, "CLIENT", $"Closed the {Counts.Of(oldest.Count, "oldest result")} to stay within {MaxResultTabs} result tabs. Pin a result to keep it.");
    }

    // F9 on a result tab: that run's SQL again on its own session, replacing it; null when the editor should run.
    private (string Sql, int? Replace, string Label)? RerunOfSelectedResult(out string? refusal)
    {
        refusal = null;
        if (Workspace.SelectedItem is not TabItem tab || RunOf(tab) is not { } run) return null;
        if (run.Engine == null) { refusal = "This result came from a multi-server run. Use Run on… (Shift+F9) to run it again."; return null; }
        if (!ReferenceEquals(run.Engine, engine)) { refusal = "This result came from an earlier session. Return to Query to run SQL on the current one."; return null; }
        // A pinned result is kept as it is: running it again adds a new result beside it.
        return (run.Sql, pinnedResults.Contains((DataTable)tab.Tag) ? null : run.Id, "re-run · " + ResultName(tab));
    }
}
