using System.Data;

namespace AseIsql;

public enum PlanDiffKind { Same, OnlyA, OnlyB, Different }

// One operator lined up across two plans by its PlanNode.Key. Changed names the compared metrics that differ:
// index, est_rows, act_rows, est_lio, act_lio.
public sealed record PlanDiffEntry(string Key, PlanDiffKind Kind, PlanNode? A, PlanNode? B, IReadOnlyList<string> Changed)
{
    public PlanNode Node => A ?? B!;
}

public static class PlanCompare
{
    public const string ResultHeader = "Plan compare";

    public static IReadOnlyList<PlanDiffEntry> Compare(ExecutionIoPlan a, ExecutionIoPlan b) => Compare(a.Tree, b.Tree);

    // A's operators in plan order (matched or only-in-A), then those only B has, in B's order.
    public static IReadOnlyList<PlanDiffEntry> Compare(IReadOnlyList<PlanNode> a, IReadOnlyList<PlanNode> b)
    {
        var right = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        foreach (var node in PlanTree.Flatten(b)) right.TryAdd(node.Key, node);
        var entries = new List<PlanDiffEntry>(); var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var left in PlanTree.Flatten(a))
        {
            if (!right.TryGetValue(left.Key, out var other)) { entries.Add(new(left.Key, PlanDiffKind.OnlyA, left, null, [])); continue; }
            matched.Add(left.Key);
            var changed = new List<string>();
            if (!string.Equals(left.Index, other.Index, StringComparison.OrdinalIgnoreCase)) changed.Add("index");
            if (left.Estimated.Rows != other.Estimated.Rows) changed.Add("est_rows");
            if (left.Actual.Rows != other.Actual.Rows) changed.Add("act_rows");
            if (left.Estimated.LogicalReads != other.Estimated.LogicalReads) changed.Add("est_lio");
            if (left.Actual.LogicalReads != other.Actual.LogicalReads) changed.Add("act_lio");
            entries.Add(new(left.Key, changed.Count == 0 ? PlanDiffKind.Same : PlanDiffKind.Different, left, other, changed));
        }
        foreach (var node in PlanTree.Flatten(b).Where(n => !matched.Contains(n.Key)))
            if (matched.Add(node.Key)) entries.Add(new(node.Key, PlanDiffKind.OnlyB, null, node, []));
        return entries;
    }

    // Grid form: one row per lined-up operator, both sides' metrics as numbers (NULL when absent).
    public static DataTable ToTable(IReadOnlyList<PlanDiffEntry> entries)
    {
        var table = new DataTable(ResultHeader);
        table.ExtendedProperties["ResultHeader"] = ResultHeader;
        table.Columns.Add("status", typeof(string)); table.Columns.Add("operator", typeof(string)); table.Columns.Add("depth", typeof(int));
        table.Columns.Add("object", typeof(string)); table.Columns.Add("index", typeof(string));
        foreach (string name in new[] { "a_est_rows", "b_est_rows", "a_act_rows", "b_act_rows", "a_est_lio", "b_est_lio", "a_act_lio", "b_act_lio" }) table.Columns.Add(name, typeof(long));
        table.Columns.Add("changed", typeof(string)); table.Columns.Add("key", typeof(string));
        static object Cell(long? value) => value.HasValue ? value.Value : DBNull.Value;
        foreach (var e in entries)
        {
            var n = e.Node; PlanMetrics aEst = e.A?.Estimated ?? PlanMetrics.None, bEst = e.B?.Estimated ?? PlanMetrics.None, aAct = e.A?.Actual ?? PlanMetrics.None, bAct = e.B?.Actual ?? PlanMetrics.None;
            string status = e.Kind switch { PlanDiffKind.OnlyA => "only in A", PlanDiffKind.OnlyB => "only in B", PlanDiffKind.Different => "different", _ => "same" };
            table.Rows.Add(status, n.Operator, n.Depth, (object?)n.Object ?? DBNull.Value, (object?)(e.A?.Index ?? e.B?.Index) ?? DBNull.Value,
                Cell(aEst.Rows), Cell(bEst.Rows), Cell(aAct.Rows), Cell(bAct.Rows), Cell(aEst.LogicalReads), Cell(bEst.LogicalReads), Cell(aAct.LogicalReads), Cell(bAct.LogicalReads),
                string.Join(", ", e.Changed), e.Key);
        }
        return table;
    }
}
