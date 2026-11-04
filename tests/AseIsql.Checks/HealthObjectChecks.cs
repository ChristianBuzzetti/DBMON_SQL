using AseIsql;
using System.Data;

internal static class HealthObjectChecks
{
    public static void Run(Action<string, Action> check, Action<bool> assert)
    {
        static DataTable Table(string[] names, params object[][] values)
        {
            var table = new DataTable();
            for (int i = 0; i < names.Length; i++) { var column = table.Columns.Add("column_" + i, typeof(object)); column.Caption = names[i]; }
            foreach (var row in values) table.Rows.Add(row);
            return table;
        }
        static QueryOutput Output(DataTable table) { var output = new QueryOutput(); output.Tables.Add(table); return output; }
        var settings = new HealthSettings();
        HealthCell Evaluate(ServerPlatform platform, string id, DataTable table) => HealthCheck.Evaluate(HealthCheck.Probes(platform).Single(p => p.Id == id), Output(table), settings);
        check("Health thresholds distinguish strict usage boundaries and inclusive blocked counts", () => {
            assert(HealthCheck.Threshold(85, 85, 95) == HealthState.Ok);
            assert(HealthCheck.Threshold(85.01, 85, 95) == HealthState.Warn);
            assert(HealthCheck.Threshold(95, 85, 95) == HealthState.Warn);
            assert(HealthCheck.Threshold(95.01, 85, 95) == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "blocked", Table(["blocked_count"], [1])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Ase, "blocked", Table(["blocked_count"], [10])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "data", Table(["used_pct"], [10], [96])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "data", Table(["used_pct"], [DBNull.Value])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Ase, "log", Table(["used_pct"], [-1])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Ase, "data", Table(["used_pct"], [96], ["unrecognized"])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "dump", Table(["age_hours"], [49])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "dump", Table(["age_hours"], [DBNull.Value])).State == HealthState.Warn);
            try { (settings with { UsageFailPercent = 84 }).Validate(); assert(false); } catch (ArgumentException) { }
            try { (settings with { ServerTimeoutSeconds = 0 }).Validate(); assert(false); } catch (ArgumentException) { }
        });
        check("Health database flags use status2 for offline and status for suspect/dbo only", () => {
            assert(Evaluate(ServerPlatform.Ase, "databases", Table(["status", "status2"], [0, 16])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "databases", Table(["status", "status2"], [256, 0])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Ase, "databases", Table(["status", "status2"], [2048, 0])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Ase, "databases", Table(["status", "status2"], [512, 0])).State == HealthState.Ok);
        });
        check("IQ health reads main/temp percentages and RS includes loss status", () => {
            assert(Evaluate(ServerPlatform.Iq, "iqspace", Table(["Name", "Value"], ["Main IQ Blocks Used:", "950 of 1000, 95%"], ["Temporary IQ Blocks Used:", "1 of 1000, 0%"], ["Cache in use:", "99%"])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Iq, "iqspace", Table(["Name", "Value"], ["Main IQ Blocks Used:", "96%"], ["Temporary IQ Blocks Used:", "5%"])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Iq, "iqspace", Table(["Name", "Value"], ["Main IQ Blocks Used:", "5%"])).State == HealthState.Warn);
            assert(Evaluate(ServerPlatform.Rs, "rshealth", Table(["Mode", "Status", "Loss Status"], ["NORMAL", "HEALTHY", "SUSPECT"])).State == HealthState.Fail);
            assert(Evaluate(ServerPlatform.Rs, "rshealth", Table(["Mode", "Status", "Loss Status"], ["NORMAL", "HEALTHY", "NO LOSS"])).State == HealthState.Ok);
            assert(Evaluate(ServerPlatform.Rs, "rsdown", Table(["Name", "State"])).State == HealthState.Ok);
            assert(Evaluate(ServerPlatform.Rs, "rsdown", Table(["Name", "State"], ["DSI", "Suspended"])).State == HealthState.Fail);
        });
        check("Health matrix aggregates worst states and marks cross-platform probes inapplicable", () => {
            var version = HealthCheck.Probes(ServerPlatform.Ase).First(); var rs = HealthCheck.Probes(ServerPlatform.Rs).First();
            HealthServer[] servers = [new("left,<tag>", ServerPlatform.Ase, [new(version, HealthState.Warn, "Unknown", "details")]), new("right", ServerPlatform.Rs, [new(rs, HealthState.Fail, "Down", "details")])];
            var matrix = HealthCheck.Matrix(servers);
            assert(servers[0].State == HealthState.Warn && servers[1].State == HealthState.Fail);
            assert(matrix.Rows.Count == 2 && (string)matrix.Rows[0]["probe_rshealth"] == "—" && (string)matrix.Rows[1]["probe_version"] == "—");
            assert(CsvExport.Serialize(matrix).Contains("\"Version\"") && CsvExport.Serialize(matrix).Contains("\"left,<tag>\""));
            assert(ResultFormats.Html(matrix, "", "Health").Contains("&lt;tag&gt;"));
            assert(HealthCheck.Evaluate(version, new QueryOutput { Error = "denied" }, settings).State == HealthState.Fail);
        });
        check("Health runs servers concurrently and isolates probe failures", () => {
            using var ready = new CountdownEvent(2);
            FixtureEngine Worker(bool broken) => new((sql, token) => {
                if (sql == "admin health") { ready.Signal(); if (!ready.Wait(TimeSpan.FromSeconds(5), token)) throw new Exception("Servers did not run concurrently"); }
                if (broken) throw new InvalidOperationException("permission denied");
                return sql == "admin health" ? Output(Table(["Status"], ["HEALTHY"])) : Output(Table(["Name"]));
            });
            var results = HealthCheck.ExecuteAsync([new(new("good", Worker(false)), ServerPlatform.Rs), new(new("bad", Worker(true)), ServerPlatform.Rs)], settings).GetAwaiter().GetResult();
            assert(results[0].State == HealthState.Ok && results[1].State == HealthState.Fail && results.All(r => r.Cells.Count == 2));
        });
        check("Health deadline marks remaining probes and leaves other servers healthy", () => {
            var slow = new FixtureEngine((_, token) => { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return new QueryOutput(); });
            var fast = new FixtureEngine((sql, _) => sql == "admin health" ? Output(Table(["Status"], ["HEALTHY"])) : Output(Table(["Name"])));
            var results = HealthCheck.ExecuteAsync([new(new("slow", slow), ServerPlatform.Rs), new(new("fast", fast), ServerPlatform.Rs)], settings with { ServerTimeoutSeconds = 1 }).GetAwaiter().GetResult();
            assert(results[0].Cells.All(c => c.State == HealthState.Fail) && results[1].State == HealthState.Ok);
        });
        check("Object sets classify missing changed equal and unreadable definitions by owner/name", () => {
            ObjectSnapshot S(string name, string text, string owner = "dbo", string? error = null) => new(new(owner, name), text, error);
            var result = ObjectCompare.Compare([S("same", "x"), S("changed", "a"), S("left", "x"), S("hidden", "", error: "denied"), S("Foo", "x"), S("b.c", "x", "a")],
                [S("same", "x"), S("changed", "b"), S("right", "x"), S("hidden", ""), S("foo", "x"), S("c", "x", "a.b")]);
            assert(result.Single(r => r.Name == "same").Difference == ObjectDifference.Same);
            assert(result.Single(r => r.Name == "changed").Difference == ObjectDifference.Different);
            assert(result.Single(r => r.Name == "left").Difference == ObjectDifference.OnlyLeft);
            assert(result.Single(r => r.Name == "right").Difference == ObjectDifference.OnlyRight);
            assert(result.Single(r => r.Name == "hidden").Difference == ObjectDifference.Unavailable);
            assert(result.Count(r => r.Difference == ObjectDifference.OnlyLeft) == 3);
        });
        check("SQL whitespace normalization preserves literals identifiers and comment terminators", () => {
            assert(ObjectCompare.NormalizeWhitespace(" select   'a  b'  as [a  b]\r\nfrom t ") == "select 'a  b' as [a  b] from t");
            assert(ObjectCompare.NormalizeWhitespace("select 'a b'") != ObjectCompare.NormalizeWhitespace("select 'a  b'"));
            assert(ObjectCompare.NormalizeWhitespace("-- comment\nselect 1") != ObjectCompare.NormalizeWhitespace("-- comment select 1"));
            assert(ObjectCompare.NormalizeWhitespace("-- comment\n   select 1") == ObjectCompare.NormalizeWhitespace("-- comment\nselect 1"));
            var key = new ObjectKey("dbo", "p");
            assert(ObjectCompare.Compare([new(key, "select  1\r\n")], [new(key, "select 1")], true).Single().Difference == ObjectDifference.Same);
        });
        check("LCS line diff reconstructs both inputs including duplicates and line numbers", () => {
            foreach (var (a, b) in new[] { ("a\nb\na\nc", "a\na\nd\nc"), ("", "x"), ("x", ""), ("one\r\ntwo\r\n", "one\ntwo\n"), ("a\nb", "z\nb") })
            {
                var diff = ObjectCompare.LineDiff(a, b);
                assert(string.Join("\n", diff.Where(d => d.Change != LineChange.Added).Select(d => d.Text)) == a.Replace("\r\n", "\n"));
                assert(string.Join("\n", diff.Where(d => d.Change != LineChange.Removed).Select(d => d.Text)) == b.Replace("\r\n", "\n"));
                assert(diff.Where(d => d.LeftLine.HasValue).Select(d => d.LeftLine!.Value).SequenceEqual(Enumerable.Range(1, a.Length == 0 ? 0 : a.Split('\n').Length)));
            }
            var replacement = ObjectCompare.LineDiff("a\nb", "a\nc");
            assert(replacement.Count(d => d.Change == LineChange.Removed) == 1 && replacement.Count(d => d.Change == LineChange.Added) == 1);
            try { ObjectCompare.LineDiff(string.Join("\n", Enumerable.Repeat("a", 2000)), string.Join("\n", Enumerable.Repeat("b", 2000))); assert(false); } catch (ArgumentException) { }
        });
        check("Column comparison detects name type length nullability identity and ordinal changes", () => {
            var columns = Table(["name", "type", "length", "nullable", "identity"], ["id", "int", 4, 0, 1], ["name", "varchar", 20, 1, 0]);
            assert(ObjectCompare.ColumnsEqual(columns, columns.Copy()));
            foreach (var (field, value) in new (int, object)[] { (0, "other"), (1, "bigint"), (2, 8), (3, 1), (4, 0) })
            {
                var changed = columns.Copy(); changed.Rows[0][field] = value;
                assert(!ObjectCompare.ColumnsEqual(columns, changed));
            }
            var reordered = columns.Clone(); reordered.ImportRow(columns.Rows[1]); reordered.ImportRow(columns.Rows[0]);
            assert(!ObjectCompare.ColumnsEqual(columns, reordered));
            var indexes = Table(["index_name", "index_description", "index_keys", "indexID"], ["ix", "unique", "id, name", 2]);
            var other = indexes.Copy(); other.Rows[0][3] = 9;
            assert(ObjectCompare.TableDefinition(columns, indexes) == ObjectCompare.TableDefinition(columns, other));
            other.Rows[0][2] = "name, id";
            assert(ObjectCompare.TableDefinition(columns, indexes) != ObjectCompare.TableDefinition(columns, other));
        });
        check("Object catalog rejects partial inventories and marks protected code unavailable", () => {
            var inventory = Table(["id", "owner", "name"], [100, "dbo", "p"]);
            var reader = new FixtureEngine((sql, _) => sql.Contains("sysobjects") ? Output(inventory) : Output(Table(["text"], [DBNull.Value])));
            var result = CatalogSql.ReadObjects(reader, "db", CompareObjectType.Procedures);
            assert(result.Count == 1 && result[0].Error != null);
            try { CatalogSql.ReadObjects(new FixtureEngine((_, _) => new QueryOutput { Error = "row limit; partial" }), "db", CompareObjectType.Procedures); assert(false); } catch (InvalidOperationException) { }
            assert(CatalogSql.ObjectText("a]b", 100).Contains("[a]]b]..syscomments"));
            assert(CatalogSql.Objects("db", CompareObjectType.Triggers).Contains("'TR'"));
        });
    }
    private sealed class FixtureEngine(Func<string, CancellationToken, QueryOutput> execute) : IQueryEngine
    {
        public string Name => "Health / catalog fixture";
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) => execute(sql, cancellation);
    }
}
