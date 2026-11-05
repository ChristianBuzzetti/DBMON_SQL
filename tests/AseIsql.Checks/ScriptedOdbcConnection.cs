using AseIsql.Odbc;
using System.Data;
using System.Data.Common;

// Strict FIFO script: unexpected SQL fails the check immediately. No native ODBC object is constructed.
sealed class ScriptedOdbcConnection : IOdbcConnection
{
    public const string FactsSql = "if @@tranchained = 0 select /* dbmon_execio */ @@spid, db_name(), suser_name(), @@client_csname";
    private readonly Queue<Step> steps = new();
    public readonly List<Command> Executed = [];
    public readonly List<OdbcDiagnostic> LoginDiagnostics = [];
    public ConnectionState State { get; private set; }
    public string Database => "fixture_db";
    public int ConnectionTimeout { get; set; }
    public bool Disposed { get; private set; }
    public int Pending => steps.Count;
    public event Action<IReadOnlyList<OdbcDiagnostic>>? InfoMessage;

    public void Open() { State = ConnectionState.Open; Emit(LoginDiagnostics.ToArray()); }
    public IOdbcCommand CreateCommand() => new Command(this);
    public IReadOnlyList<OdbcDiagnostic>? GetDiagnostics(Exception exception) =>
        exception is ScriptedOdbcException scripted ? scripted.Diagnostics : null;
    public void Emit(params OdbcDiagnostic[] diagnostics) => InfoMessage?.Invoke(diagnostics);
    public void Dispose() { Disposed = true; State = ConnectionState.Closed; }

    public Step Add(string sql, params DataTable[] tables)
    {
        var step = new Step(sql, tables.Length == 0 ? [new DataTable()] : tables);
        steps.Enqueue(step); return step;
    }

    public const string FlushSql = "set flushmessage on";

    // ASE sessions send `set flushmessage on` right after connecting, before the facts probe.
    public ScriptedOdbcConnection WithFacts(bool flush = true)
    {
        if (flush) Add(FlushSql);
        var facts = new DataTable();
        facts.Columns.Add("spid", typeof(int)); facts.Columns.Add("db"); facts.Columns.Add("login"); facts.Columns.Add("charset");
        facts.Rows.Add(73, Database, "fixture_login", "utf8");
        Add(FactsSql, facts); return this;
    }

    public sealed class Step(string sql, DataTable[] tables)
    {
        public string Sql { get; } = sql;
        public DataTable[] Tables { get; } = tables;
        public Action? OnExecute { get; set; }
        public Action<int>? BeforeRead { get; set; }
        public Exception? Error { get; set; }
        public int RecordsAffected { get; set; } = -1;
    }

    public sealed class Command(ScriptedOdbcConnection connection) : IOdbcCommand
    {
        public string CommandText { get; set; } = "";
        public int CommandTimeout { get; set; }
        public int CancelCalls { get; private set; }
        public bool Disposed { get; private set; }
        public DbDataReader? Reader { get; private set; }
        private Step Execute()
        {
            if (Disposed) throw new Exception("Executed a disposed command.");
            if (connection.steps.Count == 0) throw new Exception("Unexpected SQL: " + CommandText);
            var step = connection.steps.Dequeue();
            if (CommandText.Trim() != step.Sql) throw new Exception($"Expected SQL '{step.Sql}', received '{CommandText}'.");
            connection.Executed.Add(this);
            step.OnExecute?.Invoke();
            if (step.Error is { } error) throw error;
            return step;
        }
        public DbDataReader ExecuteReader()
        {
            var step = Execute();
            return Reader = new ScriptedReader(step);
        }
        public object? ExecuteScalar()
        {
            using var reader = ExecuteReader();
            return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
        }
        public int ExecuteNonQuery() => Execute().RecordsAffected;
        public void Cancel()
        {
            if (Disposed) throw new Exception("Cancelled a disposed command.");
            CancelCalls++;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class ScriptedReader(Step step) : StreamingTestReader(step.Tables)
    {
        private int reads;
        public override bool Read() { step.BeforeRead?.Invoke(++reads); return base.Read(); }
        public override int RecordsAffected => step.RecordsAffected;
    }
}

sealed class ScriptedOdbcException(params OdbcDiagnostic[] diagnostics)
    : Exception(string.Join("\n", diagnostics.Select(diagnostic => diagnostic.Message)))
{
    public IReadOnlyList<OdbcDiagnostic> Diagnostics { get; } = diagnostics;
}
