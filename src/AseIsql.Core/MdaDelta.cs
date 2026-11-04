using System.Data;
using System.Text.RegularExpressions;

namespace AseIsql;

// Which columns of an MDA result identify a row and which ones are cumulative counters to subtract.
public sealed record MdaSnapshot(string? Table, IReadOnlyList<string> Keys, IReadOnlyList<string> Counters);

// Deltas between two runs of the same ASE monitoring query (Repeat). MDA tables such as monSysWaits or
// monOpenObjectActivity report counters that only grow while the server runs, so the useful number is how much
// each one grew since the previous run. Rows are matched by their identifying columns (WaitEventID, DBID+ObjectID+
// IndexID, SPID+KPID, ...); the other numeric columns are subtracted and everything that is not numeric is ignored.
public static class MdaDelta
{
    public const string ResultHeader = "MDA deltas";
    public const string StatusColumn = "delta_status";
    public const string New = "new", Gone = "gone", Reset = "reset";

    // Tables whose numeric columns are (mostly) counters since server start or since the object/process appeared.
    // Gauges and text tables (monProcess, monLocks, monProcessSQLText, ...) are deliberately absent.
    static readonly HashSet<string> CumulativeTables = new(StringComparer.OrdinalIgnoreCase) {
        "monSysWaits", "monProcessWaits", "monOpenObjectActivity", "monProcessActivity", "monSysStatement", "monProcessStatement",
        "monOpenDatabases", "monOpenPartitionActivity", "monDeviceIO", "monIOQueue", "monEngine", "monDataCache", "monCachePool",
        "monProcedureCache", "monProcedureCacheModuleUsage", "monProcessObject", "monProcessNetIO", "monNetworkIO", "monCachedProcedures",
        "monCachedStatement", "monStatementCache", "monSysLoad", "monThread", "monTask", "monWorkQueue", "monLicense" };
    static readonly Regex MonitorName = new(@"\bmon[A-Z]\w*\b", RegexOptions.CultureInvariant);

    // Row identity, in priority order. Name keys only count when no ID key is present (a query that selects DBName
    // and ObjectName instead of DBID and ObjectID), since names next to IDs are just descriptions of the same row.
    static readonly string[] IdKeys = ["InstanceID", "EngineNumber", "ThreadID", "TaskID", "SPID", "KPID", "DBID", "ObjectID", "PartitionID",
        "IndexID", "WaitEventID", "BatchID", "ContextID", "LineNumber", "ProcedureID", "PlanID", "SSQLID", "StatementNumber", "CacheID",
        "CacheName", "IOBufferSize", "LogicalName", "IOType", "ModuleID", "AllocatorID", "QueueName"];
    static readonly string[] NameKeys = ["DBName", "ObjectName", "PartitionName", "IndexName", "ProcedureName", "OwnerName", "Login"];
    // Numeric columns that describe rather than count: never subtracted.
    static readonly HashSet<string> NotCounters = new(StringComparer.OrdinalIgnoreCase) {
        "Priority", "ProcNestLevel", "NetworkPacketSize", "ErrorStatus", "HashKey", "MemUsageKB", "AllocatedKB", "Connections",
        "AffinitiedToCPU", "OSPID", "Status", "Severity" };

    // The cumulative MDA table named in the code of the SQL (strings and comments ignored), or null.
    public static string? MonitorTable(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        foreach (Match match in MonitorName.Matches(SqlRisk.CodeOnly(sql)))
            if (CumulativeTables.TryGetValue(match.Value, out var name)) return name;
        return null;
    }

