using System.Data;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // One execution's results: the SQL as it ran (parameters filled) and the session it ran on. Engine is null for a
    // multi-server run, which only Run on… can repeat. F9 on any tab of a run executes it again and replaces the run.
    private sealed record ResultRun(int Id, string Sql, IQueryEngine? Engine);
    private readonly Dictionary<DataTable, ResultRun> resultRuns = [];
    private int runCounter, resultCounter;
    // Results accumulate across executions; beyond this many tabs the oldest unpinned ones close.
    public const int MaxResultTabs = 40;
    private int lastRunId;

    private ResultRun NewRun(string sql, IQueryEngine? session) => new(++runCounter, sql, session);
    private IEnumerable<TabItem> ResultTabs => Workspace.Items.OfType<TabItem>().Where(t => t.Tag is DataTable);
    private ResultRun? RunOf(TabItem? tab) => tab?.Tag is DataTable table && resultRuns.TryGetValue(table, out var run) ? run : null;

    // Publishes one execution: a new run goes after the existing tabs; a re-run takes the place of its previous tabs
    // (pinned ones stay) and keeps their names, so a renamed result keeps its name across re-runs.
    private TabItem? PublishResults(IReadOnlyList<DataTable> tables, ResultRun run, int? replaceRun)
    {
        int insertAt = Workspace.Items.Count; var names = new List<string>();
        if (replaceRun is int id)
        {
            var previous = ResultTabs.Where(t => RunOf(t)?.Id == id && !pinnedResults.Contains((DataTable)t.Tag)).ToList();
            if (previous.Count > 0)
            {
                insertAt = Workspace.Items.IndexOf(previous[0]); names = previous.Select(ResultName).ToList();
                int nextNumber = resultCounter;
                foreach (var tab in previous) CloseResult(tab);
                // Replacement is not a user closing every result: retain the sequence and the saved names.
                resultCounter = nextNumber;
                insertAt = Math.Min(insertAt, Workspace.Items.Count);
            }
        }
        TabItem? first = null;
        for (int i = 0; i < tables.Count; i++)
        {
            string? preferred = tables[i].ExtendedProperties["ResultHeader"] as string;
            string header = i < names.Count ? names[i] : preferred ?? "Result " + (++resultCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tab = AddResult(tables[i], 0, header: header, insertAt: insertAt + i);
            resultRuns[tables[i]] = run; first ??= tab;
        }
        TrimResults(run.Id);
        return first;
    }

    // Keeps the workspace bounded: the oldest unpinned results of other runs close first, and Messages says so.
    private void TrimResults(int keepRun)
    {
        int excess = ResultTabs.Count() - MaxResultTabs; if (excess <= 0) return;
        var oldest = ResultTabs.Where(t => !pinnedResults.Contains((DataTable)t.Tag) && RunOf(t)?.Id != keepRun).Take(excess).ToList();
        foreach (var tab in oldest) CloseResult(tab);
        if (oldest.Count > 0) AddMessage(0, "CLIENT", $"Closed the {Counts.Of(oldest.Count, "oldest result")} to stay within {MaxResultTabs} result tabs. Pin a result to keep it.");
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
