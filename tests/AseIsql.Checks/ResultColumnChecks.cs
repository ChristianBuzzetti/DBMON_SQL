using System.Data;
using AseIsql;

internal static class ResultColumnChecks
{
    // Match ResultReader's binding names, with an optional unrelated display caption to exercise SourceName alone.
    private static DataTable Table(bool sourceOnly, params string[] names)
    {
        var table = new DataTable();
        for (int i = 0; i < names.Length; i++) {
            var column = table.Columns.Add("column_" + i, typeof(object));
            column.Caption = sourceOnly ? "Display " + i : names[i];
            column.ExtendedProperties["SourceName"] = names[i];
        }
        return table;
    }
    private static object Value(DataRow row, string name) => row[ResultColumns.Find(row.Table, name) ?? throw new Exception("Missing " + name)];
    private static QueryOutput Output(params DataTable[] tables)
    {
        var output = new QueryOutput(); output.Tables.AddRange(tables); return output;
    }
    private static T Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T ex) { return ex; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class QueueEngine(params QueryOutput[] outputs) : IQueryEngine
    {
        private readonly Queue<QueryOutput> pending = new(outputs);
        public string Name => "Result column check";
        public int Remaining => pending.Count;
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) => pending.Dequeue();
    }

    internal static void Run(Action<string, Action> check, Action<bool> assert)
    {
        check("Result column lookup trims and matches binding names, captions and source aliases", () => {
            var table = Table(false, " spid ", "caption", "source");
            table.Columns[1].ExtendedProperties.Remove("SourceName"); table.Columns[1].Caption = " SQLText ";
            table.Columns[2].Caption = "Display heading"; table.Columns[2].ExtendedProperties["SourceName"] = " CPUTime ";
            assert(ResultColumns.Find(table, " SPID ") == table.Columns[0]);
            assert(ResultColumns.Find(table, "sqltext") == table.Columns[1]);
            assert(ResultColumns.Find(table, "cputime") == table.Columns[2]);
            assert(ResultColumns.Find(table, "COLUMN_2") == table.Columns[2]);
            assert(ResultColumns.Find(table, "absent", " SQLTEXT ") == table.Columns[1]);
            assert(ResultColumns.Find(table, "absent") == null && ResultColumns.Find(table) == null);
            assert(ResultColumns.Describe(table) == "spid, SQLText, CPUTime");
            var named = new DataTable(); named.Columns.Add("plain");
            assert(ResultColumns.Find(named, "PLAIN") == named.Columns[0] && ResultColumns.Describe(named) == "plain");
            assert(ResultColumns.Describe(new DataTable()) == "");
        });
        check("Live ASE 16 sysprocesses aliases build the full blocking tree with SPID 55 as HEAD", () => {
            var table = Table(false, "spid", "status", "loginame", "hostname", "program_name", "dbname", "cmd",
                "cpu", "physical_io", "memusage", "blocked", "time_blocked", "tran_name", "suid");
            foreach (var (spid, blocked) in new[] { (55, 0), (61, 55), (62, 55), (70, 62) })
                table.Rows.Add(spid, "running ", " sa ", "host", "isql", "app", "SELECT", 3, 4, 5, blocked, 6, "tx", 1);
            var processes = ActivityMonitor.Parse(table);
            var head = ActivityMonitor.BuildTree(processes).Single();
            assert(head.Process.Spid == 55 && head.IsHeadBlocker);
            assert(head.Children.Select(n => n.Process.Spid).SequenceEqual(new[] { 61, 62 }));
            assert(head.Children[1].Children.Single().Process.Spid == 70);
            assert(ActivityMonitor.Flatten([head]).Select(n => n.Depth).SequenceEqual(new[] { 0, 1, 1, 2 }));
            assert(processes[0] == new ActivityProcess(55, "running", "sa", "host", "isql", "app", "SELECT", 3, 4, 5, 0, 6, "tx", 1));
            assert(table.Columns[0].ColumnName == "column_0" && table.Columns[0].Caption == "spid");
        });
        check("Uppercase MDA process headings and SourceName-only aliases populate activity fields", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var table = Table(sourceOnly, " SPID ", "STATUS", "LOGIN", "HOSTNAME", "APPLICATION", "DBID", "DBNAME", "COMMAND",
                    "CPUTIME", "PHYSICALREADS", "MEMUSAGEKB", "BLOCKINGSPID", "SECONDSWAITING", "TRANSACTIONNAME", "SERVERUSERID");
                table.Rows.Add(61, "waiting", "alice", "host", "app", 4, "sales", "SELECT", 7, 8, 9, 55, 10, "tx", 11);
                assert(ActivityMonitor.Parse(table).Single() == new ActivityProcess(61, "waiting", "alice", "host", "app", "sales", "SELECT", 7, 8, 9, 55, 10, "tx", 11));
                table.Columns.RemoveAt(6);
                assert(ActivityMonitor.Parse(table).Single().Database == "4");
            }
            var login = Table(false, "SPID", "suser_name"); login.Rows.Add(55, "sa");
            assert(ActivityMonitor.Parse(login).Single().Login == "sa");
        });
        check("Activity selects the first SPID result after a status result and reports received headings", () => {
            var status = Table(false, "return_status"); status.Rows.Add(0);
            var processes = Table(false, "SPID"); processes.Rows.Add(55);
            var later = Table(false, "SPID"); later.Rows.Add(99);
            var output = Output(status, processes, later);
            assert(ReferenceEquals(ActivityMonitor.PickProcessTable(output.Tables), processes));
            assert(ActivityMonitor.Parse(ActivityMonitor.PickProcessTable(output.Tables)!).Single().Spid == 55);
            assert(ActivityMonitor.PickProcessTable([status]) == null && ActivityMonitor.PickProcessTable([]) == null);
            var bad = Table(true, " a ", "b", "c");
            assert(Reject<ArgumentException>(() => ActivityMonitor.Parse(bad)).Message == "Activity result has no spid column; received: a, b, c");
        });
        check("Activity SQL text resolves live captions/source aliases and preserves fragment whitespace", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var text = Table(sourceOnly, " SQLTEXT "); text.Rows.Add("select 'é' "); text.Rows.Add(DBNull.Value); text.Rows.Add("from t\n");
                assert(ActivityMonitor.ParseSqlText(text) == "select 'é' from t\n");
            }
            assert(ActivityMonitor.ParseSqlText(Table(false, "other")) == "");
        });
        check("Monitor identity uses its alias before the first column and safely handles positional fallback", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var identity = Table(sourceOnly, "other", " MONITOR_SPID "); identity.Rows.Add(999, 55);
                assert(ActivityMonitor.ParseIdentity(identity) == 55);
                identity.Rows[0][1] = DBNull.Value;
                assert(ActivityMonitor.ParseIdentity(identity) == null);
                var fallback = Table(sourceOnly, "unnamed"); fallback.Rows.Add("61");
                assert(ActivityMonitor.ParseIdentity(fallback) == 61);
                foreach (object invalid in new object[] { 0, -1, "invalid", "2147483648", DBNull.Value }) {
                    fallback.Rows[0][0] = invalid; assert(ActivityMonitor.ParseIdentity(fallback) == null);
                }
            }
            assert(ActivityMonitor.ParseIdentity(new DataTable()) == null);
        });
        check("MDA live aliases identify keys/counters and match reordered snapshots with readable chart headings", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var before = Table(sourceOnly, "WaitTime", "WaitEventID", "Waits", "Priority", "ServerUserID");
                before.Rows.Add(100, 1, 10, 5, 42); before.Rows.Add(90, 2, 9, 5, 42); before.Rows.Add(5, 4, 1, 5, 42);
                var after = Table(sourceOnly, "WaitEventID", "Waits", "WaitTime", "Priority", "ServerUserID");
                after.Rows.Add(1, 13, 140, 8, 44); after.Rows.Add(2, 2, 20, 8, 44); after.Rows.Add(3, 4, 30, 8, 44);
                var shape = MdaDelta.Detect(after, "select * from master..monSysWaits")!;
                assert(shape.Keys.SequenceEqual(new[] { "WaitEventID" }) && shape.Counters.SequenceEqual(new[] { "Waits", "WaitTime" }));
                var delta = MdaDelta.Compute(before, after, shape);
                assert(Value(delta.Rows[0], "Waits").Equals(3m) && Value(delta.Rows[0], "WaitTime").Equals(40m));
                assert(Value(delta.Rows[1], MdaDelta.StatusColumn).Equals(MdaDelta.Reset) && Value(delta.Rows[1], "WaitTime").Equals(20m));
                assert(Value(delta.Rows[2], MdaDelta.StatusColumn).Equals(MdaDelta.New) && Value(delta.Rows[2], "Waits").Equals(4m));
                assert(Value(delta.Rows[3], MdaDelta.StatusColumn).Equals(MdaDelta.Gone) && Value(delta.Rows[3], "Waits") is DBNull);
                assert(ResultColumns.Find(delta, MdaDelta.ChartColumn(delta)!) == ResultColumns.Find(delta, "WaitTime"));
                assert(ResultColumns.Find(delta, "WaitTime")!.Caption == after.Columns[2].Caption);
                assert(MdaDelta.Compute(after, after).Rows.Cast<DataRow>().All(row => Value(row, "Waits").Equals(0m)));
                var missing = Table(sourceOnly, "Unrelated", "Waits", "WaitTime");
                Reject<ArgumentException>(() => MdaDelta.Compute(missing, after, shape));
                var process = Table(sourceOnly, "SPID", "KPID", "CPUTime", "MemUsageKB", "ProcNestLevel"); process.Rows.Add(55, 0, 10, 99, 2);
                assert(MdaDelta.Detect(process, "select * from monProcessActivity")!.Counters.SequenceEqual(new[] { "CPUTime" }));
            }
        });
        check("IQ and RS parsers accept live source aliases, normalized metadata headings and existing derived headings", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var status = Table(sourceOnly, " VALUE ", " DESCRIPTION "); status.Rows.Add("app", "Database");
                assert(IqMetrics.Status(status).Rows[0]["Key"].Equals("Database"));
                var spaces = Table(sourceOnly, "Usage %"); spaces.Rows.Add("37.5%");
                var usage = IqMetrics.Dbspaces(spaces);
                assert(usage.Rows[0]["Usage %"].Equals(37.5m) && usage.Columns[0].Caption == "Usage (server)");
                assert(spaces.Columns[0].ColumnName == "column_0" && (string?)spaces.Columns[0].ExtendedProperties["SourceName"] == "Usage %");
                var blocks = Table(sourceOnly, "Used_Blocks", "TOTAL BLOCKS"); blocks.Rows.Add(1, 4);
                assert(IqMetrics.Dbspaces(blocks).Rows[0]["Usage %"].Equals(25m));
                var connections = Table(sourceOnly, "Connection Handle"); connections.Rows.Add(12);
                assert(IqMetrics.Connections(connections).Rows[0][0].Equals(12));
                var transactions = Table(sourceOnly, "State", "Temp_Work_Space_KB"); transactions.Rows.Add("active", 5); transactions.Rows.Add("ACTIVE ", 7);
                assert(IqMetrics.TransactionSummary(transactions).Rows[0]["Temp workspace KB"].Equals(12m));
                var threads = Table(sourceOnly, "Thread_State", "Info"); threads.Rows.Add("DOWN", "s.db");
                assert((bool)RsMonitor.Threads(threads).Rows[0]["Needs attention"]);
                var disk = Table(sourceOnly, "Total_Segs", "Used Segs"); disk.Rows.Add(4, 1);
                assert(RsMonitor.DiskSpace(disk).Rows[0]["Usage %"].Equals(25m));
                var duplicate = Table(sourceOnly, "State", "Needs attention");
                Reject<FormatException>(() => RsMonitor.Threads(duplicate));
                var duplicateUsage = Table(sourceOnly, "Total Segs", "Used Segs", "Usage %");
                Reject<FormatException>(() => RsMonitor.DiskSpace(duplicateUsage));
            }
        });
        check("Object and catalog comparison resolve optional precision/scale and live inventory aliases", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var columns = Table(sourceOnly, " NAME ", "TYPE", "length", "nullable", "identity", " PRECISION ", "SCALE");
                columns.Rows.Add("id", "numeric", 9, 0, 1, 18, 2);
                var indexes = Table(sourceOnly, "index_name", "index_description", "index_keys"); indexes.Rows.Add("pk", "unique", "id");
                string definition = ObjectCompare.TableDefinition(columns, indexes);
                assert(definition.Contains("precision=18 | scale=2") && definition.Contains("index_keys=id"));
                var changed = columns.Copy(); changed.Rows[0][6] = 3;
                assert(!ObjectCompare.ColumnsEqual(columns, changed));
                var inventory = Table(sourceOnly, " ID ", " OWNER ", " NAME "); inventory.Rows.Add(42, "dbo", "t");
                var reader = new QueueEngine(Output(inventory), Output(columns), Output(indexes));
                var snapshot = CatalogSql.ReadObjects(reader, "app", CompareObjectType.Tables).Single();
                assert(snapshot.Key == new ObjectKey("dbo", "t") && snapshot.Definition == definition && snapshot.Error == null && reader.Remaining == 0);
                var text = Table(sourceOnly, "text"); text.Rows.Add("create procedure dbo.p as select "); text.Rows.Add("1\n");
                reader = new QueueEngine(Output(inventory), Output(text));
                assert(CatalogSql.ReadObjects(reader, "app", CompareObjectType.Procedures).Single().Definition == "create procedure dbo.p as select 1\n");
            }
        });
        check("DDL resolves live metadata aliases including fragments, keys, flags and datatypes", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                var header = Table(sourceOnly, "name", "owner", "lock_scheme"); header.Rows.Add("t", "dbo", "datarows");
                var columns = Table(sourceOnly, "ordinal", "name", "type", "is_user_type", "length", "precision", "scale", "nullable", "identity", "status2", "default_id", "rule_id");
                columns.Rows.Add(1, "id", "decimal", 0, 9, 12, 2, 0, 0, 0, 50, 0);
                var keys = Table(sourceOnly, "index_name", "key_ordinal", "key_count", "column_name", "sort_order", "clustered", "is_unique", "primary_key", "unique_constraint");
                keys.Rows.Add("pk", 1, 1, "id", "ASC", 1, 1, 1, 0);
                var foreign = Table(sourceOnly, "constraint_name", "key_ordinal", "key_count", "column_name", "ref_database", "ref_owner", "ref_table", "ref_column", "match_full");
                foreign.Rows.Add("fk", 1, 1, "id", "app", "dbo", "parent", "id", 0);
                var text = Table(sourceOnly, "id", "text"); text.Rows.Add(50, "DEFAULT (7)"); text.Rows.Add(51, "CHECK (id > 0)");
                var checks = Table(sourceOnly, "id", "name"); checks.Rows.Add(51, "ck");
                string ddl = DdlGenerator.Table("app", "dbo", "t", new(header, columns, keys, foreign, text, checks));
                assert(ddl.Contains("[id] decimal(12,2) DEFAULT (7) NOT NULL") && ddl.Contains("PRIMARY KEY CLUSTERED ([id] ASC)"));
                assert(ddl.Contains("REFERENCES [app].[dbo].[parent] ([id])") && ddl.Contains("ADD CONSTRAINT [ck] CHECK (id > 0)"));
                var source = Table(sourceOnly, "text"); source.Rows.Add("create view dbo.v as select "); source.Rows.Add("1\n");
                assert(DdlGenerator.Code("app", source).Contains("select 1\n"));
                var type = Table(sourceOnly, "owner", "name", "type", "is_user_type", "length", "precision", "scale", "nullable", "identity", "default_id", "rule_id");
                type.Rows.Add("dbo", "amount", "decimal", 0, 9, 12, 2, 1, 0, 0, 0);
                assert(DdlGenerator.UserType("app", type).Contains("exec sp_addtype 'amount', 'decimal(12,2)', 'null'"));
            }
        });
        check("Health probes resolve live column aliases without changing threshold rules", () => {
            foreach (bool sourceOnly in new[] { false, true }) {
                HealthCell Evaluate(ServerPlatform platform, string id, DataTable table) => HealthCheck.Evaluate(
                    HealthCheck.Probes(platform).Single(probe => probe.Id == id), Output(table), new());
                var flags = Table(sourceOnly, " NAME ", " STATUS ", " STATUS2 "); flags.Rows.Add("app", 256, 0);
                assert(Evaluate(ServerPlatform.Ase, "databases", flags).State == HealthState.Fail);
                var percent = Table(sourceOnly, " USED_PCT "); percent.Rows.Add(99);
                assert(Evaluate(ServerPlatform.Ase, "data", percent).State == HealthState.Fail);
                var dump = Table(sourceOnly, " AGE_HOURS "); dump.Rows.Add(49);
                assert(Evaluate(ServerPlatform.Ase, "dump", dump).State == HealthState.Fail);
                var blocked = Table(sourceOnly, " BLOCKED_COUNT "); blocked.Rows.Add(2);
                assert(Evaluate(ServerPlatform.Ase, "blocked", blocked).State == HealthState.Warn);
                var uptime = Table(sourceOnly, " BOOT_TIME ", " UPTIME_HOURS "); uptime.Rows.Add("2026-11-04", 3);
                assert(Evaluate(ServerPlatform.Ase, "uptime", uptime).Summary == "3 hours · since 2026-11-04");
                var rs = Table(sourceOnly, " STATUS ", " MODE ", " LOSS STATUS "); rs.Rows.Add("HEALTHY", "NORMAL", "NO LOSS");
                assert(Evaluate(ServerPlatform.Rs, "rshealth", rs).State == HealthState.Ok);
            }
        });
        check("Plan XML extraction, trees and comparison remain independent of result column bindings", () => {
            const string xml = "<?xml version=\"1.0\"?><query><statementNum>1</statementNum><lineNum>1</lineNum><text>select * from t</text><opTree><TableScan><objName>t</objName><est><rowCnt>10</rowCnt></est></TableScan></opTree></query>";
            int split = xml.IndexOf("<opTree>", StringComparison.Ordinal);
            var cells = Table(true, "showplan_in_xml"); cells.Rows.Add(xml[..split]); cells.Rows.Add(xml[split..]);
            var plans = EstimatedPlan.Plans(EstimatedPlan.Xml([cells]));
            var plan = plans.Single();
            assert(plan.Text == "select * from t" && PlanTree.Flatten(plan.Tree).Single().Estimated.Rows == 10);
            var changed = EstimatedPlan.Plans(xml.Replace("<rowCnt>10</rowCnt>", "<rowCnt>20</rowCnt>")).Single();
            assert(PlanCompare.Compare(plan, changed).Single().Changed.SequenceEqual(new[] { "est_rows" }));
        });
    }
}
