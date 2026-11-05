using System.Data;
using System.Globalization;

namespace AseIsql;

public sealed record ActivityProcess(int Spid, string Status, string Login, string Host, string Program,
    string Database, string Command, long Cpu, long PhysicalIo, long Memory, int BlockedBy,
    long TimeBlocked, string Transaction, int? Suid)
{
    public string SearchText => $"{Spid} {Status} {Login} {Host} {Program} {Database} {Command} {BlockedBy} {Transaction}";
}

public sealed class BlockingNode(ActivityProcess process)
{
    public ActivityProcess Process { get; } = process;
    public List<BlockingNode> Children { get; } = [];
    public BlockingNode? Parent { get; internal set; }
    public bool InCycle { get; internal set; }
    public bool IsHeadBlocker => Parent == null && Children.Count > 0 && !InCycle && Process.BlockedBy == 0;
}

// UI-free SQL, parsing and guard rules. All interpolated SPIDs are validated integers.
public static class ActivityMonitor
{
    public const string IdentitySql = "select @@spid as monitor_spid";
    public const string ProcessesSql = """
        select p.spid, p.status, suser_name(p.suid) as loginame, p.hostname, p.program_name,
               db_name(p.dbid) as dbname, p.cmd, p.cpu, p.physical_io, p.memusage,
               p.blocked, p.time_blocked, p.tran_name, p.suid
        from master..sysprocesses p order by p.spid
        """;

    private static string Spid(int spid) => spid > 0 ? spid.ToString(CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(spid));
    public static string ProcessSql(int spid) => ProcessesSql.Replace("order by p.spid", "where p.spid = " + Spid(spid), StringComparison.Ordinal);
    public static string SqlTextSql(int spid, bool currentStatement = true) => currentStatement
        ? "select t.SQLText from master..monProcessSQLText t where t.SPID = " + Spid(spid) +
          " and exists (select 1 from master..monProcessStatement s where s.SPID = t.SPID and s.KPID = t.KPID and s.BatchID = t.BatchID) order by t.BatchID, t.LineNumber, t.SequenceInLine"
        : "select SQLText from master..monProcessSQLText where SPID = " + Spid(spid) + " order by BatchID, LineNumber, SequenceInLine";
    public static string LocksSql(int spid) => "select * from master..monLocks where SPID = " + Spid(spid);

