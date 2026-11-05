#if DBMON_SMOKE
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class App
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int standardHandle);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    private async void StartLiveTestSmoke(string[] args)
    {
        // WinExe has no console by default. Preserve redirected stdout, or attach the launching console.
        IntPtr output = GetStdHandle(-11);
        if ((output == IntPtr.Zero || output == new IntPtr(-1)) && AttachConsole(uint.MaxValue))
        {
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true }); }
            catch (IOException) { } // The JSON report remains available if the launching console disappears.
        }
        // There is deliberately no MainWindow instance: its constructor loads settings and installs persistence.
        int code = await Task.Run(() => MainWindow.RunLiveTestSmokeAsync(args));
        Shutdown(code);
    }
}

public partial class MainWindow
{
    internal static Task<int> RunLiveTestSmokeAsync(string[] args) => new LiveTestRun().RunAsync(args);

    private sealed class LiveTestRefusal(string message) : Exception(message);
    private sealed class LiveTestSkip(string message) : Exception(message);
    private sealed record LiveTestResult(string Check, string Status, string Detail, double ElapsedMs);

    private sealed record LiveTestOptions(string Profile, string[] Allow, int BlockSeconds, int MaxMinutes, bool IncludeSysmon)
    {
        public static LiveTestOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var switches = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key is "--smoke" or "--live-test" or "--include-sysmon")
                {
                    if (!switches.Add(key)) throw new LiveTestRefusal("Duplicate live-test switch.");
                }
                else if (key is "--profile" or "--allow" or "--report" or "--block-seconds" or "--max-minutes")
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(key, args[++i]))
                        throw new LiveTestRefusal("Missing value or duplicate live-test option: " + key);
                }
                else throw new LiveTestRefusal("Unrecognized live-test option; use only the switches documented in docs/building.md.");
            }
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new LiveTestRefusal("Live test requires " + key + ".");
            int Number(string key, int fallback, int max) => !values.TryGetValue(key, out var value) ? fallback
                : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= max
                    ? n : throw new LiveTestRefusal(key + " must be a whole number from 1 to " + max + ".");
            if (!switches.Contains("--smoke") || !switches.Contains("--live-test"))
                throw new LiveTestRefusal("Live test requires both --smoke and --live-test.");
            _ = Required("--report");
            var allow = Required("--allow").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (allow.Length == 0) throw new LiveTestRefusal("--allow must contain at least one non-production server alias.");
            return new(Required("--profile"), allow, Number("--block-seconds", 75, 3600), Number("--max-minutes", 10, 60), switches.Contains("--include-sysmon"));
        }
    }

    private sealed class LiveTestRun
    {
        private const string PlanSql = "select count(*) from master..sysobjects where type = 'U'";
        private const string MdaSql = "select * from master..monOpenObjectActivity";
        private readonly List<LiveTestResult> results = [];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        private readonly CancellationTokenSource deadline = new();
        private SavedLogin? login;
        private OdbcSession? root;
        private string? reportPath;
        private bool reportFailed;
        private int? exitCode;
        private string phase = "preflight";

        private string Safe(string text) => OdbcSession.Redact(text, login?.Password ?? "", login?.IdentityPassword ?? "");
        private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }

        public async Task<int> RunAsync(string[] args)
        {
            try
            {
                // Find the destination even when another argument is invalid, so refusals can still be reported.
                int at = Array.IndexOf(args, "--report");
                if (at >= 0 && at + 1 < args.Length && !string.IsNullOrWhiteSpace(args[at + 1]) && !args[at + 1].StartsWith("--", StringComparison.Ordinal))
                    reportPath = ReportDestination(args[at + 1]);
                var options = LiveTestOptions.Parse(args);
                deadline.CancelAfter(TimeSpan.FromMinutes(options.MaxMinutes));
                List<SavedLogin> saved;
                try { saved = ProfileStore.Load(ProfileStore.PathName); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                { throw new LiveTestRefusal("Unable to read the real saved profiles file (" + ex.GetType().Name + "). Open the app normally to inspect saved logins."); }
                var matches = saved.Where(p => string.Equals(p.Name, options.Profile, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) throw new LiveTestRefusal("--profile must match exactly one SavedLogin.Name (case-sensitive); matches: " + matches.Length + ".");
                var selected = matches[0];
                login = selected;
                string server = login.Profile.ServerAlias.Length == 0 ? login.Profile.Server : login.Profile.ServerAlias;
                if (!options.Allow.Contains(server, StringComparer.OrdinalIgnoreCase)) throw new LiveTestRefusal("The profile's server alias (or server when no alias is saved) is absent from --allow.");
                if (login.Profile.Platform != ServerPlatform.Ase) throw new LiveTestRefusal("Live test requires an ASE profile.");
                if (login.Profile.Production) throw new LiveTestRefusal("Live test refuses a profile marked Production.");
                if (!login.HasPassword) throw new LiveTestRefusal("Please save the password in the profile or run the checks manually in the app; live test never prompts for credentials.");
                try { login.Profile.Validate(); }
                catch (ArgumentException ex) { throw new LiveTestRefusal("Saved profile is invalid: " + Safe(ex.Message)); }
                phase = "running";
                Add("preflight", "PASS", "Authorized saved non-PROD ASE profile: " + login.Name + "; query timeout " + login.Profile.QueryTimeout + " s. Real profiles and health files are read-only.", clock.Elapsed.TotalMilliseconds);
                if (reportFailed) throw new LiveTestRefusal("Cannot write --report; no connection was opened.");

                await CheckSync("connect", token => {
                    root = OpenChecked(() => OdbcSession.Open(selected.Profile, selected.Password!, selected.IdentityPassword ?? ""), token);
                    var output = Send(root, "select @@version, @@servername, @@spid, db_name()", token);
                    var row = SingleRow(output);
                    Require(row.Table.Columns.Count == 4 && Integer(row[2]) > 0, "Connection identity result is incomplete.");
                    return string.Join(" | ", row.ItemArray.Select(ResultFormats.Cell));
                });
                await CheckSync("activity monitor", token => {
                    using var session = Sibling(token);
                    int spid = Spid(session, token);
                    var processes = Processes(session, token);
                    Require(processes.Count > 0 && processes.Any(p => p.Spid == spid), "Activity parsing did not include the monitor's own SPID.");
                    return processes.Count + " processes; own SPID " + spid + ".";
                });
                await CheckSync("explorer master catalog", token => {
                    using var session = Sibling(token);
                    Require(Send(session, DatabasesSql, token).Tables.SelectMany(t => t.Rows.Cast<DataRow>()).Any(r => ResultFormats.Cell(r[0]).Trim().Equals("master", StringComparison.OrdinalIgnoreCase)), "Explorer database list did not include master.");
                    int count = 0;
                    foreach (var (type, _) in CatalogGroups)
                        count += Send(session, ObjectsPageSql("master", type, "", null), token).Tables.Sum(t => t.Rows.Count);
                    foreach (var (kind, _) in AseExtraGroups)
                        _ = Send(session, AseEntriesSql("master", kind, "", null), token);
                    _ = Send(session, AseEntriesSql("master", "LOGIN", "", null), token);
                    Require(count > 0, "Explorer returned no master objects.");
                    return "Explorer database, object-group and ASE extra-branch queries succeeded; " + count + " objects on first pages.";
                });
                await BlockingAsync(options);
                await CheckSync("duplicate session", token => {
                    using var original = Sibling(token);
                    using var duplicate = OpenChecked(original.OpenSibling, token);
                    var a = SingleRow(Send(original, "select db_name(), @@spid", token));
                    var b = SingleRow(Send(duplicate, "select db_name(), @@spid", token));
                    Require(ResultFormats.Cell(a[0]) == ResultFormats.Cell(b[0]) && Integer(a[1]) != Integer(b[1]), "Sibling must use the same database and a different SPID.");
                    return "Database " + ResultFormats.Cell(b[0]) + "; SPIDs " + ResultFormats.Cell(a[1]) + " and " + ResultFormats.Cell(b[1]) + ".";
                });
                await HealthAsync();
                await CheckSync("health non-PROD default last log dump", _ => {
                    var probe = HealthCheck.Probes(ServerPlatform.Ase).Single(p => p.Id == "dump");
                    var cell = HealthCheck.Evaluate(probe, new QueryOutput { Error = "Unavailable dump metadata" }, new(), new HealthRuleSet().RuleFor(probe, production: false));
                    Require(cell.State != HealthState.Fail && cell.Ignored, "Non-PROD default dump rule must ignore failures.");
                    return cell.Caption + "; defaults ignore Last log dump even when its probe fails.";
                });
                await CheckSync("estimated plan", Estimated);
                await CheckSync("actual plan", Actual);
                await Check("MDA delta", async token => {
                    using var session = Sibling(token);
                    var before = MdaRead(session, MdaSql, token);
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                    var after = MdaRead(session, MdaSql, token);
                    Require(before.Tables.Count > 0 && after.Tables.Count > 0, "MDA query returned no table.");
                    var shape = MdaDelta.Detect(after.Tables[0], MdaSql) ?? throw new InvalidOperationException("MDA snapshot has no detectable keys/counters.");
                    var delta = MdaDelta.Compute(before.Tables[0], after.Tables[0], shape);
                    Require(ResultColumns.Find(delta, MdaDelta.StatusColumn) != null, "MDA delta lacks its status column.");
                    return delta.Rows.Count + " delta rows, " + shape.Counters.Count + " counters (empty delta is valid).";
                });
                await CheckSync("live messages", LiveMessages);
                await CheckSync("export with cancel", ExportCancel);
                await CheckSync("sp_sysmon index", token => {
                    if (!options.IncludeSysmon) throw new LiveTestSkip("Opt in with --include-sysmon (one-minute sample; needs sa_role).");
                    using var session = Sibling(token);
                    var output = Send(session, "exec sp_sysmon '00:01:00'", token, timeout: 0);
                    var sections = ResultFormats.SysmonSections(ResultFormats.Isql(output));
                    Require(sections.Count > 0, "sp_sysmon output produced no index sections.");
                    return string.Join(", ", sections.Select(s => s.Heading));
                });
                await CheckSync("PROD guard (pure)", _ => {
                    Require(SqlRisk.Review("delete from x").ChangesState && SqlRisk.StateChanges("delete from x").Contains("delete"), "SqlRisk failed to flag delete from x.");
                    return "SqlRisk flags delete from x; no SQL was sent.";
                });
            }
            catch (LiveTestRefusal ex) { exitCode = 2; Add("refusal", "FAIL", ex.Message, clock.Elapsed.TotalMilliseconds); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Add("runner", "FAIL", ex.Message, clock.Elapsed.TotalMilliseconds); }
            finally
            {
                if (deadline.IsCancellationRequested) Add("global deadline", "FAIL", "Global deadline reached; all active commands were asked to cancel. Cleanup runs with a separate bounded token.", clock.Elapsed.TotalMilliseconds);
                try { root?.Dispose(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Add("root cleanup", "FAIL", ex.Message, 0); }
                deadline.Dispose();
                exitCode ??= results.Any(r => r.Status == "FAIL") || reportFailed ? 1 : 0;
                phase = "finished";
                SaveReport();
                if (reportFailed && exitCode == 0) exitCode = 1;
            }
            Console.WriteLine("Live test exit code: " + exitCode);
            return exitCode ?? 1;
        }

        private static string ReportDestination(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                foreach (string folder in new[] { DataFolder.Root, DataFolder.Legacy })
                {
                    string protectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
                    if (full.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || full.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new LiveTestRefusal("The report must be outside the app's current and legacy user-data folders.");
                }
                return full;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            { throw new LiveTestRefusal("Invalid --report destination."); }
        }

        private void SaveReport()
        {
            if (reportPath == null || reportFailed) return;
            string staging = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(staging, JsonSerializer.Serialize(new { mode = "live-test", phase, started, exitCode, elapsedMs = clock.Elapsed.TotalMilliseconds, checks = results }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new UTF8Encoding(false));
                File.Move(staging, reportPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { reportFailed = true; Console.WriteLine(Safe("FAIL report: " + ex.Message)); }
            finally { try { File.Delete(staging); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
        }

        private void Add(string name, string status, string detail, double elapsed)
        {
            var result = new LiveTestResult(Safe(name), status, Safe(detail), elapsed);
            results.Add(result);
            Console.WriteLine($"{status} {result.Check} ({elapsed:F0} ms): {result.Detail}");
            SaveReport();
        }

        private Task CheckSync(string name, Func<CancellationToken, string> action, CancellationToken? token = null) =>
            Check(name, t => Task.FromResult(action(t)), token);

        private async Task Check(string name, Func<CancellationToken, Task<string>> action, CancellationToken? token = null)
        {
            var timer = Stopwatch.StartNew();
            var run = token ?? deadline.Token;
            if (run.IsCancellationRequested) { Add(name, "SKIP", "Global deadline reached / cancellation requested before check.", 0); return; }
            try
            {
                string detail = await action(run);
                run.ThrowIfCancellationRequested();
                Add(name, "PASS", detail, timer.Elapsed.TotalMilliseconds);
            }
            catch (LiveTestSkip ex) { Add(name, "SKIP", ex.Message, timer.Elapsed.TotalMilliseconds); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { Add(name, "FAIL", run.IsCancellationRequested ? "Cancelled / deadline reached. " + ex.Message : ex.Message, timer.Elapsed.TotalMilliseconds); }
        }

        private static OdbcSession OpenChecked(Func<OdbcSession> open, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Open has no cancellation overload; the saved login timeout bounds the native connect call.
            var session = open();
            if (!token.IsCancellationRequested) return session;
            session.Dispose(); token.ThrowIfCancellationRequested(); return session;
        }

        private OdbcSession Sibling(CancellationToken token) => root == null
            ? throw new LiveTestSkip("Connect did not establish a session.") : OpenChecked(root.OpenSibling, token);

        private static string Failure(QueryOutput output) => (output.Error ?? "Query completed without an error.") + "\n" +
            string.Join("\n", output.Messages.Select(m => $"[{m.Number}] {m.Text}"));

        private static QueryOutput Good(QueryOutput output, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (output.Error != null) throw new InvalidOperationException(Failure(output));
            return output;
        }

        private static QueryOutput Send(OdbcSession session, string sql, CancellationToken token, int timeout = 10) =>
            Good(session.Execute(sql, token, userRun: false, timeoutSeconds: timeout), token);

        private static DataRow SingleRow(QueryOutput output)
        {
            var table = output.Tables.FirstOrDefault(t => t.Rows.Count > 0);
            Require(table != null && table.Rows.Count == 1, "Expected one identity/result row.");
            return table!.Rows[0];
        }

        private static int Integer(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);
        private static int Spid(OdbcSession session, CancellationToken token)
        {
            int spid = Integer(SingleRow(Send(session, "select @@spid", token))[0]);
            Require(spid > 0, "The server returned an invalid SPID.");
            return spid;
        }

        private static IReadOnlyList<ActivityProcess> Processes(OdbcSession session, CancellationToken token)
        {
            var output = Send(session, ActivityMonitor.ProcessesSql, token);
            var table = ActivityMonitor.PickProcessTable(output.Tables) ?? throw new InvalidOperationException("Activity returned no process table.");
            return ActivityMonitor.Parse(table);
        }

        private static bool MdaUnavailable(QueryOutput output) => output.Messages.Any(m =>
            m.Number is 208 or 10330 or 12036 || new[] { "permission", "not authorized", "not enabled", "disabled", "enable monitoring", "not configured", "monitoring is not", "monitoring data is not" }
                .Any(text => m.Text.Contains(text, StringComparison.OrdinalIgnoreCase)));

        private static QueryOutput MdaRead(OdbcSession session, string sql, CancellationToken token)
        {
            var output = session.Execute(sql, token, userRun: false, timeoutSeconds: 10);
            token.ThrowIfCancellationRequested();
            if (MdaUnavailable(output)) throw new LiveTestSkip("MDA disabled / unavailable / permission denied: " + Failure(output));
            return Good(output, token);
        }

        private async Task BlockingAsync(LiveTestOptions options)
        {
            bool ddlAttempted = false, timeoutAttempted = false, treeAttempted = false, probesAttempted = false;
            await Check("blocking pair", async token => {
                using var a = Sibling(token);
                using var b = OpenChecked(a.OpenSibling, token);
                using var c = OpenChecked(a.OpenSibling, token);
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                int aSpid = Spid(a, token), bSpid = Spid(b, token), cSpid = Spid(c, token);
                string name = "dbmon_lt_" + aSpid.ToString(CultureInfo.InvariantCulture);
                string table = "tempdb.." + name;
                bool created = false;
                Task<QueryOutput>? pending = null;
                try
                {
                    foreach (var session in new[] { a, b, c })
                    {
                        _ = Send(session, "set chained off", token);
                        Require(Integer(SingleRow(Send(session, "select @@trancount", token))[0]) == 0, "Fresh blocking session has an unexpected transaction.");
                    }
                    Require(SingleRow(Send(a, "select object_id('" + table + "')", token))[0] is DBNull, "Test table name already exists; refusing to modify or drop it.");
                    var creation = a.Execute("create table " + table + " (id int, v varchar(20))", token, userRun: false, timeoutSeconds: 10);
                    // Record confirmed ownership before Good checks a concurrently cancelled deadline.
                    created = creation.Error == null;
                    _ = Good(creation, token);
                    _ = Send(a, "insert " + table + " values (1, 'original')", token);
                    ddlAttempted = true;
                    await CheckSync("explorer test-table DDL", t => TableDdl(a, name, t));
                    timeoutAttempted = true;
                    await CheckSync("blocking default timeout", _ => {
                        Require(b.Profile.QueryTimeout == 0, "Saved/migrated profile query timeout is " + b.Profile.QueryTimeout + "; this regression check expects the 1.0.1 default 0. The waiting query still uses the saved timeout unchanged.");
                        return "Profile QueryTimeout = 0; B executes without a timeout override.";
                    });
                    _ = Send(a, "begin tran\nupdate " + table + " set v='x' where id=1", token);
                    Require(Integer(SingleRow(Send(a, "select @@trancount", token))[0]) == 1, "A did not keep exactly one transaction open.");
                    var entered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var waiter = Task.Run(() => {
                        entered.SetResult(Stopwatch.GetTimestamp());
                        return b.Execute("select * from " + table + " holdlock where id=1", waitCancellation.Token, userRun: false);
                    });
                    pending = waiter;
                    long waitStarted = await entered.Task;
                    treeAttempted = true;
                    await Check("blocking tree and KillDenied", async t => {
                        var poll = Stopwatch.StartNew();
                        do
                        {
                            var processes = Processes(c, t);
                            var head = ActivityMonitor.BuildTree(processes).FirstOrDefault(n => n.Process.Spid == aSpid && n.IsHeadBlocker);
                            var child = head?.Children.FirstOrDefault(n => n.Process.Spid == bSpid);
                            if (child != null)
                                return "HEAD " + aSpid + " → child " + bSpid + "; KillDenied: " + (ActivityMonitor.KillDenied(child.Process, cSpid, new[] { aSpid, bSpid, cSpid }) ?? "allowed by guard") + ". No KILL executed.";
                            if (waiter.IsCompleted) throw new InvalidOperationException("B finished before the blocking tree became visible: " + Failure(await waiter));
                            await Task.Delay(250, t);
                        } while (poll.Elapsed < TimeSpan.FromSeconds(10));
                        throw new InvalidOperationException("BuildTree did not show A as HEAD with B as child within 10 seconds.");
                    });
                    probesAttempted = true;
                    await CheckSync("blocking SQL text (MDA)", t => "SQL text query returned " + MdaRead(c, ActivityMonitor.SqlTextSql(bSpid), t).Tables.Sum(x => x.Rows.Count) + " rows.");
                    await CheckSync("blocking locks (MDA)", t => "Locks query returned " + MdaRead(c, ActivityMonitor.LocksSql(bSpid), t).Tables.Sum(x => x.Rows.Count) + " rows.");
                    TimeSpan remaining = TimeSpan.FromSeconds(options.BlockSeconds) - Stopwatch.GetElapsedTime(waitStarted);
                    if (remaining > TimeSpan.Zero) await Task.WhenAny(waiter, Task.Delay(remaining, token));
                    token.ThrowIfCancellationRequested();
                    if (waiter.IsCompleted) throw new InvalidOperationException("B did not remain blocked for " + options.BlockSeconds + " seconds: " + Failure(await waiter));
                    _ = Send(a, "rollback tran", token);
                    // Bound release, and cancel the actual command if the server does not release it.
                    waitCancellation.CancelAfter(TimeSpan.FromSeconds(10));
                    var released = Good(await waiter, token);
                    Require(released.Tables.Count == 1 && released.Tables[0].Rows.Count == 1, "B did not return exactly one row after rollback.");
                    return $"A={aSpid}, B={bSpid}, C={cSpid}; B still waiting after {options.BlockSeconds} s with saved timeout {b.Profile.QueryTimeout}; rollback released one row.";
                }
                finally
                {
                    // Never dispose a session while its driver is still executing. Cancellation is cooperative,
                    // as in the app; the report is checkpointed before entering cleanup.
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await CheckSync("blocking rollback cleanup", t => {
                        _ = Send(a, "if @@trancount > 0 rollback tran", t);
                        return "A has no open test transaction.";
                    }, cleanup.Token);
                    if (pending != null)
                    {
                        try { await waitCancellation.CancelAsync(); }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { Add("blocking waiter cancel", "FAIL", ex.Message, 0); }
                        try { _ = await pending; }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { Add("blocking waiter cleanup", "FAIL", ex.Message, 0); }
                    }
                    if (created)
                    {
                        using var drop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await CheckSync("blocking drop cleanup", t => {
                            // The successful CREATE is the only path that permits this DROP. No stale tables touched.
                            _ = Send(a.IsOpen ? a : c, "drop table " + table, t);
                            return "Dropped owned test table " + table + ".";
                        }, drop.Token);
                    }
                }
            });
            if (!ddlAttempted) Add("explorer test-table DDL", "SKIP", "Blocking setup did not create a usable test table.", 0);
            if (!timeoutAttempted) Add("blocking default timeout", "SKIP", "Blocking setup did not reach the timeout check.", 0);
            if (!treeAttempted) Add("blocking tree and KillDenied", "SKIP", "Blocking setup did not start the waiter.", 0);
            if (!probesAttempted)
            {
                Add("blocking SQL text (MDA)", "SKIP", "Blocking setup did not start the waiter.", 0);
                Add("blocking locks (MDA)", "SKIP", "Blocking setup did not start the waiter.", 0);
            }
        }

        private static string TableDdl(OdbcSession session, string name, CancellationToken token)
        {
            var identity = SingleRow(Send(session, "select o.id, u.name from tempdb..sysobjects o join tempdb..sysusers u on o.uid=u.uid where o.id=object_id('tempdb.." + name + "')", token));
            int id = Integer(identity[0]);
            string owner = ResultFormats.Cell(identity[1]).Trim();
            var output = Send(session, "if @@tranchained = 0 and @@trancount = 0 begin\n" + CatalogSql.AseTableDdl("tempdb", owner, name, id) + ";\nend", token);
            Require(output.OmittedMessages == 0 && output.Tables.Count == 6, "DDL metadata is unavailable or partial.");
            var tables = output.Tables.Select(NamedCatalogTable).ToArray();
            // This exact two-column table has no foreign keys. Match the app's resolved-owner schema.
            Require(tables[3].Rows.Count == 0, "Unexpected foreign keys on the newly created test table.");
            tables[3].Columns.Add("ref_owner", typeof(string));
            string ddl = DdlGenerator.Table("tempdb", owner, name, new(tables[0], tables[1], tables[2], tables[3], tables[4], tables[5]));
            Require(ddl.Contains("create table", StringComparison.OrdinalIgnoreCase), "Generated DDL has no CREATE TABLE.");
            return ddl;
        }

        private async Task HealthAsync()
        {
            await Check("health check", async token => {
                using var session = Sibling(token);
                var settings = HealthSettings.Load(DataFolder.File("health-check.json"));
                var rules = HealthRuleSet.Load(DataFolder.File("health-rules.json"), out string? warning, settings);
                var engine = new LiveHealthEngine(session, settings.ServerTimeoutSeconds);
                var servers = await HealthCheck.ExecuteAsync(new[] { new HealthTarget(new RunTarget("live-test", engine), ServerPlatform.Ase, Production: false) }, settings, rules, token);
                foreach (var cell in servers.Single().Cells)
                    Add("health: " + cell.Probe.Title, cell.Ignored ? "SKIP" : cell.State == HealthState.Fail ? "FAIL" : "PASS",
                        cell.Caption + "\n" + cell.Details, engine.Elapsed.GetValueOrDefault(cell.Probe.Sql));
                Require(servers.Single().State != HealthState.Fail, "One or more non-PROD health cells failed. " + warning);
                return warning ?? "Executed all health probes with the user's non-PROD rules (or defaults when no file exists); per-cell outcomes above.";
            });
        }

        private sealed class LiveHealthEngine(OdbcSession session, int timeout) : IQueryEngine
        {
            public string Name => "live-test health";
            public Dictionary<string, double> Elapsed { get; } = new(StringComparer.Ordinal);
            public QueryOutput Execute(string sql, CancellationToken cancellation = default)
            {
                var timer = Stopwatch.StartNew();
                try { return new ToolQueryEngine(session, timeout).Execute(sql, cancellation); }
                finally { Elapsed[sql] = timer.Elapsed.TotalMilliseconds; }
            }
        }

        private string Estimated(CancellationToken token)
        {
            using var session = Sibling(token);
            string xml = "";
            try
            {
                _ = Send(session, EstimatedPlan.Batches(EstimatedPlan.Prepare(AseSessionOptions.Default)), token);
                _ = Send(session, PlanSql, token);
                _ = Send(session, EstimatedPlan.Lift, token);
                xml = EstimatedPlan.Xml(Send(session, EstimatedPlan.Read(0), token).Tables);
                if (xml.Length == 0) xml = EstimatedPlan.Xml(Send(session, EstimatedPlan.Read(-1), token).Tables);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _ = Send(session, EstimatedPlan.Lift, cleanup.Token);
                _ = Send(session, EstimatedPlan.Batches(EstimatedPlan.Restore(AseSessionOptions.Default)), cleanup.Token);
            }
            int operators = EstimatedPlan.Plans(xml).Sum(p => PlanTree.Flatten(p.Tree).Count());
            Require(operators > 0, "Estimated plan has no operator tree (XML length " + xml.Length + ").");
            return operators + " operators; " + xml.Length + " XML characters; options restored.";
        }

        private string Actual(CancellationToken token)
        {
            using var session = Sibling(token);
            var options = AseSessionOptions.Default with { StatisticsIo = true };
            _ = Send(session, EstimatedPlan.Batches(AseSessionOptions.Script(AseSessionOptions.Default, options)), token);
            session.CaptureExecutionIo = options.CapturesExecutionIo;
            var output = Good(session.Execute(PlanSql, token, userRun: true), token);
            var plans = output.Tables.SelectMany(ExecutionIo.PlansOf).ToArray();
            int operators = plans.Sum(p => PlanTree.Flatten(p.Tree).Count());
            Require(operators > 0, "Actual plan has no operator tree. " + Failure(output));
            return operators + " operators via Statistics IO result/ExecutionIo.PlansOf; " + plans.Length + " plans.";
        }

        private sealed class LiveMessageCollector : IProgress<QueryMessage>
        {
            private readonly object gate = new();
            private readonly List<(string Text, long At)> messages = [];
            public void Report(QueryMessage value) { lock (gate) messages.Add((value.Text, Stopwatch.GetTimestamp())); }
            public (string Text, long At)[] Snapshot() { lock (gate) return messages.ToArray(); }
        }

        private string LiveMessages(CancellationToken token)
        {
            using var session = Sibling(token);
            // Progress<T> would post callbacks and could manufacture late delivery; collect synchronously.
            var collector = new LiveMessageCollector();
            long start = Stopwatch.GetTimestamp();
            _ = Good(session.Execute("print 'lt one' waitfor delay '00:00:02' print 'lt two'", token, userRun: false, timeoutSeconds: 10, messages: collector), token);
            long end = Stopwatch.GetTimestamp();
            var messages = collector.Snapshot();
            var first = messages.FirstOrDefault(m => m.Text.Contains("lt one", StringComparison.Ordinal));
            var second = messages.FirstOrDefault(m => m.Text.Contains("lt two", StringComparison.Ordinal));
            double lead = first.At == 0 ? 0 : Stopwatch.GetElapsedTime(first.At, end).TotalMilliseconds;
            Require(first.At != 0 && second.At > first.At && lead >= 1000, "First PRINT did not arrive live at least one second before completion (lead " + lead.ToString("F0", CultureInfo.InvariantCulture) + " ms), or second PRINT is missing/out of order.");
            return "First message at " + Stopwatch.GetElapsedTime(start, first.At).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms; batch ended " + lead.ToString("F0", CultureInfo.InvariantCulture) + " ms later; both PRINTs received.";
        }

        private string ExportCancel(CancellationToken token)
        {
            using var session = Sibling(token);
            string directory = Path.Combine(Path.GetTempPath(), "dbmon-live-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "export.partial.csv");
            Directory.CreateDirectory(directory);
            try
            {
                QueryOutput? output = null;
                bool threwCancellation = false;
                long rows;
                using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16);
                    using var text = new StreamWriter(stream, new UTF8Encoding(false, true), 1 << 16);
                    var writer = new QueryExportWriter(text, QueryExportFormat.Csv);
                    cancel.CancelAfter(TimeSpan.FromSeconds(1));
                    try
                    {
                        // TOP caps disk use; the trailing wait keeps a small catalog from finishing before Esc.
                        output = session.Export("select top 10000 * from master..sysobjects a, master..sysobjects b;\nwaitfor delay '00:00:05'", writer, cancel.Token);
                    }
                    catch (OperationCanceledException) { threwCancellation = true; }
                    rows = writer.RowsWritten;
                    Require(cancel.IsCancellationRequested, "Export finished before cancellation could be exercised.");
                }
                token.ThrowIfCancellationRequested();
                // Verify the same connection, after export has relinquished native command ownership.
                Require(Integer(SingleRow(Send(session, "select 1", token))[0]) == 1, "Session is unusable after export cancellation.");
                Require(threwCancellation || output?.Error != null, "Export did not report cancellation/partial results.");
                Require(rows <= 10000, "Export exceeded its SQL row cap.");
                return rows + " rows streamed through OdbcSession.Export/QueryExportWriter; cancelled after ~1 s; same session answered select 1. Partial file removed in finally. Trailing 5 s wait ensures the cancellation window on small catalogs.";
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
#endif
