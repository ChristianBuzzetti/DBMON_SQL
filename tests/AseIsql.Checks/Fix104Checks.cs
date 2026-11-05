using AseIsql;
using AseIsql.Odbc;
using System.Data;
using System.Text.RegularExpressions;

// 1.0.4: reserved words as aliases in generated ASE SQL, tool row limit, flushmessage at login.
static class Fix104Checks
{
    private const string Frame = "[SAP][ASE ODBC Driver][Adaptive Server Enterprise] ";
    private static readonly ConnectionProfile Ase = new("fixture", 5000, "master", "fixture_user", false, "");
    private static void Assert(bool value, string what = "") { if (!value) throw new Exception("1.0.4 assertion failed. " + what); }
    private static DataTable Numbers(params int[] numbers)
    {
        var table = new DataTable(); table.Columns.Add("n", typeof(int));
        foreach (int number in numbers) table.Rows.Add(number);
        return table;
    }

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) {
        "add", "all", "alter", "and", "any", "arith_overflow", "as", "asc", "at", "authorization", "avg", "begin", "between", "break", "browse", "bulk", "by",
        "cascade", "case", "char_convert", "check", "checkpoint", "close", "clustered", "coalesce", "commit", "compressed", "compute", "confirm", "connect",
        "constraint", "continue", "controlrow", "convert", "count", "count_big", "create", "current", "cursor", "database", "dbcc", "deallocate", "declare",
        "decrypt", "default", "delete", "desc", "deterministic", "disk", "distinct", "double", "drop", "dummy", "dump", "else", "encrypt", "end", "endtran",
        "errlognull", "errordata", "escape", "except", "exclusive", "exec", "execute", "exists", "exit", "exp_row_size", "external", "fetch", "fillfactor",
        "for", "foreign", "from", "goto", "grant", "group", "having", "holdlock", "identity", "identity_gap", "identity_start", "if", "in", "index", "inout",
        "insensitive", "insert", "install", "intersect", "into", "is", "isolation", "jar", "join", "key", "kill", "level", "like", "lineno", "load",
        "lob_compression", "lock", "materialized", "max", "max_rows_per_page", "min", "mirror", "mirrorexit", "modify", "national", "new", "noholdlock",
        "nonclustered", "nonscrollable", "non_sensitive", "not", "null", "nullif", "numeric_truncation", "of", "off", "offsets", "on", "once", "online", "only",
        "open", "option", "or", "order", "out", "output", "over", "partition", "perm", "permanent", "plan", "prepare", "primary", "print", "privileges", "proc",
        "procedure", "processexit", "proxy_table", "public", "quiesce", "raiserror", "read", "readpast", "readtext", "reconfigure", "references", "remove",
        "reorg", "replace", "replication", "reservepagegap", "return", "returns", "revoke", "right", "role", "rollback", "rowcount", "rows", "rule", "save",
        "schema", "scroll", "scrollable", "select", "semi_sensitive", "set", "setuser", "shared", "shutdown", "some", "statistics", "stringsize", "stripe",
        "sum", "syb_identity", "syb_restree", "syb_terminate", "table", "temp", "temporary", "textsize", "to", "tracefile", "tran", "transaction", "trigger",
        "truncate", "tsequal", "union", "unique", "unpartition", "update", "use", "user", "user_option", "using", "values", "varying", "view", "waitfor",
        "when", "where", "while", "with", "work", "writetext", "xmlextract", "xmlparse", "xmlstest", "xmltable" };

    private static readonly Regex Literals = new(@"'(?:[^']|'')*'|--[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex BareAlias = new(@"\bas\s+([A-Za-z_#@][A-Za-z0-9_#@$]*)", RegexOptions.IgnoreCase);
    private static List<string> ReservedAliases(string sql) =>
        BareAlias.Matches(Literals.Replace(sql, " ")).Select(m => m.Groups[1].Value).Where(Reserved.Contains).ToList();

    public static void Run(Action<string, Action> check)
    {
        check("Generated ASE catalog and tool SQL never alias a column with a reserved word", () => {
            Assert(ReservedAliases("select 1 as clustered, 2 as [end], 'x as key' as ok, 3 as Plan -- as user").SequenceEqual(["clustered", "Plan"]), "scanner");
            var sql = new List<(string Name, string Text)> {
                ("AseIndexKeys", CatalogSql.AseIndexKeys("app", "dbo", "orders", 42)),
                ("AseIndexKeys(index)", CatalogSql.AseIndexKeys("app", "dbo", "orders", 42, "ix")),
                ("AseForeignKeys", CatalogSql.AseForeignKeys("app", 42)),
                ("AseTableDetails", CatalogSql.AseTableDetails("app", 42)),
                ("AseUsers", CatalogSql.AseUsers("app", [1, 2])),
                ("AseTableDdl", CatalogSql.AseTableDdl("app", "dbo", "orders", 42)),
                ("AseUserType", CatalogSql.AseUserType("app", 7)),
                ("ProcessesSql", ActivityMonitor.ProcessesSql), ("IdentitySql", ActivityMonitor.IdentitySql),
                ("SqlTextSql", ActivityMonitor.SqlTextSql(5)), ("SqlTextSql(all)", ActivityMonitor.SqlTextSql(5, false)), ("LocksSql", ActivityMonitor.LocksSql(5)),
                ("Comparison text", CatalogSql.ComparisonObjectText("app", 42)), ("Columns", CatalogSql.Columns("app", 42)),
            };
            foreach (var type in Enum.GetValues<CompareObjectType>()) sql.Add(("Objects " + type, CatalogSql.Objects("app", type)));
            foreach (var platform in Enum.GetValues<ServerPlatform>())
                foreach (var probe in HealthCheck.Probes(platform)) sql.Add(("Health " + platform + "/" + probe.Id, probe.Sql));
            foreach (var (name, text) in sql) Assert(ReservedAliases(text).Count == 0, name + " aliases a reserved word: " + string.Join(",", ReservedAliases(text)));
            var keys = CatalogSql.AseIndexKeys("app", "dbo", "orders", 42);
            Assert(keys.Contains("as is_clustered", StringComparison.Ordinal) && !Regex.IsMatch(keys, @"\bas\s+clustered\b", RegexOptions.IgnoreCase));
        });
        check("OdbcSession tool queries read past the profile row limit while user runs stop at it", () => {
            var profile = Ase with { MaxRows = 2 };
            var tool = new ScriptedOdbcConnection().WithFacts(); tool.Add("select tool", Numbers(1, 2, 3, 4, 5));
            using (var session = OdbcSession.Open(profile, "fixture-secret", "", tool))
            {
                var output = session.Execute("select tool", CancellationToken.None, userRun: false);
                Assert(output.Error == null && output.Tables.Single().Rows.Count == 5, "tool " + output.Error);
            }
            var explicitLimit = new ScriptedOdbcConnection().WithFacts(); explicitLimit.Add("select tool", Numbers(1, 2, 3, 4, 5));
            using (var session = OdbcSession.Open(profile, "fixture-secret", "", explicitLimit))
            {
                var output = session.Execute("select tool", CancellationToken.None, userRun: false, maxRows: 3);
                Assert(output.Error!.Contains("Local limit of 3 rows"), "explicit " + output.Error);
            }
            var user = new ScriptedOdbcConnection().WithFacts(); user.Add("select user", Numbers(1, 2, 3, 4, 5));
            using (var session = OdbcSession.Open(profile, "fixture-secret", "", user))
            {
                var output = session.Execute("select user", CancellationToken.None, userRun: true);
                Assert(output.Error!.Contains("Local limit of 2 rows"), "user " + output.Error);
            }
            Assert(OdbcSession.ToolMaxRows == 200_000);
        });
        check("MdaDelta accepts the bounded live-test monOpenObjectActivity snapshot", () => {
            const string sql = "select DBID, ObjectID, IndexID, LogicalReads, PhysicalReads, RowsInserted, RowsDeleted, RowsUpdated, LockRequests, LockWaits from master..monOpenObjectActivity where DBID = db_id()";
            var table = new DataTable();
            foreach (string name in new[] { "DBID", "ObjectID", "IndexID", "LogicalReads", "PhysicalReads", "RowsInserted", "RowsDeleted", "RowsUpdated", "LockRequests", "LockWaits" })
                table.Columns.Add(name, typeof(long));
            table.Rows.Add(4L, 100L, 0L, 10L, 1L, 2L, 0L, 0L, 5L, 0L);
            var shape = MdaDelta.Detect(table, sql);
            Assert(shape != null && shape.Keys.SequenceEqual(["DBID", "ObjectID", "IndexID"]) && shape.Counters.Count == 7 && shape.Table == "monOpenObjectActivity");
        });
        check("ASE login sends set flushmessage on; IQ and RS do not; a failing set never blocks the login", () => {
            var ase = new ScriptedOdbcConnection().WithFacts();
            using (OdbcSession.Open(Ase, "fixture-secret", "", ase))
            {
                Assert(ase.Executed[0].CommandText == "set flushmessage on" && ase.Executed[0].CommandTimeout == 10 && ase.Executed[1].CommandText == ScriptedOdbcConnection.FactsSql && ase.Pending == 0);
            }
            var sibling = new ScriptedOdbcConnection().WithFacts();
            using (var root = OdbcSession.Open(Ase, "fixture-secret", "", new ScriptedOdbcConnection().WithFacts()))
            using (root.OpenSibling(_ => sibling)) Assert(sibling.Executed[0].CommandText == "set flushmessage on" && sibling.Pending == 0);

            var iq = new ScriptedOdbcConnection(); iq.Add("select connection_property('Number'), db_name(), current user, connection_property('CharSet')");
            using (OdbcSession.Open(new ConnectionProfile("iq-host", 2638, "", "dba", false, "", Platform: ServerPlatform.Iq), "fixture-secret", "", iq))
                Assert(iq.Executed.All(c => !c.CommandText.Contains("flushmessage")) && iq.Pending == 0);
            var rs = new ScriptedOdbcConnection();
            using (OdbcSession.Open(new ConnectionProfile("rs-host", 5510, "", "sa", false, "", Platform: ServerPlatform.Rs), "fixture-secret", "", rs))
                Assert(rs.Executed.Count == 0);

            var failing = new ScriptedOdbcConnection();
            failing.Add(ScriptedOdbcConnection.FlushSql).Error = new ScriptedOdbcException(new OdbcDiagnostic(156, "42000", Frame + "Incorrect syntax"));
            failing.WithFacts(flush: false);
            using (var session = OdbcSession.Open(Ase, "fixture-secret", "", failing))
                Assert(session.IsOpen && session.Facts == new SessionFacts("fixture_db", 73, "fixture_login", "utf8") && failing.Pending == 0);
        });
    }
}