    public static IReadOnlyList<ActivityProcess> Parse(DataTable table)
    {
        if (ResultColumns.Find(table, "spid") == null)
            throw new ArgumentException("Activity result has no spid column; received: " + ResultColumns.Describe(table));
        object? Value(DataRow row, string[] names) => ResultColumns.Find(table, names) is { } column && !row.IsNull(column) ? row[column] : null;
        string Text(DataRow row, params string[] names) => Convert.ToString(Value(row, names), CultureInfo.InvariantCulture)?.Trim() ?? "";
        long? Number(DataRow row, params string[] names) => long.TryParse(Text(row, names), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : null;
        int? Integer(DataRow row, params string[] names) => Number(row, names) is long n && n >= int.MinValue && n <= int.MaxValue ? (int)n : null;
        var result = new List<ActivityProcess>();
        foreach (DataRow row in table.Rows)
        {
            if (Integer(row, "spid") is not int spid || spid <= 0) continue;
            string login = Text(row, "loginame");
            if (login.Length == 0) login = Text(row, "suser_name", "Login");
            result.Add(new(spid, Text(row, "status"), login, Text(row, "hostname", "Host"), Text(row, "program_name", "Application"),
                Text(row, "dbname", "DBID"), Text(row, "cmd", "Command"), Number(row, "cpu", "CPUTime") ?? 0, Number(row, "physical_io", "PhysicalReads") ?? 0,
                Number(row, "memusage", "MemUsageKB") ?? 0, Integer(row, "blocked", "BlockingSPID") ?? 0, Number(row, "time_blocked", "SecondsWaiting") ?? 0,
                Text(row, "tran_name", "TransactionName"), Integer(row, "suid", "ServerUserID")));
        }
        // Parallel workers may repeat a SPID; prefer an external blocking edge over a worker self-reference.
        return result.GroupBy(p => p.Spid).Select(g => g.OrderByDescending(p => p.BlockedBy > 0 && p.BlockedBy != p.Spid).First()).ToList();
    }

    public static DataTable? PickProcessTable(IReadOnlyList<DataTable> tables) =>
        tables.FirstOrDefault(table => ResultColumns.Find(table, "spid") != null);

    public static int? ParseIdentity(DataTable table)
    {
        if (table.Rows.Count == 0 || table.Columns.Count == 0) return null;
        var column = ResultColumns.Find(table, "monitor_spid") ?? table.Columns[0];
        return int.TryParse(Convert.ToString(table.Rows[0][column], CultureInfo.InvariantCulture), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int spid) && spid > 0 ? spid : null;
    }

    public static string ParseSqlText(DataTable table)
    {
        var column = ResultColumns.Find(table, "SQLText");
        return column == null ? "" : string.Concat(table.Rows.Cast<DataRow>().Select(r => r.IsNull(column) ? "" : Convert.ToString(r[column], CultureInfo.InvariantCulture)));
    }

    public static IReadOnlyList<BlockingNode> BuildTree(IEnumerable<ActivityProcess> processes)
    {
        var nodes = processes.GroupBy(p => p.Spid).ToDictionary(g => g.Key, g => new BlockingNode(g.First()));
        foreach (var node in nodes.Values)
            if (nodes.TryGetValue(node.Process.BlockedBy, out var parent)) node.Parent = parent;
        var done = new HashSet<BlockingNode>();
        foreach (var start in nodes.Values.OrderBy(n => n.Process.Spid))
        {
            var path = new List<BlockingNode>();
            var positions = new Dictionary<BlockingNode, int>();
            BlockingNode? node = start;
            while (node != null && !done.Contains(node))
            {
                if (positions.TryGetValue(node, out int at))
                {
                    var cycle = path.Skip(at).ToList();
                    foreach (var member in cycle) member.InCycle = true;
                    // Break one edge, retaining every process exactly once and marking every cycle member.
                    cycle.MinBy(n => n.Process.Spid)!.Parent = null;
                    break;
                }
                positions[node] = path.Count; path.Add(node); node = node.Parent;
            }
            done.UnionWith(path);
        }
        foreach (var node in nodes.Values) node.Parent?.Children.Add(node);
        return nodes.Values.Where(n => n.Parent == null).OrderBy(n => n.Process.Spid).ToList();
    }

    public static IReadOnlyList<(BlockingNode Node, int Depth)> Flatten(IReadOnlyList<BlockingNode> roots,
        Comparison<ActivityProcess>? compare = null, string filter = "")
    {
        compare ??= (a, b) => a.Spid.CompareTo(b.Spid);
        IEnumerable<BlockingNode> Sorted(IEnumerable<BlockingNode> nodes) => nodes.OrderBy(n => n.Process, Comparer<ActivityProcess>.Create(compare));
        var rows = new List<(BlockingNode Node, int Depth)>();
        var pending = new Stack<(BlockingNode Node, int Depth)>(Sorted(roots).Reverse().Select(n => (n, 0)));
        while (pending.TryPop(out var item))
        {
            rows.Add(item);
            foreach (var child in Sorted(item.Node.Children).Reverse()) pending.Push((child, item.Depth + 1));
        }
        if (string.IsNullOrWhiteSpace(filter)) return rows;
        var visible = new HashSet<BlockingNode>();
        foreach (var (node, _) in rows.AsEnumerable().Reverse())
        {
            if (node.Process.SearchText.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)) visible.Add(node);
            if (visible.Contains(node) && node.Parent != null) visible.Add(node.Parent);
        }
        return rows.Where(r => visible.Contains(r.Node)).ToList();
    }

    public static string? KillDenied(ActivityProcess process, int? ownSpid, IEnumerable<int>? protectedSpids = null)
    {
        if (ownSpid is not > 0) return "The monitor's own SPID is unknown; killing is disabled.";
        if (process.Spid <= 0) return "Invalid SPID.";
        if (process.Spid == ownSpid || protectedSpids?.Contains(process.Spid) == true) return "This is one of this window's own sessions.";
        if (process.Suid is null or <= 0 || string.IsNullOrWhiteSpace(process.Login)) return "System processes and processes without a login cannot be killed.";
        return null;
    }

    public static bool ConfirmsSpid(int spid, string? typed) => spid > 0 && typed == Spid(spid);
    public static bool SameIdentity(ActivityProcess before, ActivityProcess now) => before.Spid == now.Spid && before.Suid == now.Suid &&
        before.Login == now.Login && before.Host == now.Host && before.Program == now.Program && before.Command == now.Command;

    public static string KillSql(ActivityProcess process, int? ownSpid, IEnumerable<int>? protectedSpids = null)
    {
        if (KillDenied(process, ownSpid, protectedSpids) is { } reason) throw new InvalidOperationException(reason);
        string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        // Repeat the safety/identity checks on the server immediately before KILL. No free-form SQL is accepted.
        return "if exists (select 1 from master..sysprocesses where spid = " + Spid(process.Spid) +
            " and spid <> @@spid and suid > 0 and suid = " + process.Suid!.Value.ToString(CultureInfo.InvariantCulture) +
            " and suser_name(suid) = " + Literal(process.Login) + " and hostname = " + Literal(process.Host) +
            " and program_name = " + Literal(process.Program) + " and cmd = " + Literal(process.Command) + ")\n" +
            "begin\nkill " + Spid(process.Spid) + "\nend\nelse raiserror 20000 'Process changed or is protected; refresh and try again.'";
    }
}
