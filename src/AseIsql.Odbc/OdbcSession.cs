using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AseIsql.Odbc;

public sealed class OdbcSession : IQueryEngine, ISessionFacts, IDisposable
{
    public const string DriverName = "Adaptive Server Enterprise";
    public const string IqDriverName = "SAP IQ";
    // ASE and Replication Server speak TDS through the SAP ASE driver, which recognizes Replication Server at login;
    // SAP IQ uses its own driver from the IQ client.
    public static string DriverFor(ServerPlatform platform) => platform == ServerPlatform.Iq ? IqDriverName : DriverName;
    // The smallest statement that proves a session answers: Replication Server has no SELECT.
    public static string TestSql(ServerPlatform platform) => platform == ServerPlatform.Rs ? "admin version" : "SELECT 1 AS connection_ok";
    // The program name the server records for every login (sysprocesses.program_name). Fixed by the product: no
    // profile field, setting or caller value reaches it, and it is written last so nothing earlier can replace it.
    public const string ApplicationName = "DBMON SQL";
    private readonly OdbcConnection connection;
    private readonly ConnectionProfile profile;
    private string password;
    private string identityPassword;
    private string database;
    private bool connectionIssue;
    private int? spid;
    private string? login, charset;
    private readonly object messagesGate = new();
    private QueryOutput? activeOutput;
    private bool suppressMessages;
    private readonly List<QueryMessage> loginMessages = [];
    private readonly DiagnosticMessages diagnostics = new();
    public ServerPlatform Platform => profile.Platform;
    public string Name => $"{profile.Platform.Tag()} · {(profile.ServerAlias.Length == 0 ? profile.Server : profile.ServerAlias)}:{profile.Port}" + (database.Length == 0 ? "" : " · " + database);
    public bool IsOpen => !connectionIssue && connection.State == ConnectionState.Open;
    public SessionFacts Facts => new(database, spid, login, charset, profile.Platform);
    public IReadOnlyList<QueryMessage> LoginMessages { get { lock (messagesGate) return loginMessages.ToArray(); } }

    private OdbcSession(ConnectionProfile profile, string password, string identityPassword)
    {
        this.profile = profile; this.password = password; this.identityPassword = identityPassword; database = profile.Database;
        connection = new OdbcConnection(BuildConnectionString(profile, password, identityPassword));
        if (profile.Platform == ServerPlatform.Iq) connection.ConnectionTimeout = profile.LoginTimeout;
        connection.InfoMessage += (_, args) => {
            lock (messagesGate)
            {
                if (suppressMessages) return;
                foreach (OdbcError error in args.Errors)
                {
                    var message = diagnostics.Read(error.NativeError, error.SQLState, error.Message, this.password, this.identityPassword);
                    // Nothing but a transport frame: ASE separates its trailing blocks with these, and a row that
                    // shows no server text is noise in Messages.
                    if (message.Text.Length == 0) continue;
                    var target = activeOutput?.Messages ?? loginMessages;
                    if (target.Count >= 500) { target.RemoveAt(0); if (activeOutput != null) activeOutput.OmittedMessages++; }
                    target.Add(message);
                }
            }
        };
    }

