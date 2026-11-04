using System.Data;
using System.Data.Common;
using System.Data.Odbc;

namespace AseIsql.Odbc;

// Only the driver boundary is replaceable. OdbcSession still owns execution, diagnostics, cancellation and
// result reading. DbDataReader keeps the existing precision-preserving ResultReader path in both cases.
internal sealed record OdbcDiagnostic(int NativeError, string SQLState, string Message);

internal interface IOdbcConnection : IDisposable
{
    ConnectionState State { get; }
    string Database { get; }
    int ConnectionTimeout { get; set; }
    event Action<IReadOnlyList<OdbcDiagnostic>>? InfoMessage;
    void Open();
    IOdbcCommand CreateCommand();
    // Null means this is not a driver failure. Preserve native exceptions, including errors raised by a reader.
    IReadOnlyList<OdbcDiagnostic>? GetDiagnostics(Exception exception);
}

internal interface IOdbcCommand : IDisposable
{
    string CommandText { get; set; }
    int CommandTimeout { get; set; }
    DbDataReader ExecuteReader();
    object? ExecuteScalar();
    int ExecuteNonQuery();
    void Cancel();
}

internal sealed class NativeOdbcConnection : IOdbcConnection
{
    private readonly OdbcConnection connection;
    public NativeOdbcConnection(string connectionString)
    {
        connection = new OdbcConnection(connectionString);
        connection.InfoMessage += (_, args) => InfoMessage?.Invoke(ReadDiagnostics(args.Errors));
    }

    public ConnectionState State => connection.State;
    public string Database => connection.Database;
    public int ConnectionTimeout { get => connection.ConnectionTimeout; set => connection.ConnectionTimeout = value; }
    public event Action<IReadOnlyList<OdbcDiagnostic>>? InfoMessage;
    public void Open() => connection.Open();
    public IOdbcCommand CreateCommand() => new NativeOdbcCommand(connection.CreateCommand());
    public IReadOnlyList<OdbcDiagnostic>? GetDiagnostics(Exception exception) =>
        exception is OdbcException odbc ? ReadDiagnostics(odbc.Errors) : null;
    private static OdbcDiagnostic[] ReadDiagnostics(OdbcErrorCollection errors) => errors.Cast<OdbcError>()
        .Select(error => new OdbcDiagnostic(error.NativeError, error.SQLState, error.Message)).ToArray();
    public void Dispose() => connection.Dispose();

    private sealed class NativeOdbcCommand(OdbcCommand command) : IOdbcCommand
    {
        public string CommandText { get => command.CommandText; set => command.CommandText = value; }
        public int CommandTimeout { get => command.CommandTimeout; set => command.CommandTimeout = value; }
        public DbDataReader ExecuteReader() => command.ExecuteReader();
        public object? ExecuteScalar() => command.ExecuteScalar();
        public int ExecuteNonQuery() => command.ExecuteNonQuery();
        public void Cancel() => command.Cancel();
        public void Dispose() => command.Dispose();
    }
}
