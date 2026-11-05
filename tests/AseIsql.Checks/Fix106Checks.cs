using AseIsql;
using AseIsql.Odbc;
using System.Data;

// 1.0.6: the estimated plan path is exactly the 1.0.3 command sequence (no flushmessage), and our code never buffers server messages.
static class Fix106Checks
{
    private const string Frame = "[SAP][ASE ODBC Driver][Adaptive Server Enterprise]";
    private const string UserSql = "select count(*) from master..sysobjects where type = 'U'";
    private static readonly ConnectionProfile Ase = new("fixture", 5000, "master", "fixture_user", false, "");
    private static void Assert(bool value, string what = "") { if (!value) throw new Exception("1.0.6 assertion failed. " + what); }

    private static DataTable TextTable(string text)
    {
        var table = new DataTable(); table.Columns.Add("xml", typeof(string));
        table.Rows.Add(text); return table;
    }

    private static string PlanXml(int padding = 0) =>
        "<?xml version=\"1.0\"?><query><statementNum>1</statementNum><lineNum>1</lineNum><text>" + UserSql + "</text>" +
        "<opTree><Emit><est><rowCnt>1</rowCnt></est><ScalarAgg><est><rowCnt>1</rowCnt></est><TableScan><est><rowCnt>50</rowCnt><lio>7</lio></est>" +
        "<objName>sysobjects</objName></TableScan></ScalarAgg></Emit></opTree>" + new string(' ', padding) + "</query>";

    // The sequence MainWindow.LiveTest.Estimated() sends, against a scripted engine that accepts exactly it.
    private static (ScriptedOdbcConnection Connection, string Xml) RunEstimated(string planXml)
    {
        var connection = new ScriptedOdbcConnection().WithFacts();
        var options = AseSessionOptions.Default;
        foreach (string statement in EstimatedPlan.Prepare(options)) connection.Add(statement);
        connection.Add(UserSql);
        connection.Add(EstimatedPlan.Lift);
        connection.Add(EstimatedPlan.Read(0), TextTable(planXml));
        foreach (string statement in EstimatedPlan.Restore(options)) connection.Add(statement);
        string xml;
        using (var session = OdbcSession.Open(Ase, "fixture-secret", "", connection))
        {
            QueryOutput Send(string sql) { var output = session.Execute(sql, CancellationToken.None, userRun: false, timeoutSeconds: 10); Assert(output.Error == null, output.Error ?? ""); return output; }
            _ = Send(EstimatedPlan.Batches(EstimatedPlan.Prepare(options)));
            _ = Send(UserSql);
            _ = Send(EstimatedPlan.Lift);
            xml = EstimatedPlan.Xml(Send(EstimatedPlan.Read(0)).Tables);
            _ = Send(EstimatedPlan.Batches(EstimatedPlan.Restore(options)));
        }
        return (connection, xml);
    }

    public static void Run(Action<string, Action> check)
    {
        check("Estimated plan runs the exact 1.0.3 command sequence on a fresh ASE session and reads the XML", () => {
            string planXml = PlanXml();
            var (connection, xml) = RunEstimated(planXml);
            var expected = new[] { ScriptedOdbcConnection.FactsSql, "set plan for show_final_plan_xml to message on", "set noexec on", UserSql, EstimatedPlan.Lift, EstimatedPlan.Read(0), "set plan for show_final_plan_xml off" };
            Assert(connection.Executed.Select(c => c.CommandText.Trim()).SequenceEqual(expected), string.Join(" | ", connection.Executed.Select(c => c.CommandText)));
            Assert(connection.Executed.All(c => !c.CommandText.Contains("flushmessage", StringComparison.OrdinalIgnoreCase)), "flushmessage sent");
            Assert(connection.Pending == 0, "pending " + connection.Pending);
            Assert(EstimatedPlan.Plans(xml).Sum(p => PlanTree.Flatten(p.Tree).Count()) > 0, "no operators");
            Assert(xml.Length == planXml.Length, "xml length " + xml.Length + " vs " + planXml.Length);

            // A big plan cell is returned untruncated by a tool run (the cell cap is 1 MiB).
            string big = PlanXml(300_000 - PlanXml().Length);
            Assert(big.Length == 300_000 && big.Length < ResultBudget.MaxCellBytes);
            var (_, bigXml) = RunEstimated(big);
            Assert(bigXml.Length == EstimatedPlan.Xml([TextTable(big)]).Length && bigXml.Length > 0, "big xml length " + bigXml.Length);
            Assert(EstimatedPlan.Plans(bigXml).Count > 0, "big plan has no operators");
        });
        check("OdbcSession publishes each server message to the progress sink the moment the driver raises it", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            var live = new List<string>();
            var progress = new MessageProgress(message => live.Add(message.Text));
            bool seenBeforeRead = false;
            var step = connection.Add("print 'lt one'", TextTable("row"));
            step.OnExecute = () => {
                connection.Emit(new OdbcDiagnostic(0, "01000", Frame + "lt one"));
                // Synchronous sink: already delivered while the driver is still inside the call, nothing buffered.
                Assert(live.SequenceEqual(new[] { "lt one" }), "not delivered inside OnExecute");
            };
            step.BeforeRead = read => { if (read == 1) seenBeforeRead = live.Contains("lt one"); };
            using var session = OdbcSession.Open(Ase, "fixture-secret", "", connection);
            var output = session.Execute("print 'lt one'", CancellationToken.None, userRun: false, timeoutSeconds: 10, messages: progress);
            Assert(output.Error == null && seenBeforeRead && live.SequenceEqual(new[] { "lt one" }) && connection.Pending == 0);
        });
    }
}
