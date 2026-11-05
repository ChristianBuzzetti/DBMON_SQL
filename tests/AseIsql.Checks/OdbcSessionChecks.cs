using AseIsql;
using AseIsql.Odbc;
using System.Data;

static class OdbcSessionChecks
{
    private const string Frame = "[SAP][ASE ODBC Driver][Adaptive Server Enterprise] ";
    private static readonly ConnectionProfile Profile = new("fixture", 5000, "master", "fixture_user", false, "");
    private static OdbcSession Open(ScriptedOdbcConnection connection, ConnectionProfile? profile = null) =>
        OdbcSession.Open(profile ?? Profile, "fixture-secret", "", connection);
    private static void Assert(bool value) { if (!value) throw new Exception("ODBC session assertion failed."); }
    private static DataTable Numbers(params int[] numbers)
    {
        var table = new DataTable(); table.Columns.Add("n", typeof(int));
        foreach (int number in numbers) table.Rows.Add(number);
        return table;
    }
    private static ScriptedOdbcException Failure(string state = "42000", string text = "Rejected fixture-secret") =>
        new(new OdbcDiagnostic(50001, state, Frame + text));

    public static void Run(Action<string, Action> check)
    {
        check("OdbcSession reads facts, multiple result sets and affected rows through the driver seam", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("select fixture", Numbers(1, 2), Numbers(3)).RecordsAffected = 4;
            using (var session = Open(connection))
            {
                Assert(session.IsOpen && session.Facts == new SessionFacts("fixture_db", 73, "fixture_login", "utf8"));
                var output = session.Execute("select fixture");
                Assert(output.Error == null && output.Tables.Count == 2 && output.Tables[0].Rows.Count == 2);
                Assert((int)output.Tables[1].Rows[0][0] == 3 && output.Messages.Single().Text == "Rows affected: 4");
                Assert(connection.Pending == 0 && connection.Executed.All(command => command.Disposed && command.Reader!.IsClosed));
            }
            Assert(connection.Disposed && connection.State == ConnectionState.Closed);
        });
        check("OdbcSession bounds login diagnostics and keeps run messages separate", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            for (int i = 0; i < 620; i++) connection.LoginDiagnostics.Add(new(0, "01000", Frame + $"login {i} fixture-secret"));
            var step = connection.Add("print fixture");
            step.OnExecute = () => connection.Emit(new OdbcDiagnostic(0, "01000", Frame), new(0, "01000", Frame + "run only"));
            using var session = Open(connection);
            Assert(session.LoginMessages.Count == 500 && session.OmittedLoginMessages == 120);
            Assert(session.LoginMessages[249].Text == "login 249 [password]" && session.LoginMessages[250].Text == "login 370 [password]");
            var output = session.Execute("print fixture");
            Assert(output.Error == null && output.Messages.Single().Text == "run only");
            Assert(session.LoginMessages.Count == 500 && session.LoginMessages[^1].Text == "login 619 [password]");
            Assert(session.LoginMessages.All(message => !message.RawText!.Contains("fixture-secret")) && connection.Pending == 0);
        });
        check("OdbcSession streams every diagnostic live while retaining only its head and tail", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            bool inDriver = false;
            var live = new List<QueryMessage>();
            var progress = new MessageProgress(message => { Assert(inDriver); live.Add(message); });
            var step = connection.Add("print many", Numbers(7));
            step.BeforeRead = read => {
                if (read != 1) return;
                inDriver = true;
                for (int i = 0; i < 620; i++) connection.Emit(new OdbcDiagnostic(0, "01000", Frame + $"message {i}"));
                Assert(live.Count == 620); inDriver = false;
            };
            using var session = Open(connection);
            var output = session.Execute("print many", default, progress);
            Assert(output.Error == null && live.Count == 620 && output.Messages.Count == 620 && output.OmittedMessages == 0);
            Assert(output.Messages[249].Text == "message 249" && output.Messages[250].Text == "message 250");
            Assert(output.Messages[^1].Text == "message 619" && session.LoginMessages.Count == 0 && connection.Pending == 0);
        });
        check("OdbcSession captures bounded error records and redacts original diagnostics", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("bad batch").Error = new ScriptedOdbcException(Enumerable.Range(0, 620)
                .Select(i => new OdbcDiagnostic(50001, "42000", Frame + $"error {i} fixture-secret")).ToArray());
            using var session = Open(connection);
            var live = new List<QueryMessage>();
            var output = session.Execute("bad batch", default, new MessageProgress(live.Add));
            Assert(output.Error != null && output.ErrorSource == "ODBC" && session.IsOpen);
            Assert(live.Count == 620 && output.Messages.Count == 620 && output.OmittedMessages == 0);
            Assert(output.Messages[250].Text == "error 250 [password]" && output.Messages[^1].Text == "error 619 [password]");
            string raw = output.ErrorRawText ?? throw new Exception("Missing original error diagnostic.");
            Assert(!raw.Contains("fixture-secret") && raw.Contains("[password]") && connection.Pending == 0);
        });
        check("OdbcSession submits go N exactly N times with the run timeout", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            for (int i = 1; i <= 3; i++) connection.Add("select repeated", Numbers(i));
            using var session = Open(connection);
            using var cancellation = new CancellationTokenSource();
            var output = session.Execute("select repeated\ngo 3", cancellation.Token, userRun: true, timeoutSeconds: 0);
            Assert(output.Error == null && output.Tables.Select(table => (int)table.Rows[0][0]).SequenceEqual(new[] { 1, 2, 3 }));
            Assert(connection.Executed.Skip(1).All(command => command.CommandTimeout == 0) && connection.Pending == 0);
            Assert(output.Messages.Any(message => message.Text == "Summary: 3 batches ok / 0 with error."));
            cancellation.Cancel(); // No registration may call Cancel on an already-disposed command.
            Assert(connection.Executed.All(command => command.CancelCalls == 0));
        });
        check("OdbcSession stops on errors by default and continues only for opted-in user runs", () => {
            foreach (var (continueOnError, userRun) in new[] { (false, true), (true, true), (true, false) })
            {
                var connection = new ScriptedOdbcConnection().WithFacts();
                connection.Add("bad batch").Error = Failure();
                connection.Add("select after", Numbers(8));
                using var session = Open(connection);
                session.ContinueOnError = continueOnError;
                var output = session.Execute("bad batch\ngo\nselect after", default, userRun);
                bool continued = continueOnError && userRun;
                Assert(output.Error != null && output.ErrorSource == "ODBC" && session.IsOpen);
                Assert(output.Tables.Count == (continued ? 1 : 0) && connection.Pending == (continued ? 0 : 1));
                Assert(output.ErrorRawText == Frame + "Rejected [password]");
                if (continued) Assert(output.Messages.Any(message => message.Text == "Summary: 1 batch ok / 1 with error."));
            }
        });
        check("OdbcSession treats connection-level errors as fatal even with continue-on-error", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("broken batch").Error = Failure("08S01", "Connection interrupted");
            connection.Add("select never");
            using var session = Open(connection);
            session.ContinueOnError = true;
            var output = session.Execute("broken batch\ngo\nselect never");
            Assert(output.Error != null && !session.IsOpen && connection.Pending == 1);
            Assert(output.Messages.Any(message => message.Text.Contains("1 not run")));
            Assert(session.Execute("select never").Error!.Contains("disconnected") && connection.Pending == 1);
        });
        check("OdbcSession captures reader failures, preserves rows and continues when requested", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("select interrupted", Numbers(1, 2)).BeforeRead = read => { if (read == 2) throw Failure(); };
            connection.Add("select after", Numbers(3));
            using var session = Open(connection);
            session.ContinueOnError = true;
            var output = session.Execute("select interrupted\ngo\nselect after");
            Assert(output.Error != null && output.ErrorSource == "ODBC" && session.IsOpen && connection.Pending == 0);
            Assert(output.Tables.Count == 2 && output.Tables[0].Rows.Count == 1 && (int)output.Tables[1].Rows[0][0] == 3);
            Assert(connection.Executed[1].CancelCalls == 1 && connection.Executed[1].Reader!.IsClosed);
            Assert(output.Messages.Any(message => message.Text == "Rejected [password]"));
        });
        check("OdbcSession cancels its active command, preserves partial rows and stops subsequent batches", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            using var cancellation = new CancellationTokenSource();
            connection.Add("select slow", Numbers(1, 2, 3)).BeforeRead = read => { if (read == 2) cancellation.Cancel(); };
            connection.Add("select never");
            using var session = Open(connection);
            session.ContinueOnError = true;
            var output = session.Execute("select slow\ngo\nselect never", cancellation.Token);
            var command = connection.Executed[1];
            Assert(output.Error!.Contains("Cancellation requested") && output.Tables.Single().Rows.Count == 1);
            Assert(command.CancelCalls > 0 && command.Disposed && command.Reader!.IsClosed && connection.Pending == 1);
        });
        check("OdbcSession rejects pre-cancelled execution before creating a command", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            using var session = Open(connection);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool cancelled = false;
            try { session.Execute("select never", cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && connection.Executed.Count == 1 && connection.Pending == 0);
        });
        check("OdbcSession initializes textsize once per session across capture toggles and reads plans without replay", () => {
            for (int sessionIndex = 0; sessionIndex < 2; sessionIndex++)
            {
                var connection = new ScriptedOdbcConnection().WithFacts();
                Capture(connection, "exec fixture", initialize: true);
                connection.Add("select capture_off");
                Capture(connection, "exec fixture_again", initialize: false);
                using var session = Open(connection);
                session.CaptureExecutionIo = true;
                var first = session.Execute("exec fixture");
                session.CaptureExecutionIo = false;
                Assert(session.Execute("select capture_off").Error == null);
                session.CaptureExecutionIo = true;
                var last = session.Execute("exec fixture_again");
                Assert(first.Error == null && last.Error == null && connection.Pending == 0);
                Assert(first.Messages.Any(message => message.Text == ScriptRunner.PlanNotCaptured));
                Assert(connection.Executed.Count(command => command.CommandText == "set textsize 16777216") == 1);
                Assert(connection.Executed.Count(command => command.CommandText == "exec fixture") == 1);
                Assert(connection.Executed.Count(command => command.CommandText == "exec fixture_again") == 1);
            }
        });
        check("OdbcSession preserves user textsize set before capture is enabled", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("set textsize 2048");
            Capture(connection, "select fixture", initialize: false);
            using var session = Open(connection);
            Assert(session.Execute("set textsize 2048").Error == null);
            session.CaptureExecutionIo = true;
            Assert(session.Execute("select fixture").Error == null && connection.Pending == 0);
            Assert(connection.Executed.All(command => command.CommandText != "set textsize 16777216"));
        });
        check("OdbcSession cancels on the shared row limit and never sends later batches", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("select first", Numbers(1));
            connection.Add("select second", Numbers(2));
            connection.Add("select never");
            using var session = Open(connection, Profile with { MaxRows = 1 });
            session.ContinueOnError = true;
            var output = session.Execute("select first\ngo\nselect second\ngo\nselect never");
            Assert(output.Error!.Contains("Local limit of 1 rows") && output.Tables.Sum(table => table.Rows.Count) == 1);
            Assert(connection.Pending == 1 && connection.Executed[2].CancelCalls == 1);
        });
    }

    private static void Capture(ScriptedOdbcConnection connection, string sql, bool initialize)
    {
        if (initialize) connection.Add("set textsize 16777216");
        connection.Add("set plan for show_execio_xml to message on");
        connection.Add(sql);
        connection.Add("select showplan_in_xml(0)");
        connection.Add("select showplan_in_xml(-1)");
    }
}
