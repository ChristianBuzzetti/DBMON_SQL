using System.Data;
using System.Text.RegularExpressions;
using AseIsql;

// 1.0.1 fixes from the ASE 16 static review (REVIEW_101.md). Tables use ResultReader's shape: binding names in
// ColumnName (column_N), server aliases in Caption and SourceName.
internal static class ReviewFixChecks
{
    private static DataTable Table(params string[] names)
    {
        var table = new DataTable();
        for (int i = 0; i < names.Length; i++) {
            var column = table.Columns.Add("column_" + i, typeof(object));
            column.Caption = names[i]; column.ExtendedProperties["SourceName"] = names[i];
        }
        return table;
    }
    private static QueryOutput Output(params DataTable[] tables) { var output = new QueryOutput(); output.Tables.AddRange(tables); return output; }
    private sealed class QueueEngine(params QueryOutput[] outputs) : IQueryEngine
    {
        private readonly Queue<QueryOutput> pending = new(outputs);
        public string Name => "Review fix check";
        public int Remaining => pending.Count;
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) => pending.Dequeue();
    }

    internal static void Run(Action<string, Action> check, Action<bool> assert)
    {
        check("Health data/log/dump probes separate mixed data+log databases and label them in the data probe", () => {
            var probes = HealthCheck.Probes(ServerPlatform.Ase).ToDictionary(p => p.Id);
            const string mixed = "not exists (select 1 from master..sysusages m where m.dbid = d.dbid and (m.segmap & 4) != 0 and (m.segmap & 3) != 0)";
            assert(probes["log"].Sql.Contains(mixed) && probes["dump"].Sql.Contains(mixed) && !probes["data"].Sql.Contains(mixed));
            assert(probes["data"].Sql.Contains("u.segmap != 4") && probes["data"].Sql.Contains("'data+log'") && probes["data"].Sql.Contains("as segments"));
            assert(probes["dump"].Sql.Contains("dbid > 3 and name not like 'syb%' and (status & 8)=0") && probes["dump"].Sql.Contains("(isnull(status3,0) & 4194304)=0"));
            var data = Table("database_name", "total_pages", "free_pages", "used_pct", "segments");
            data.Rows.Add("master", 1000, 394.5, 60.55, "data+log"); data.Rows.Add("sales", 5000, 3000, 40.0, "data");
            var cell = HealthCheck.Evaluate(probes["data"], Output(data), new HealthSettings());
            assert(cell.State == HealthState.Ok && cell.Summary == "max 60.55% used" && cell.Details.Contains("data+log"));
            data.Rows.Add("busy", 100, 1, 99.0, "data");
            assert(HealthCheck.Evaluate(probes["data"], Output(data), new HealthSettings()).State == HealthState.Fail);
        });
        check("Object compare reads the sp_helpindex index list among several result sets", () => {
            var inventory = Table("id", "owner", "name"); inventory.Rows.Add(42, "dbo", "t");
            var columns = Table("name", "type", "length", "nullable", "identity", "precision", "scale"); columns.Rows.Add("id", "int", 4, 0, 0, 0, 0);
            var indexes = Table("index_name", "index_description", "index_keys"); indexes.Rows.Add("pk", "clustered, unique", "id");
            var partitions = Table("index_ptn_name", "index_ptn_seg"); partitions.Rows.Add("pk_123", "default");
            string expected = ObjectCompare.TableDefinition(columns, indexes);
            foreach (var order in new[] { new[] { indexes, partitions }, new[] { partitions, indexes } }) {
                var engine = new QueueEngine(Output(inventory), Output(columns), Output(order));
                var snapshot = CatalogSql.ReadObjects(engine, "app", CompareObjectType.Tables).Single();
                assert(snapshot.Error == null && snapshot.Definition == expected && engine.Remaining == 0);
            }
            var broken = CatalogSql.ReadObjects(new QueueEngine(Output(inventory), Output(columns), Output(partitions)), "app", CompareObjectType.Tables).Single();
            assert(broken.Error == "Column or index metadata is unavailable.");
        });
        check("Activity SQL text orders by BatchID, LineNumber, SequenceInLine and matches the running statement", () => {
            string current = ActivityMonitor.SqlTextSql(21), all = ActivityMonitor.SqlTextSql(21, false);
            assert(current.Contains("master..monProcessStatement s where s.SPID = t.SPID and s.KPID = t.KPID and s.BatchID = t.BatchID") && current.EndsWith("order by t.BatchID, t.LineNumber, t.SequenceInLine", StringComparison.Ordinal));
            assert(all.EndsWith("where SPID = 21 order by BatchID, LineNumber, SequenceInLine", StringComparison.Ordinal));
            assert(!(current + all).Contains("SequenceInBatch") && !(current + all).Contains("monSysStatement"));
        });
        check("PlanTree skips VA leaves, finds an opTree nested under plan and rounds fractional estimates", () => {
            const string xml = """
<?xml version="1.0" encoding="UTF-8"?>
<query><statementNum>1</statementNum><lineNum>1</lineNum><text>select * from t where id = 1</text>
<plan><opTree><Emit><VA>1</VA><est><rowCnt>4.33</rowCnt><lio>2.5</lio></est><act><rowCnt>4</rowCnt></act>
  <TableScan><VA>0</VA><est><rowCnt>4.6</rowCnt><lio>7</lio><pio>0.4</pio></est><act><rowCnt>4</rowCnt><lio>7</lio><pio>0</pio></act><objName>db..t</objName></TableScan>
</Emit></opTree></plan></query>
""";
            var plan = ExecutionIo.Parse(xml).Plans.Single();
            var nodes = PlanTree.Flatten(plan.Tree).ToList();
            assert(string.Join(",", nodes.Select(n => n.Operator)) == "Emit,TableScan" && nodes[1].Key == "Emit/TableScan[db..t]");
            assert(nodes[0].Estimated == new PlanMetrics(4, 3, null) && nodes[1].Estimated == new PlanMetrics(5, 7, 0));
            var scan = plan.Scans.Single();
            assert(scan.Object == "db..t" && scan.EstimatedRows == 5 && scan.EstimatedLogicalReads == 7 && scan.ActualRows == 4);
            assert(PlanTree.Parse(xml.Replace("4.6", "not a number")).Single().Children.Single().Estimated.Rows == null);
        });
        check("A run keeps up to 20,000 messages or 8 MiB of text, the pane keeps 250 + 250, and sp_sysmon survives whole", () => {
            var output = new QueryOutput();
            for (int i = 0; i < 25_000; i++) output.Messages.Add(new QueryMessage(0, "ASE", "m" + i));
            var rows = output.Messages.Snapshot(n => new QueryMessage(0, "CLIENT", HeadTailBuffer<QueryMessage>.OmissionText(n)));
            assert(output.Messages.Count == 20_000 && output.OmittedMessages == 5_000 && rows.Count == 20_001);
            assert(rows[9_999].Text == "m9999" && rows[10_000].Text == HeadTailBuffer<QueryMessage>.OmissionText(5_000) && rows[10_001].Text == "m15000" && rows[^1].Text == "m24999");
            var large = new QueryOutput(); string line = new('x', 1000);
            for (int i = 0; i < 10_000; i++) large.Messages.Add(new QueryMessage(i, "ASE", line));
            long characters = large.Messages.Sum(m => (long)m.Text.Length);
            assert(large.OmittedMessages > 0 && large.Messages.Count + large.OmittedMessages == 10_000 && characters <= QueryOutput.MessageCharacterLimit && characters > QueryOutput.MessageCharacterLimit - 2_000);
            assert(large.Messages[0].Number == 0 && large.Messages[^1].Number == 9_999 && ResultFormats.Isql(large).Contains(HeadTailBuffer<QueryMessage>.OmissionText(large.OmittedMessages)));
            large.Messages.Clear(); large.Messages.Add(new QueryMessage(0, "ASE", "again"));
            assert(large.Messages.Count == 1 && large.OmittedMessages == 0);
            var pane = new HeadTailBuffer<int>();
            for (int i = 0; i < 1000; i++) pane.Add(i);
            assert(pane.Count == 500 && pane.OmittedCount == 500);
            string[] headings = ["Kernel Utilization", "Task Management", "Housekeeper Task Activity", "Monitor Access to Executing SQL", "Lock Management",
                "Data Cache Management", "Procedure Cache Management", "Disk I/O Management", "Network I/O Management", "Replication Agent", "Tempdb Activity"];
            var sysmon = new QueryOutput();
            foreach (string heading in headings) {
                sysmon.Messages.Add(new QueryMessage(0, "ASE", heading)); sysmon.Messages.Add(new QueryMessage(0, "ASE", new string('-', heading.Length)));
                for (int i = 0; i < 250; i++) sysmon.Messages.Add(new QueryMessage(0, "ASE", "  counter " + i + "   12.3   45.6   7"));
            }
            assert(sysmon.OmittedMessages == 0 && ResultFormats.SysmonSections(ResultFormats.Isql(sysmon)).Select(s => s.Heading).SequenceEqual(headings));
            assert(ResultFormats.SysmonSections("Thread Utilization\n---\nRun Queue\n---").Count == 2);
        });
        check("PROD guard flags login, replication, binding, space, setting, lock table, sp_sysmon and RS state changes", () => {
            var cases = new (string Sql, string Kind)[] {
                ("exec sp_locklogin bob, 'lock'", "sp_locklogin"), ("sp_role 'grant', sa_role, bob", "sp_role"), ("sp_changedbowner bob", "sp_changedbowner"), ("sp_changegroup g, bob", "sp_changegroup"),
                ("exec sp_setreptable t, 'true'", "sp_setreptable"), ("sp_setrepproc p, 'table'", "sp_setrepproc"), ("sp_reptostandby sales, 'all'", "sp_reptostandby"),
                ("sp_setrepdbmode sales, 'UDI', 'on'", "sp_setrepdbmode"), ("sp_config_rep_agent sales, 'enable', rs, sa, pwd", "sp_config_rep_agent"),
                ("sp_start_rep_agent sales", "sp_start_rep_agent"), ("sp_stop_rep_agent sales", "sp_stop_rep_agent"),
                ("sp_bindefault d, 't.c'", "sp_bindefault"), ("sp_bindrule r, 't.c'", "sp_bindrule"), ("sp_unbindefault 't.c'", "sp_unbindefault"), ("sp_unbindrule 't.c'", "sp_unbindrule"),
                ("sp_bindcache c, sales", "sp_bindcache"), ("sp_unbindcache c, sales", "sp_unbindcache"),
                ("sp_extendsegment seg, sales, dev2", "sp_extendsegment"), ("sp_placeobject seg, t", "sp_placeobject"), ("sp_logdevice sales, logdev", "sp_logdevice"),
                ("sp_cacheconfig 'c', '100M'", "sp_cacheconfig"), ("exec sp_poolconfig 'default data cache', '16M', '16K'", "sp_poolconfig"), ("sp_modifythreshold sales, logsegment, 200, p", "sp_modifythreshold"),
                ("sp_chgattribute t, 'exp_row_size', 100", "sp_chgattribute"), ("sp_recompile t", "sp_recompile"), ("sp_hidetext p", "sp_hidetext"), ("sp_tempdb 'bind', 'lg', 'sa', 'DB', 'tempdb2'", "sp_tempdb"),
                ("sp_audit 'login', 'all', 'all', 'on'", "sp_audit"), ("sp_logintrigger 'p'", "sp_logintrigger"),
                ("lock table t in exclusive mode", "lock table"), ("sp_sysmon '00:01:00'", "sp_sysmon"), ("sp_sysmon \"00:01:00\" -- noclear", "sp_sysmon"),
                ("switch active for ws.sales to ds2.sales", "switch active"), ("configure replication server set memory_limit to '4000'", "configure replication server"),
                ("admin quiesce_force_rsi", "admin quiesce_force_rsi") };
            foreach (var (sql, kind) in cases)
                if (SqlRisk.StateChanges(sql).FirstOrDefault() != kind) throw new Exception($"{sql} → {string.Join(", ", SqlRisk.StateChanges(sql))}, expected {kind}");
            foreach (string safe in new[] {
                "exec sp_help t\nexec sp_helpindex t\nexec sp_helpdb\nexec sp_who\nexec sp_lock\nexec sp_spaceused t\nexec sp_helprotect t\nexec sp_helprole",
                "sp_configure\ngo\nsp_configure 'number of locks'\ngo\nsp_cacheconfig\ngo\nsp_cacheconfig 'default data cache'\ngo\nsp_poolconfig 'default data cache'",
                "sp_sysmon '00:01:00', noclear\ngo\nsp_sysmon \"00:05:00\", 'noclear'\ngo\nselect 'lock table t' -- switch active\nadmin who_is_down\nadmin quiesce_check" })
                if (SqlRisk.StateChanges(safe).Count != 0) throw new Exception("False positive: " + string.Join(", ", SqlRisk.StateChanges(safe)));
            var findings = SqlRisk.Findings("select 1\nsp_sysmon '00:01:00', noclear\nsp_sysmon '00:01:00'");
            assert(findings.Single() is { Kind: "sp_sysmon", Line: 3 });
            var byId = Recipes();
            assert(SqlRisk.StateChanges(byId["ase_act_locklogin"]).Contains("sp_locklogin") && SqlRisk.StateChanges(byId["ase_act_setreptable"]).Contains("sp_setreptable"));
        });
        check("MDA deltas no longer treat history pipes or gauges (monSysStatement, monSysLoad, monLicense) as counters", () => {
            foreach (string table in new[] { "monSysStatement", "monSysLoad", "monLicense" })
                assert(MdaDelta.MonitorTable("select * from master.." + table) == null);
            assert(MdaDelta.MonitorTable("select * from master..monProcessStatement") == "monProcessStatement" && MdaDelta.MonitorTable("select * from master..monSysWaits") == "monSysWaits");
        });
        check("ASE recipes use exact MDA column case, 2K device pages with vdevno, @@maxpagesize and mixed-log space", () => {
            var byId = Recipes();
            var lower = new Regex(@"\b(?:mp|mps|mpa|m|l|mLocks)\.(?:spid|kpid|serveruserid|command|dbname|application|blockingspid|secondswaiting|secondsconnected|waiteventid|cputime|logicalreads|physicalreads|memusagekb|rowsaffected|dbid|objectid|ObjectId|locktype|lockLevel|login)\b");
            foreach (string id in new[] { "ase_top10_lreads", "ase_top10_cpu", "ase_sesiones_activas", "ase_procesos_panorama", "ase_idle_largas", "ase_blocking", "ase_locks_spid", "ase_locks_tabla", "ase_tx_zombies", "ase_act_gen_kill_idle" })
                if (lower.Match(byId[id]) is { Success: true } match) throw new Exception(id + ": " + match.Value);
            assert(byId["ase_locks_spid"].Contains("l.ObjectID") && byId["ase_locks_spid"].Contains("l.LockType") && byId["ase_top10_cpu"].Contains("mps.CPUTime"));
            string devices = byId["ase_devices_libres"];
            assert(devices.Contains("u.vdevno = d.vdevno") && !devices.Contains("16777216") && devices.Contains("convert(bigint, d.high - d.low + 1) * 2 / 1024") && devices.Contains("sum(convert(bigint, u.size)) * (@@maxpagesize/1024)"));
            assert(byId["ase_top_tablas"].Contains("c.pagecnt) * (@@maxpagesize/1024)") && !byId["ase_top_tablas"].Contains("* 2)"));
            assert(byId["ase_space_db"].Contains("(segmap & 4) = 0") && byId["ase_space_db"].Contains("data_log_mb"));
        });
    }

    private static Dictionary<string, string> Recipes()
    {
        using var recipes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AseIsql.Desktop", "Assets", "ase-recipes.json")));
        return recipes.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("sql").GetString()!);
    }
}
