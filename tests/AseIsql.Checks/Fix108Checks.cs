using AseIsql;

// 1.0.8: menu keys, active processes, pluralization and ASE process diagnostics.
static class Fix108Checks
{
    private static void Assert(bool value, string what = "") { if (!value) throw new Exception("1.0.8 assertion failed. " + what); }

    private static ActivityProcess P(int spid, string status, string command, int blockedBy = 0) =>
        new(spid, status, "sa", "host", "isql", "master", command, 0, 0, 0, blockedBy, 0, "", 1);

    private static QueryMessage M(string text, int number = 0) => new(number, "ASE", text);
    private static QueryMessage Completed => M("DBCC execution completed. If DBCC printed error messages, contact a user with System Administrator (SA) role.", 2528);

    public static void Run(Action<string, Action> check)
    {
        check("1.0.8 menu access keys are unique and match the documented letters", () =>
        {
            Assert(MenuHeaders.AccessKey(MenuHeaders.SessionDuplicate) == 'D', "session duplicate");
            Assert(MenuHeaders.AccessKey(MenuHeaders.MonitorDuplicate) == 'D', "monitor duplicate");
            Assert(MenuHeaders.AccessKey(MenuHeaders.SessionDisconnect) == 'I', "disconnect");
            Assert(MenuHeaders.AccessKey(MenuHeaders.MonitorKill) == null, "kill has no key");
            Assert(MenuHeaders.AccessKey("a__b") == null, "literal underscore");
            Assert(MenuHeaders.AccessKey("a__b_c") == 'C', "literal then key");
            Assert(MenuHeaders.UniqueAccessKeys(MenuHeaders.SessionMenu), "session menu");
            Assert(MenuHeaders.UniqueAccessKeys(MenuHeaders.MonitorMenu), "monitor menu");
            Assert(!MenuHeaders.UniqueAccessKeys(new[] { "_Foo", "_Far" }), "collision detected");
        });

        check("1.0.8 Only active hides idle sessions but keeps work and blockers", () =>
        {
            var idleRecv = P(10, "recv sleep", "AWAITING COMMAND");
            var idleSleeping = P(11, "sleeping", "awaiting command ");
            var alarm = P(12, "alarm sleep", "WAITFOR");
            var running = P(13, "running", "SELECT");
            var head = P(14, "sleeping", "AWAITING COMMAND");
            var blocked1 = P(15, "lock sleep", "UPDATE", 14);
            var blocked2 = P(16, "lock sleep", "AWAITING COMMAND", 14);
            var all = new[] { idleRecv, idleSleeping, alarm, running, head, blocked1, blocked2 };
            var kept = ActivityMonitor.OnlyActive(all);
            Assert(kept.Select(p => p.Spid).SequenceEqual(new[] { 12, 13, 14, 15, 16 }), "kept " + string.Join(",", kept.Select(p => p.Spid)));
            Assert(ActivityMonitor.IsIdle(idleRecv) && ActivityMonitor.IsIdle(idleSleeping), "idle");
            Assert(!ActivityMonitor.IsIdle(alarm) && !ActivityMonitor.IsIdle(running), "not idle");
            Assert(ActivityMonitor.IsIdle(P(1, " Send Sleep ", " awaiting command ")), "trim and case");
            Assert(!ActivityMonitor.IsIdle(P(1, "sleeping", "SELECT")), "sleeping with a command");
            Assert(ActivityMonitor.OnlyActive(Array.Empty<ActivityProcess>()).Count == 0, "empty");
        });

        check("1.0.8 Counts.Of pluralizes s, x, z, ch and sh nouns with es", () =>
        {
            Assert(Counts.Of(2, "process") == "2 processes", Counts.Of(2, "process"));
            Assert(Counts.Of(1, "process") == "1 process");
            Assert(Counts.Of(3, "row") == "3 rows");
            Assert(Counts.Of(2, "batch") == "2 batches" && Counts.Of(2, "hash") == "2 hashes" && Counts.Of(2, "box") == "2 boxes");
        });

        check("1.0.8 diagnostic SQL is exact, invariant and rejects invalid SPIDs", () =>
        {
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
                Assert(ProcessDiagnostics.SqlTextSql(1327) == "dbcc traceon(3604)\ndbcc sqltext(1327)\ndbcc traceoff(3604)");
                Assert(ProcessDiagnostics.ShowplanSql(1327) == "dbcc traceon(3604)\nexec sp_showplan 1327, null, null, null\ndbcc traceoff(3604)");
                foreach (int spid in new[] { 0, -1, int.MinValue })
                {
                    bool sqlThrew = false, planThrew = false;
                    try { ProcessDiagnostics.SqlTextSql(spid); } catch (ArgumentOutOfRangeException) { sqlThrew = true; }
                    try { ProcessDiagnostics.ShowplanSql(spid); } catch (ArgumentOutOfRangeException) { planThrew = true; }
                    Assert(sqlThrew && planThrew, "invalid SPID " + spid);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        });

        check("1.0.8 dbcc sqltext parses ASE SQL and drops DBCC housekeeping", () =>
        {
            const string sql = "select count(*) from smg_seguros_c..polizas where estado = 'A'";
            var result = ProcessDiagnostics.ParseSqlText(new[] { Completed, M("SQL Text: " + sql), Completed }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == sql && result.Detail == "", result.Text);
            result = ProcessDiagnostics.ParseSqlText(new[] { M("DBCC execution completed (traceon)."), M("SQL Text: " + sql + "  \r\n"), M("DBCC execution completed (traceoff).") }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == sql, "housekeeping by prefix and trailing whitespace");
            result = ProcessDiagnostics.ParseSqlText(new[] { M("SPID 1327\nSQL Text: " + sql), Completed }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == sql, "text starts after the SQL marker");
        });

        check("1.0.8 dbcc sqltext keeps procedure and subordinate SQL in order", () =>
        {
            var result = ProcessDiagnostics.ParseSqlText(new[] {
                M("SQL Text: exec sp_cobranza_diaria @fecha = '20261105'"),
                M("Executing procedure: sp_cobranza_diaria"),
                M("Subordinate SQL Text: update cuotas set estado = 'P' where ..."), Completed
            }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == "exec sp_cobranza_diaria @fecha = '20261105'\n-- Executing procedure: sp_cobranza_diaria\nupdate cuotas set estado = 'P' where ...", result.Text);
            result = ProcessDiagnostics.ParseSqlText(new[] {
                M("SQL Text: select * from test..t1"), M("Executing procedure: *ss1137874382_1262752346ss*"),
                M("Subordinate SQL Text: select * from test..t1"), Completed
            }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == "select * from test..t1\n-- Executing procedure: *ss1137874382_1262752346ss*\nselect * from test..t1", result.Text);
        });

        check("1.0.8 dbcc sqltext joins split messages and continuation lines", () =>
        {
            string first = "select " + new string('x', 2048) + " from test..t1";
            var result = ProcessDiagnostics.ParseSqlText(new[] {
                M("SQL Text: " + first + "  "), M("    where estado = 'A'  \r\n    and id > 0\r\n"), Completed
            }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == first + "\n    where estado = 'A'\n    and id > 0", result.Text);
            result = ProcessDiagnostics.ParseSqlText(new[] { M("SQL Text:\nselect 1\nExecuting procedure: p\nSubordinate SQL Text:\n  select 2") }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == "select 1\n-- Executing procedure: p\n\n  select 2", result.Text);
        });

        check("1.0.8 dbcc sqltext distinguishes empty output and execution failure", () =>
        {
            foreach (var messages in new[] { Array.Empty<QueryMessage>(), new[] { Completed }, new[] { M("SQL Text:  \r\n"), Completed } })
            {
                var empty = ProcessDiagnostics.ParseSqlText(messages, null);
                Assert(empty.Status == DiagnosticStatus.Empty && empty.Text == "" && empty.Detail == "");
            }
            const string error = "[208] Diagnostic command failed.\nAdditional context.";
            var failed = ProcessDiagnostics.ParseSqlText(new[] { M("SQL Text: select 1"), Completed }, error);
            Assert(failed.Status == DiagnosticStatus.Failed && failed.Text == "" && failed.Detail == error);
        });

        check("1.0.8 process diagnostics recognize permission messages and errors", () =>
        {
            const string error = "[10353] You must have the following role(s) to execute this command/procedure: 'sa_role' or 'sybase_ts_role' .";
            Assert(ProcessDiagnostics.ParseSqlText(new[] { Completed }, error).Status == DiagnosticStatus.NoPermission);
            Assert(ProcessDiagnostics.ParseShowplan(new[] { Completed }, error).Status == DiagnosticStatus.NoPermission);
            foreach (int number in new[] { 10353, 10330, 567, 10351 })
            {
                var messages = new[] { M("ASE refused this diagnostic.", number), Completed };
                Assert(ProcessDiagnostics.ParseSqlText(messages, null).Status == DiagnosticStatus.NoPermission, "sql permission number " + number);
                Assert(ProcessDiagnostics.ParseShowplan(messages, null).Status == DiagnosticStatus.NoPermission, "plan permission number " + number);
            }
            foreach (string phrase in new[] { "YOU MUST HAVE THE FOLLOWING ROLE", "SA_ROLE", "SYBASE_TS_ROLE", "PERMISSION DENIED", "NO PERMISSION" })
            {
                var messages = new[] { M(phrase), Completed };
                Assert(ProcessDiagnostics.ParseSqlText(messages, null).Status == DiagnosticStatus.NoPermission, phrase);
                Assert(ProcessDiagnostics.ParseShowplan(messages, null).Status == DiagnosticStatus.NoPermission, phrase);
                Assert(ProcessDiagnostics.ParseSqlText(Array.Empty<QueryMessage>(), phrase).Status == DiagnosticStatus.NoPermission, phrase);
                Assert(ProcessDiagnostics.ParseShowplan(Array.Empty<QueryMessage>(), phrase).Status == DiagnosticStatus.NoPermission, phrase);
            }
        });

        check("1.0.8 sp_showplan retains the ASE operator tree and removes status lines", () =>
        {
            string[] plan = ["QUERY PLAN FOR STATEMENT 1 (at line 1).", "Optimized using Serial Mode", "    STEP 1",
                "        The type of query is SELECT.", "    2 operator(s) under root", "   |ROOT:EMIT Operator (VA = 2)",
                "   |   |SCALAR AGGREGATE Operator (VA = 1)", "   |   |  Evaluate Ungrouped COUNT AGGREGATE.",
                "   |   |   |SCAN Operator (VA = 0)", "   |   |   |  FROM TABLE", "   |   |   |  polizas", "   |   |   |  Table Scan."];
            var messages = plan.Select(line => M(line)).Concat(new[] { M("(return status = 0)"), Completed }).ToArray();
            var result = ProcessDiagnostics.ParseShowplan(messages, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == string.Join("\n", plan) && result.Detail == "", result.Text);
            Assert(!result.Text.Contains("return status", StringComparison.OrdinalIgnoreCase));
            result = ProcessDiagnostics.ParseShowplan(new[] { M(string.Join("\r\n", plan) + "\r\n (RETURN STATUS = 0) \r\nDBCC execution completed.") }, null);
            Assert(result.Status == DiagnosticStatus.Ok && result.Text == string.Join("\n", plan), "multi-line message");
        });

        check("1.0.8 sp_showplan reports idle processes and failures", () =>
        {
            foreach (var messages in new[] { Array.Empty<QueryMessage>(), new[] { Completed }, new[] { M("(return status = 1)"), Completed } })
            {
                var empty = ProcessDiagnostics.ParseShowplan(messages, null);
                Assert(empty.Status == DiagnosticStatus.Empty && empty.Text == "" && empty.Detail == "");
            }
            const string idle = "The spid 1327 is not executing any statement.";
            var result = ProcessDiagnostics.ParseShowplan(new[] { M(idle), M("(return status = 1)"), Completed }, null);
            Assert(result.Status == DiagnosticStatus.Empty && result.Text == "" && result.Detail == idle);
            result = ProcessDiagnostics.ParseShowplan(new[] { Completed }, "Plan request failed.");
            Assert(result.Status == DiagnosticStatus.Failed && result.Detail == "Plan request failed.");
        });
    }
}