    public static OdbcSession Open(ConnectionProfile profile, string password, string identityPassword = "")
    {
        profile.Validate();
        var session = new OdbcSession(profile, password, identityPassword);
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            session.connection.Open();
            // Replication Server has no current database; the ASE driver is not asked for one there.
            if (profile.Platform != ServerPlatform.Rs) session.database = session.connection.Database ?? "";
            var opened = clock.Elapsed; session.ReadSessionFacts();
            session.OpenTiming = (opened, clock.Elapsed - opened);
            return session;
        }
        catch { session.Dispose(); throw; }
    }
    // Where a login spends its time: the driver's connect and server login, then the session facts probes.
    public (TimeSpan Login, TimeSpan Facts) OpenTiming { get; private set; }

    // In chained mode a select opens a transaction implicitly; these probes then run nothing and the facts stay as
    // they were, so reading the status never changes the user's transaction state.
    private const string FactsProbe = "if @@tranchained = 0 select /* dbmon_execio */ @@spid, db_name(), suser_name(), @@client_csname";
    private const string BasicFactsProbe = "if @@tranchained = 0 select /* dbmon_execio */ @@spid, db_name(), suser_name()";
    private const string DatabaseProbe = "if @@tranchained = 0 select /* dbmon_execio */ db_name()";
    // IQ runs ODBC in autocommit, so a probe never leaves a transaction open there.
    private const string IqFactsProbe = "select connection_property('Number'), db_name(), current user, connection_property('CharSet')";
    private static readonly Regex UseStatement = new(@"\buse\s+\S", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    // A match only costs one probe; comments or strings that mention "use" are harmless false positives.
    public static bool MayChangeDatabase(string sql) => UseStatement.IsMatch(sql);

    // Session identity for the status bar. A failure never blocks the login: the facts stay unknown.
    private void ReadSessionFacts()
    {
        // Replication Server answers only RCL; its session facts stay unknown rather than probed with SQL.
        if (profile.Platform == ServerPlatform.Rs) return;
        foreach (string probe in profile.Platform == ServerPlatform.Iq ? new[] { IqFactsProbe } : new[] { FactsProbe, BasicFactsProbe })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = probe; command.CommandTimeout = 10;
                using var reader = command.ExecuteReader();
                if (reader.FieldCount == 0 || !reader.Read()) return;
                spid = reader.IsDBNull(0) ? null : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                if (!reader.IsDBNull(1) && Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } name) database = name;
                login = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture)?.Trim();
                charset = reader.FieldCount > 3 && !reader.IsDBNull(3) ? Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture)?.Trim() : null;
                return;
            }
            catch (Exception ex) when (ex is OdbcException or InvalidCastException or FormatException or OverflowException or InvalidOperationException) { }
        }
    }

    private void RefreshDatabase()
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = DatabaseProbe; command.CommandTimeout = 10;
            if (Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } name) database = name;
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { }
    }

    public static string BuildConnectionString(ConnectionProfile profile, string password, string identityPassword = "")
    {
        profile.Validate();
        if (identityPassword.Length != 0 && (profile.Platform != ServerPlatform.Iq || profile.IqIdentityFile.Length == 0))
            throw new ArgumentException("Select an IQ client identity file before entering its password.");
        if (profile.UseTls && string.IsNullOrWhiteSpace(profile.TrustedFile))
            throw new ArgumentException("TLS is enabled: select the server CA certificate file in TrustedFile. For a TCP server without TLS, clear Encrypt data (TLS); Encrypt password is independent.");
        if (profile.Platform == ServerPlatform.Iq) return BuildIqConnectionString(profile, password, identityPassword);
        var builder = new OdbcConnectionStringBuilder { Driver = DriverName };
        builder["Server"] = profile.Server; builder["Port"] = profile.Port;
        if (profile.Platform == ServerPlatform.Ase) builder["Database"] = profile.Database;
        builder["UID"] = profile.User; builder["PWD"] = password;
        builder["LoginTimeOut"] = profile.LoginTimeout; builder["EncryptPassword"] = profile.EncryptPassword ? 1 : 0;
        builder["Encryption"] = profile.UseTls ? "ssl" : "none";
        if (profile.UseTls && profile.TrustedFile.Length != 0) builder["TrustedFile"] = profile.TrustedFile;
        builder["CharSet"] = profile.EffectiveCharset;
        builder["Language"] = profile.Language;
        if (profile.ClientHostname.Length != 0) builder["ClientHostName"] = profile.ClientHostname;
        builder["ApplicationName"] = ApplicationName;
        return builder.ConnectionString;
    }

    // SAP IQ (SQL Anywhere family) parameters: Host carries host:port, ServerName and DatabaseName are optional.
    private static string BuildIqConnectionString(ConnectionProfile profile, string password, string identityPassword)
    {
        var builder = new OdbcConnectionStringBuilder { Driver = IqDriverName };
        string encryption = "";
        builder["UID"] = profile.User; builder["PWD"] = password;
        builder["Host"] = profile.Server + ":" + profile.Port.ToString(CultureInfo.InvariantCulture);
        if (profile.IqServerName.Length != 0) builder["ServerName"] = profile.IqServerName;
        if (profile.Database.Length != 0) builder["DatabaseName"] = profile.Database;
        if (!profile.EffectiveCharset.Equals("ServerDefault", StringComparison.OrdinalIgnoreCase)) builder["CharSet"] = profile.EffectiveCharset;
        if (profile.UseTls)
        {
            ValidateIqTlsValue(profile.TrustedFile, "CA file path");
            string tls = "TLS(TLS_TYPE=RSA;TRUSTED_CERTIFICATES=" + profile.TrustedFile;
            if (profile.IqIdentityFile.Length != 0)
            {
                ValidateIqTlsValue(profile.IqIdentityFile, "client identity file path");
                if (identityPassword.Length == 0) throw new ArgumentException("Enter the IQ client identity password. It is separate from the database login password.");
                ValidateIqTlsValue(identityPassword, "client identity password");
                tls += ";IDENTITY=" + profile.IqIdentityFile + ";IDENTITY_PASSWORD=" + identityPassword;
            }
            if (profile.IqCertificateName.Length != 0)
            {
                ValidateIqTlsValue(profile.IqCertificateName, "expected certificate name");
                tls += ";CERTIFICATE_NAME=" + profile.IqCertificateName;
            }
            encryption = ";Encryption=" + tls + ")";
        }
        builder["AppInfo"] = "APPNAME=" + ApplicationName;
        // IQ parses its parenthesized TLS options itself. OdbcConnectionStringBuilder wraps a
        // semicolon-containing value in braces, which IQ rejects with native error -832.
        // Only append this validated clause; all ordinary ODBC values still use the builder.
        return builder.ConnectionString + encryption;
    }

    // Reject ambiguous values in IQ's unbraced, nested TLS grammar
    // rather than interpreting a password or filename as an additional security option.
    private static void ValidateIqTlsValue(string value, string field)
    {
        if (value.IndexOfAny([';', '(', ')', '{', '}', '\'', '"']) >= 0 || value.Any(char.IsControl) || value != value.Trim())
            throw new ArgumentException("The IQ " + field + " cannot contain semicolons, parentheses, braces, quotes, control characters or leading/trailing whitespace. Its value was not sent to the driver.");
    }

    public static string DescribeConnectionFailure(Exception exception, string password, string identityPassword = "")
    {
        if (exception is not OdbcException odbc) return Redact(exception.Message, password, identityPassword);
        var errors = odbc.Errors.Cast<OdbcError>().ToArray();
        var reader = new DiagnosticMessages();
        string details = string.Join("\n", errors.DistinctBy(e => (e.SQLState, e.NativeError, e.Message))
            .Select(e => (Error: e, Text: reader.Read(e.NativeError, e.SQLState, e.Message, password, identityPassword).Text))
            .Where(record => record.Text.Length != 0)
            .Select(record => $"[{record.Error.SQLState} / {record.Error.NativeError}] {record.Text}"));
        bool certificateList = errors.Any(e => IsCertificateAuthorityFailure(e.SQLState, e.Message));
        return (certificateList ? "Unable to load the TLS CA certificate list. Check TrustedFile and its format. No unencrypted retry was attempted.\n\n" : "") +
            (details.Length == 0 ? Redact(exception.Message, password, identityPassword) : details);
    }

    public static bool IsCertificateAuthorityFailure(string state, string message) =>
        state.Equals("SC001", StringComparison.OrdinalIgnoreCase) &&
        message.Contains("setting the certificate authority list", StringComparison.OrdinalIgnoreCase);

    // Set by the window when a SET option makes ASE emit messages after the last row (the execution times, the
    // total writes). Those reach the driver, but System.Data.Odbc hands over diagnostics only on calls that
    // report them, and the final SQLMoreResults reports no data, so the block is never raised as an InfoMessage. This
    // is a no-op statement run right after the batches, inside the window where messages still belong to this run,
    // purely to give the driver that call. Null when no option needs it, which is the usual case.
    public string? TrailingFlush { get; set; }
    // Set when "set plan for show_execio_xml to message on" is active on this session: after the batches finish,
    // showplan_in_xml(0) returns the executed plans as a result set so per-table logical and physical reads arrive
    // without depending on ODBC's diagnostic channel.
    public bool CaptureExecutionIo { get; set; }

    public QueryOutput Execute(string sql, CancellationToken cancellation = default) => Execute(sql, cancellation, userRun: true);

    public QueryOutput Execute(string sql, CancellationToken cancellation, bool userRun)
    {
        cancellation.ThrowIfCancellationRequested();
        var output = new QueryOutput();
        if (!IsOpen) { output.Error = "The " + profile.Platform.Tag() + " session is disconnected."; return output; }
        if (sql.Length > 1024 * 1024) { output.Error = "Local limit: 1 MiB of SQL text per execution."; return output; }
        lock (messagesGate) { activeOutput = output; suppressMessages = false; }
        try
        {
            var batches = SqlScript.Split(sql, cancellation: cancellation);
            if (batches.Count == 0) { output.Error = "No SQL to execute."; return output; }
            if (batches.Count > 256) { output.Error = "Local limit: 256 GO batches per execution."; return output; }
            var budget = new ResultBudget(profile.MaxRows);
            var ioPlans = new List<ExecutionIoPlan>();
            foreach (string batch in batches)
            {
                cancellation.ThrowIfCancellationRequested();
                if (userRun && profile.Platform == ServerPlatform.Ase && CaptureExecutionIo && !cancellation.IsCancellationRequested)
                    ArmExecutionIo(cancellation);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = batch; command.CommandTimeout = profile.QueryTimeout;
                    // Cancel is best effort in ODBC. The UI requests it from a worker
                    // thread and keeps ownership until the actual call returns.
                    using var registration = cancellation.Register(() => {
                        try { command.Cancel(); } catch (Exception ex) when (ex is OdbcException or InvalidOperationException or ObjectDisposedException) { }
                    });
                    using var reader = command.ExecuteReader();
                    try { ResultReader.Read(reader, output, budget, cancellation); }
                    catch (Exception ex) when (ex is ResultLimitException or OperationCanceledException)
                    {
                        try { command.Cancel(); } catch (OdbcException cancelError) { output.Messages.Add(new QueryMessage(0, "ODBC", "Cancellation was not confirmed. Open the original diagnostic for details.", RedactSecrets(cancelError.Message))); }
                        throw;
                    }
                    if (reader.RecordsAffected >= 0) output.Messages.Add(new QueryMessage(0, profile.Platform.Tag(), $"Rows affected: {reader.RecordsAffected}"));
                }
                if (userRun && profile.Platform == ServerPlatform.Ase && CaptureExecutionIo && !cancellation.IsCancellationRequested)
                    CollectExecutionIo(output, ioPlans, batch, cancellation);
                if (profile.Platform == ServerPlatform.Ase) database = connection.Database;
            }
            if (ioPlans.Any(p => p.Scans.Count > 0))
                output.Tables.Add(ExecutionIo.ToTable(ioPlans));
            // A failure here is never the run's failure: the results are already read and complete.
            if (userRun && TrailingFlush is { Length: > 0 } flush && !cancellation.IsCancellationRequested)
            {
                try
                {
                    using var flushCommand = connection.CreateCommand();
                    flushCommand.CommandText = flush; flushCommand.CommandTimeout = 10;
                    flushCommand.ExecuteNonQuery();
                }
                catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { }
            }
        }
        catch (OdbcException ex)
        {
            lock (messagesGate)
                foreach (OdbcError error in ex.Errors) output.Messages.Add(diagnostics.Read(error.NativeError, error.SQLState, error.Message, password, identityPassword));
            output.ErrorSource = "ODBC";
            if (ex.Errors.Cast<OdbcError>().Any(e => e.SQLState.StartsWith("08", StringComparison.Ordinal))) connectionIssue = true;
            // Native records above own the readable server facts. Do not append
            // the exception's framed aggregate as another display message.
            output.ErrorRawText = RedactSecrets(ex.Message);
            output.Error = cancellation.IsCancellationRequested ? "Cancellation requested. Review server messages and verify the session state." : "Query failed. Review server messages or open the original diagnostic for details.";
        }
        catch (Exception ex) when (ex is FormatException or ResultLimitException or InvalidOperationException or OverflowException or NotSupportedException or InvalidCastException or ArgumentException)
        { output.Error = RedactSecrets(ex.Message); }
        catch (OperationCanceledException) { output.Error = "Cancellation requested. Received results are partial; verify the session before continuing."; }
        finally
        {
            lock (messagesGate) { activeOutput = null; suppressMessages = true; }
            // A USE in any batch, even one followed by a failure, can move the session; read where it stands now.
            if (profile.Platform == ServerPlatform.Ase && !connectionIssue && !cancellation.IsCancellationRequested && MayChangeDatabase(sql))
                RefreshDatabase();
            lock (messagesGate) suppressMessages = false;
        }
        return output;
    }

    private QueryOutput RunInternalBatch(string sql, CancellationToken cancellation, int maxRows = 64)
    {
        var probe = new QueryOutput();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 15;
        using var registration = cancellation.Register(() => {
            try { command.Cancel(); } catch (Exception ex) when (ex is OdbcException or InvalidOperationException or ObjectDisposedException) { }
        });
        using var reader = command.ExecuteReader();
        ResultReader.Read(reader, probe, new ResultBudget(maxRows), cancellation);
        return probe;
    }

    private string ReadPlanXml(string sql, CancellationToken cancellation)
    {
        var probe = RunInternalBatch(sql, cancellation);
        var xml = new System.Text.StringBuilder();
        foreach (var table in probe.Tables)
            foreach (DataRow row in table.Rows)
                for (int i = 0; i < table.Columns.Count; i++)
                    if (row[i] is string text && text.Length > 0)
                        xml.AppendLine(text);
        return xml.ToString().Trim();
    }

    private void ArmExecutionIo(CancellationToken cancellation)
    {
        lock (messagesGate) suppressMessages = true;
        try
        {
            RunInternalBatch("set textsize 16777216", cancellation);
            RunInternalBatch("set plan for show_execio_xml to message on", cancellation);
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException or FormatException or InvalidCastException or OverflowException or ResultLimitException) { }
        finally
        {
            lock (messagesGate) suppressMessages = false;
        }
    }

    private void CollectExecutionIo(QueryOutput output, List<ExecutionIoPlan> ioPlans, string batch, CancellationToken cancellation)
    {
        string rawXml;
        lock (messagesGate) suppressMessages = true;
        try
        {
            rawXml = ReadPlanXml("select showplan_in_xml(0)", cancellation);
            if (rawXml.Length == 0)
                rawXml = ReadPlanXml("select showplan_in_xml(-1)", cancellation);
            if (rawXml.Length == 0 && TrailingFlush is { Length: > 0 } && SqlRisk.StateChanges(batch).Count == 0)
            {
                RunInternalBatch("set statistics time off", cancellation);
                RunInternalBatch("set textsize 16777216", cancellation);
                RunInternalBatch("set plan for show_execio_xml to message on", cancellation);
                RunInternalBatch(batch, cancellation, profile.MaxRows);
                rawXml = ReadPlanXml("select showplan_in_xml(0)", cancellation);
                if (rawXml.Length == 0)
                    rawXml = ReadPlanXml("select showplan_in_xml(-1)", cancellation);
                RunInternalBatch("set statistics time on", cancellation);
            }
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException or FormatException or InvalidCastException or OverflowException or ResultLimitException)
        {
            output.Messages.Add(new QueryMessage(0, "CLIENT", "Statistics IO plan read failed: " + RedactSecrets(ex.Message)));
            return;
        }
        finally
        {
            lock (messagesGate) suppressMessages = false;
        }
        var report = ExecutionIo.Parse(rawXml);
        if (report.Plans.Count == 0)
        {
            output.Messages.Add(new QueryMessage(0, "CLIENT",
                rawXml.Length == 0
                    ? "Statistics IO: showplan_in_xml(0) and (-1) returned an empty buffer."
                    : $"Statistics IO: no user execution plan found in {rawXml.Length} chars ({report.Unreadable} unreadable).",
                rawXml.Length == 0 ? null : rawXml));
            return;
        }
        foreach (var plan in report.Plans)
        {
            ioPlans.Add(plan);
            foreach (string line in ExecutionIo.Summary(plan))
                output.Messages.Add(new QueryMessage(3615, profile.Platform.Tag(), line, plan.Xml));
            if (plan.MissingStatistics.Count > 0)
                output.Messages.Add(new QueryMessage(0, profile.Platform.Tag(), "Missing statistics · " + string.Join(", ", plan.MissingStatistics), plan.Xml));
        }
        if (report.Unreadable > 0)
            output.Messages.Add(new QueryMessage(0, "CLIENT", $"Statistics IO: {report.Unreadable} plan block(s) could not be parsed.", rawXml));
    }

    public static string Redact(string text, string secret, string identityPassword = "")
    {
        if (identityPassword.Length == 0)
            return secret.Length == 0 ? text : text.Replace(secret.Replace("}", "}}", StringComparison.Ordinal), "[password]", StringComparison.Ordinal).Replace(secret, "[password]", StringComparison.Ordinal);
        var secrets = new[] { secret, identityPassword }.Where(s => s.Length != 0)
            .SelectMany(s => new[] { s, s.Replace("}", "}}", StringComparison.Ordinal) }).Distinct()
            .OrderByDescending(s => s.Length).ToArray();
        if (secrets.Length == 0) return text;
        // One pass preserves overlapping secrets and never rewrites an already inserted placeholder.
        return Regex.Replace(text, string.Join("|", secrets.Select(Regex.Escape)), "[password]", RegexOptions.CultureInvariant);
    }

    private string RedactSecrets(string text) => Redact(text, password, identityPassword);
    public void Dispose() { try { connection.Dispose(); } finally { password = identityPassword = ""; } }
}
