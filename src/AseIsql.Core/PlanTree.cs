using System.Globalization;
using System.Xml.Linq;

namespace AseIsql;

// Counts of one operator as the server wrote them. Null where the plan does not carry the value (an estimated plan
// has no act section at all).
public sealed record PlanMetrics(long? Rows, long? LogicalReads, long? PhysicalReads)
{
    public static readonly PlanMetrics None = new(null, null, null);
    public bool IsEmpty => Rows == null && LogicalReads == null && PhysicalReads == null;
}

// One operator of the opTree, containers included (Emit, joins, aggregates, sorts...). Key identifies the node by its
// path of operator labels from the root, with an ordinal among equal siblings, so two captures of the same query line up.
public sealed record PlanNode(string Operator, string Key, int Depth, string? Object, string? Alias, string? Index, string? ScanType,
    PlanMetrics Estimated, PlanMetrics Actual, IReadOnlyList<PlanNode> Children)
{
    public string Label => Object == null ? Operator : $"{Operator} ({Object}{(Alias == null ? "" : " " + Alias)}{(Index == null ? "" : ", " + Index)})";
}

public enum PlanSeverity { None, Warning, Error }

public sealed record PlanEstimateIssue(string Metric, long Estimated, long Actual, PlanSeverity Severity, string Message);

public static class PlanTree
{
    // Deviation thresholds: act/est at or beyond 10x either way is a warning, at or beyond 100x (or a non-zero actual
    // against a zero estimate) is an error.
    public const double WarningRatio = 10, ErrorRatio = 100;

    // ASE writes operators as PascalCase elements and their properties (est, act, objName, scanType, ...) as camelCase
    // ones; that is what tells a child operator from an attribute of its parent. <VA>n</VA> (the operator's virtual
    // address) is uppercase too but a leaf: an operator always carries child elements.
    private static bool IsOperator(XElement element) =>
        element.Name.LocalName is { Length: > 0 } name && char.IsUpper(name[0]) && name != "VA" && element.HasElements;

    // The operator forest of one <query> document, or of a bare XML string holding one (first document only).
    public static IReadOnlyList<PlanNode> Parse(string? xml) =>
        ExecutionIo.Parse(xml).Plans.FirstOrDefault()?.Tree ?? [];

    internal static IReadOnlyList<PlanNode> FromQuery(XElement query) =>
        ExecutionIo.OperatorTree(query) is { } tree ? Build(tree, "", 0) : [];

    private static List<PlanNode> Build(XElement parent, string parentKey, int depth)
    {
        var nodes = new List<PlanNode>(); var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var element in parent.Elements().Where(IsOperator))
        {
            string? obj = ExecutionIo.Value(element, "objName"), alias = ExecutionIo.Value(element, "corrName");
            string label = element.Name.LocalName + (obj == null ? "" : "[" + obj + (alias == null ? "" : " " + alias) + "]");
            int ordinal = seen.TryGetValue(label, out int n) ? n : 0; seen[label] = ordinal + 1;
            string key = (parentKey.Length == 0 ? "" : parentKey + "/") + label + (ordinal == 0 ? "" : "#" + ordinal.ToString(CultureInfo.InvariantCulture));
            nodes.Add(new PlanNode(element.Name.LocalName, key, depth, obj, alias, ExecutionIo.Value(element, "indName"), ExecutionIo.Value(element, "scanType"),
                Metrics(element.Element("est")), Metrics(element.Element("act")), Build(element, key, depth + 1)));
        }
        return nodes;
    }

    private static PlanMetrics Metrics(XElement? section) => section == null ? PlanMetrics.None
        : new(ExecutionIo.Number(section, "rowCnt"), ExecutionIo.Number(section, "lio"), ExecutionIo.Number(section, "pio"));

    // Pre-order: each operator before its inputs, the order the viewer shows them.
    public static IEnumerable<PlanNode> Flatten(IEnumerable<PlanNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    // Estimate-vs-actual problems of one operator, on rows and logical reads. Nothing is flagged when either side is
    // missing: an estimated plan shows its estimates without being a problem.
    public static IReadOnlyList<PlanEstimateIssue> Assess(PlanNode node)
    {
        var issues = new List<PlanEstimateIssue>();
        Add(issues, "rows", node.Estimated.Rows, node.Actual.Rows);
        Add(issues, "logical reads", node.Estimated.LogicalReads, node.Actual.LogicalReads);
        return issues;
    }

    public static PlanSeverity Severity(PlanNode node) => Assess(node).Select(i => i.Severity).DefaultIfEmpty(PlanSeverity.None).Max();

    public static PlanSeverity Classify(long? estimated, long? actual)
    {
        if (estimated is not { } est || actual is not { } act || est < 0 || act < 0) return PlanSeverity.None;
        if (est == 0) return act > 0 ? PlanSeverity.Error : PlanSeverity.None;
        double ratio = (double)act / est;
        if (ratio >= ErrorRatio || ratio <= 1 / ErrorRatio) return PlanSeverity.Error;
        return ratio >= WarningRatio || ratio <= 1 / WarningRatio ? PlanSeverity.Warning : PlanSeverity.None;
    }

    private static void Add(List<PlanEstimateIssue> issues, string metric, long? estimated, long? actual)
    {
        var severity = Classify(estimated, actual);
        if (severity == PlanSeverity.None) return;
        long est = estimated!.Value, act = actual!.Value;
        var inv = CultureInfo.InvariantCulture;
        string factor = est == 0 ? "estimate 0" : act >= est ? string.Create(inv, $"{(double)act / est:0.#}x over estimate") : string.Create(inv, $"{(double)est / Math.Max(act, 1):0.#}x under estimate");
        issues.Add(new PlanEstimateIssue(metric, est, act, severity,
            string.Create(CultureInfo.InvariantCulture, $"{metric}: actual {act:N0} vs estimated {est:N0} ({factor})")));
    }
}
