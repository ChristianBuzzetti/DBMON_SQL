using System.Data;
using System.Diagnostics;

namespace AseIsql;

// One server of a multi-server run: a label the user recognizes and the engine that owns that server's session.
public sealed record RunTarget(string Label, IQueryEngine Engine);
public sealed record TargetOutcome(RunTarget Target, QueryOutput Output, double ElapsedMs);
// Result number Index of every target, stacked under a leading server column; Reason explains a missing table.
public sealed record CombinedResult(int Index, DataTable? Table, string? Reason);

public static class MultiRun
{
    public const int MaxTargets = 8;
    public const int MaxCombinedRows = 200_000;
    public const string ServerCaption = "server";

    // Each engine owns its own connection and runs on its own worker, so the servers work in parallel. A target that
    // fails or is cancelled reports it in its own output; it never stops or hides the outcome of the others.
    public static async Task<IReadOnlyList<TargetOutcome>> ExecuteAsync(IReadOnlyList<RunTarget> targets, string sql, CancellationToken cancellation = default)
    {
        if (targets.Count == 0) throw new ArgumentException("Choose at least one server.");
        if (targets.Count > MaxTargets) throw new ArgumentException($"Local limit: {MaxTargets} servers per run.");
        if (targets.Select(t => t.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count) throw new ArgumentException("Server labels must be unique within a run.");
        var runs = targets.Select(target => Task.Run(() => {
            var timer = Stopwatch.StartNew();
            QueryOutput output;
            try { output = target.Engine.Execute(sql, cancellation); }
            catch (OperationCanceledException) { output = new QueryOutput { Error = "Cancellation requested. Received results are partial; verify the session before continuing." }; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { output = new QueryOutput { Error = "Execution failed: " + ex.Message }; }
            return new TargetOutcome(target, output, timer.Elapsed.TotalMilliseconds);
        }));
        return await Task.WhenAll(runs);
    }

    // Combines result k of every target that produced it when names and types agree column by column. Values are
    // copied as received (typed, DBNull kept); the server column is a product label, not a source column.
    public static IReadOnlyList<CombinedResult> Combine(IReadOnlyList<TargetOutcome> outcomes)
    {
        var combined = new List<CombinedResult>();
        int results = outcomes.Count == 0 ? 0 : outcomes.Max(o => o.Output.Tables.Count);
        for (int index = 0; index < results; index++)
        {
            var parts = outcomes.Where(o => o.Output.Tables.Count > index).Select(o => (o.Target.Label, Table: o.Output.Tables[index])).ToArray();
            if (parts.Length < 2) { combined.Add(new(index, null, $"Result {index + 1} came from one server only.")); continue; }
            var first = parts[0].Table;
            bool sameShape = parts.All(p => p.Table.Columns.Count == first.Columns.Count && Enumerable.Range(0, first.Columns.Count).All(c =>
                p.Table.Columns[c].DataType == first.Columns[c].DataType && string.Equals(p.Table.Columns[c].Caption, first.Columns[c].Caption, StringComparison.OrdinalIgnoreCase)));
            if (!sameShape) { combined.Add(new(index, null, $"Result {index + 1} differs in columns or types between servers; see each server's tab.")); continue; }
            long rows = parts.Sum(p => (long)p.Table.Rows.Count);
            if (rows > MaxCombinedRows) { combined.Add(new(index, null, $"Result {index + 1} has {Counts.Of(rows, "row")}; the combined view stops at {Counts.Of(MaxCombinedRows, "row")}.")); continue; }
            var table = new DataTable();
            var server = table.Columns.Add("column_server", typeof(string)); server.Caption = ServerCaption; server.ExtendedProperties["SourceName"] = ServerCaption;
            foreach (DataColumn column in first.Columns)
            {
                var added = table.Columns.Add(column.ColumnName, column.DataType); added.Caption = column.Caption;
                added.ExtendedProperties["SourceName"] = column.ExtendedProperties["SourceName"] ?? column.Caption;
            }
            table.BeginLoadData();
            foreach (var (label, part) in parts)
                foreach (DataRow row in part.Rows) table.Rows.Add([label, .. row.ItemArray]);
            table.EndLoadData();
            combined.Add(new(index, table, null));
        }
        return combined;
    }
}
