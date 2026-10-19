using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AseIsql;

// One access to a table or index inside an executed plan: the operator that read it, what the optimizer expected
// (est) and what the run actually did (act). Null where the server did not report a value.
public sealed record ExecutionIoScan(string Operator, string Object, string? Alias, string? Index, string? ScanType,
    long? ActualRows, long? LogicalReads, long? PhysicalReads, long? EstimatedRows, long? EstimatedLogicalReads, long? EstimatedPhysicalReads);

// One statement's executed plan, as ASE wrote it for "set plan for show_execio_xml to message on".
public sealed record ExecutionIoPlan(int Statement, int Line, string Text, IReadOnlyList<ExecutionIoScan> Scans, IReadOnlyList<string> MissingStatistics, string Xml);

public sealed record ExecutionIoReport(IReadOnlyList<ExecutionIoPlan> Plans, int Unreadable);

// Reads the buffer returned by showplan_in_xml(0): one XML document per statement, concatenated, each starting with
// its own declaration. The documents are the server's; nothing here infers a value it did not write. In particular
// there is no scan count: the XML does not carry one, and deriving it from the outer side of a join holds for some
// plans only.
public static class ExecutionIo
{
    // The read-back statements of the app itself are captured like any other and are not the user's.
    public const string BufferRead = "showplan_in_xml(";
    public const string InternalMarker = "dbmon_execio";
    private static readonly Regex Declaration = new(@"<\?xml\b", RegexOptions.NonBacktracking);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.NonBacktracking);
    private const string SubordinatePrefix = "Subordinate SQL Text:";

    public static ExecutionIoReport Parse(string? buffer)
    {
        var plans = new List<ExecutionIoPlan>(); int unreadable = 0;
        if (string.IsNullOrWhiteSpace(buffer)) return new(plans, 0);
        var starts = Declaration.Matches(buffer).Select(m => m.Index).ToList();
        if (starts.Count == 0) starts.Add(0);
        for (int i = 0; i < starts.Count; i++)
        {
            string xml = buffer[starts[i]..(i + 1 < starts.Count ? starts[i + 1] : buffer.Length)].Trim();
            if (xml.Length == 0) continue;
            var plan = ParseDocument(xml);
            if (plan == null) { unreadable++; continue; }
            if (plan.Text.Contains(BufferRead, StringComparison.OrdinalIgnoreCase) || plan.Text.Contains(InternalMarker, StringComparison.OrdinalIgnoreCase)) continue;
            plans.Add(plan);
        }
        return new(plans, unreadable);
    }

    private static ExecutionIoPlan? ParseDocument(string xml)
    {
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64L * 1024 * 1024 };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException) { return null; }
        var query = document.Root;
        if (query == null || query.Name.LocalName != "query") return null;
        string text = Spaces.Replace((string?)query.Element("text") ?? "", " ").Trim();
        if (text.StartsWith(SubordinatePrefix, StringComparison.Ordinal)) text = text[SubordinatePrefix.Length..].TrimStart();
        var scans = new List<ExecutionIoScan>();
        // An access operator is the one that names its object: TableScan, IndexScan, and the insert/update/delete
        // operators that write to one. Containers such as joins and aggregates carry no objName of their own.
        foreach (var node in (query.Element("opTree") ?? query).Descendants().Where(e => e.Element("objName") != null))
        {
            var act = node.Element("act"); var est = node.Element("est");
            scans.Add(new ExecutionIoScan(node.Name.LocalName, Value(node, "objName")!, Value(node, "corrName"), Value(node, "indName"), Value(node, "scanType"),
                Number(act, "rowCnt"), Number(act, "lio"), Number(act, "pio"), Number(est, "rowCnt"), Number(est, "lio"), Number(est, "pio")));
        }
        var missing = (query.Element("optimizerStatistics")?.Elements("statInfo") ?? [])
            .SelectMany(info => info.Elements("missingHistogram").Elements("column").Select(column => (Value(info, "objName") ?? "?") + "." + column.Value.Trim()))
            .Distinct(StringComparer.Ordinal).ToList();
        return new ExecutionIoPlan(Int(query, "statementNum"), Int(query, "lineNum"), text, scans, missing, xml);
    }

    private static string? Value(XElement? parent, string name) => parent?.Element(name)?.Value.Trim() is { Length: > 0 } value ? value : null;
    private static long? Number(XElement? parent, string name) =>
        long.TryParse(Value(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;
    private static int Int(XElement parent, string name) => (int)Math.Clamp(Number(parent, name) ?? 0, 0, int.MaxValue);

    public const string ResultHeader = "Statistics IO";

    // One typed row per access operator, in plan order. Values stay numbers and unknown stays NULL, so the grid's
    // sorting, filtering, aggregates and exports treat them as data.
    public static DataTable ToTable(IReadOnlyList<ExecutionIoPlan> plans)
    {
        var table = new DataTable(ResultHeader);
        table.ExtendedProperties["ResultHeader"] = ResultHeader;
        table.Columns.Add("statement", typeof(int)); table.Columns.Add("line", typeof(int));
        table.Columns.Add("object", typeof(string)); table.Columns.Add("alias", typeof(string)); table.Columns.Add("index", typeof(string));
        table.Columns.Add("operator", typeof(string)); table.Columns.Add("scan_type", typeof(string));
        foreach (string name in new[] { "actual_rows", "logical_reads", "physical_reads", "est_rows", "est_logical_reads", "est_physical_reads" }) table.Columns.Add(name, typeof(long));
        table.Columns.Add("sql_text", typeof(string));
        static object Cell(object? value) => value ?? DBNull.Value;
        foreach (var plan in plans)
            foreach (var scan in plan.Scans)
                table.Rows.Add(plan.Statement, plan.Line, scan.Object, Cell(scan.Alias), Cell(scan.Index), scan.Operator, Cell(scan.ScanType),
                    Cell(scan.ActualRows), Cell(scan.LogicalReads), Cell(scan.PhysicalReads), Cell(scan.EstimatedRows), Cell(scan.EstimatedLogicalReads), Cell(scan.EstimatedPhysicalReads), plan.Text);
        return table;
    }

    // The per-table lines of isql's statistics io, from the actual counts, one statement at a time. An object read by
    // several operators of one statement is summed, as statistics io does; the scan count is not claimed.
    public static IReadOnlyList<string> Summary(ExecutionIoPlan plan)
    {
        static string Count(long? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "n/a";
        static long? Sum(IEnumerable<long?> values) { var known = values.Where(v => v.HasValue).ToList(); return known.Count == 0 ? null : known.Sum(v => v!.Value); }
        return plan.Scans.GroupBy(scan => scan.Object, StringComparer.OrdinalIgnoreCase).Select(group =>
            $"Table: {group.Key} logical reads: {Count(Sum(group.Select(s => s.LogicalReads)))}, physical reads: {Count(Sum(group.Select(s => s.PhysicalReads)))}, rows: {Count(Sum(group.Select(s => s.ActualRows)))}").ToList();
    }
}