    // Safe detection: the SQL must read a cumulative MDA table (when the SQL is known) and the result must carry at
    // least one identifying column and one numeric counter. Anything else is not treated as an MDA snapshot.
    public static MdaSnapshot? Detect(DataTable table, string? sql = null)
    {
        string? source = MonitorTable(sql);
        if (sql != null && source == null) return null;
        var columns = table.Columns.Cast<DataColumn>().ToList();
        DataColumn? Find(string name) => columns.FirstOrDefault(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase));
        var keys = IdKeys.Select(Find).OfType<DataColumn>().ToList();
        if (keys.Count == 0) keys = NameKeys.Select(Find).OfType<DataColumn>().ToList();
        if (keys.Count == 0) return null;
        var counters = columns.Where(c => !keys.Contains(c) && IsCounter(c, table)).Select(c => c.ColumnName).ToList();
        return counters.Count == 0 ? null : new MdaSnapshot(source, keys.Select(c => c.ColumnName).ToList(), counters);
    }

    // next - prev per row key. Keys only in next are "new" (their counters started at zero, so the delta is the value);
    // keys only in prev are "gone" with NULL deltas; a counter that went down was reset (process/object reopened,
    // counters cleared) and shows its new value. A NULL on either side gives a NULL delta. Rows sharing a key are
    // summed, which keeps the delta right for the group when the query left out part of the key.
    public static DataTable Compute(DataTable prev, DataTable next, MdaSnapshot? shape = null)
    {
        shape ??= Detect(next) ?? throw new ArgumentException("The result has no MDA row key (WaitEventID, ObjectID, SPID, ...) or no numeric counters.");
        var missing = shape.Keys.Where(k => !prev.Columns.Contains(k) || !next.Columns.Contains(k)).ToList();
        if (missing.Count > 0) throw new ArgumentException("Both runs must return the key column(s) " + string.Join(", ", missing) + ".");
        var counters = shape.Counters.Where(c => next.Columns.Contains(c) && prev.Columns.Contains(c)
            && IsCounter(next.Columns[c]!, next) && IsCounter(prev.Columns[c]!, prev)).ToList();
        var before = Index(prev, shape.Keys, counters, out _);
        var after = Index(next, shape.Keys, counters, out var order);

        var result = new DataTable(ResultHeader);
        result.ExtendedProperties["ResultHeader"] = ResultHeader;
        if (shape.Table != null) result.ExtendedProperties["MdaTable"] = shape.Table;
        foreach (var key in shape.Keys) result.Columns.Add(next.Columns[key]!.ColumnName, KeyType(prev.Columns[key]!, next.Columns[key]!));
        result.Columns.Add(StatusColumn, typeof(string));
        foreach (var counter in counters) result.Columns.Add(next.Columns[counter]!.ColumnName, CounterType(prev.Columns[counter]!, next.Columns[counter]!));

        void Emit(object[] key, string status, Func<int, object> value)
        {
            var row = result.NewRow();
            for (int k = 0; k < key.Length; k++) row[k] = key[k];
            row[key.Length] = status;
            for (int c = 0; c < counters.Count; c++) row[key.Length + 1 + c] = value(c);
            result.Rows.Add(row);
        }
        foreach (var key in order)
        {
            var now = after[key];
            if (!before.TryGetValue(key, out var was)) { Emit(key, New, c => Box(now[c], result.Columns[key.Length + 1 + c].DataType)); continue; }
            bool reset = false;
            var deltas = new object[counters.Count];
            for (int c = 0; c < counters.Count; c++)
            {
                if (now[c] is not decimal n || was[c] is not decimal p) { deltas[c] = DBNull.Value; continue; }
                if (n < p) reset = true;
                deltas[c] = n - p;
            }
            // A reset restarts every counter of the row, so all of them show their value since the reset.
            Emit(key, reset ? Reset : "", c => Box(reset ? now[c] : deltas[c], result.Columns[key.Length + 1 + c].DataType));
        }
        foreach (var key in before.Keys.Where(k => !after.ContainsKey(k))) Emit(key, Gone, _ => DBNull.Value);
        return result;
    }

    // The counter whose deltas add up to the most (absolute), the default series for a top-N chart; null when none moved.
    public static string? ChartColumn(DataTable delta)
    {
        int first = delta.Columns.IndexOf(StatusColumn);
        if (first < 0) return null;
        string? best = null; decimal most = 0;
        for (int c = first + 1; c < delta.Columns.Count; c++)
        {
            decimal total = 0;
            foreach (DataRow row in delta.Rows) if (row[c] is not DBNull) total += Math.Abs(Convert.ToDecimal(row[c]));
            if (total > most) { most = total; best = delta.Columns[c].ColumnName; }
        }
        return best;
    }

    static Dictionary<object[], object[]> Index(DataTable table, IReadOnlyList<string> keys, IReadOnlyList<string> counters, out List<object[]> order)
    {
        var keyOrdinals = keys.Select(k => table.Columns.IndexOf(k)).ToArray();
        var counterOrdinals = counters.Select(c => table.Columns.IndexOf(c)).ToArray();
        var rows = new Dictionary<object[], object[]>(KeyComparer.Instance);
        order = [];
        foreach (DataRow row in table.Rows)
        {
            var key = keyOrdinals.Select(k => row[k]).ToArray();
            var values = counterOrdinals.Select(c => Number(row[c]) is decimal d ? (object)d : DBNull.Value).ToArray();
            if (!rows.TryGetValue(key, out var sum)) { rows[key] = values; order.Add(key); continue; }
            for (int c = 0; c < sum.Length; c++) sum[c] = sum[c] is decimal a && values[c] is decimal b ? (object)(a + b) : DBNull.Value;
        }
        return rows;
    }

    static bool IsCounter(DataColumn column, DataTable table)
    {
        if (NotCounters.Contains(column.ColumnName) || column.ColumnName.EndsWith("ID", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsNumericType(column.DataType)) return true;
        // Untyped (object) columns qualify only when every non-NULL value is numeric and at least one is present.
        if (column.DataType != typeof(object)) return false;
        bool any = false;
        foreach (DataRow row in table.Rows)
        {
            var value = row[column];
            if (value is DBNull or null) continue;
            if (!IsNumericType(value.GetType())) return false;
            any = true;
        }
        return any;
    }

    static bool IsNumericType(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Decimal or TypeCode.Single or TypeCode.Double;
    static bool IsFloat(Type type) => type == typeof(float) || type == typeof(double);

    static decimal? Number(object value)
    {
        if (value is DBNull or null || !IsNumericType(value.GetType())) return null;
        if (value is double d && (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 7.9e28)) return null;
        if (value is float f && (float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) > 7.9e28f)) return null;
        return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Integer counters become bigint (ASE int counters wrap near 2^31 and a sum must not); decimals stay decimal; floats double.
    static Type CounterType(DataColumn a, DataColumn b) =>
        IsFloat(a.DataType) || IsFloat(b.DataType) ? typeof(double) :
        a.DataType == typeof(decimal) || b.DataType == typeof(decimal) || a.DataType == typeof(object) || b.DataType == typeof(object) || a.DataType == typeof(ulong) || b.DataType == typeof(ulong) ? typeof(decimal) : typeof(long);
    static Type KeyType(DataColumn a, DataColumn b) => a.DataType == b.DataType ? a.DataType : typeof(object);

    static object Box(object value, Type type) => value is decimal d
        ? type == typeof(long) ? (object)(long)d : type == typeof(double) ? (object)(double)d : d
        : DBNull.Value;

    // Keys compare by value across both runs, so an int 5 from one run and a long 5 from the other are the same row.
    sealed class KeyComparer : IEqualityComparer<object[]>
    {
        public static readonly KeyComparer Instance = new();
        static object Normal(object value) => Number(value) is decimal d ? (object)d : value is string s ? s.TrimEnd() : value;
        public bool Equals(object[]? a, object[]? b) => a != null && b != null && a.Length == b.Length && a.Zip(b).All(p => Normal(p.First).Equals(Normal(p.Second)));
        public int GetHashCode(object[] values)
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(Normal(value));
            return hash.ToHashCode();
        }
    }
}
