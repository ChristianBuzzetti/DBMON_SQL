using AseIsql;
using System.Data;
using System.Globalization;
using AseIsql.Odbc;
using System.Data.Odbc;

int passed = 0;
void Check(string name, Action body) { body(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
var engine = new DemoQueryEngine();
Check("INSERT target keeps bracketed and quoted parts containing dots", () => {
    var t = new DataTable(); t.Columns.Add("v", typeof(int)); t.Rows.Add(1);
    Assert(ResultFormats.Inserts(t, "[dbo].[a.b]") == "INSERT INTO [dbo].[a.b] ([v]) VALUES (1);");
    Assert(ResultFormats.Inserts(t, " sales . \"own.er\" . [x]]y] ") == "INSERT INTO [sales].[own.er].[x]]y] ([v]) VALUES (1);");
    Assert(ResultFormats.QualifiedName("my table").Single() == "my table");
    foreach (string bad in new[] { "[open", "a..b", "a.b.c.d", "a]b", "[a]b", "" }) { try { ResultFormats.Inserts(t, bad); Assert(false); } catch (ArgumentException) { } }
});
Check("Typed constants and NULL", () => {
    var output = engine.Execute("select 42 as n, 'hola' as texto, NULL as vacio");
    Assert(output.Error == null && output.Tables.Count == 1);
    Assert((decimal)output.Tables[0].Rows[0][0] == 42 && (string)output.Tables[0].Rows[0][1] == "hola" && output.Tables[0].Rows[0][2] is DBNull);
});
Check("Quoted delimiters and apostrophes", () => {
    var output = engine.Execute("SELECT 'O''Brien,;--/*GO*/' AS [display name]");
    Assert(output.Error == null && (string)output.Tables[0].Rows[0][0] == "O'Brien,;--/*GO*/");
});
Check("GO and semicolon batches", () => Assert(engine.Execute("select 1\nGO\nselect 2; select 3\ngo --comment").Tables.Count == 3));
Check("GO inside multiline string", () => Assert((string)engine.Execute("select 'a\nGO\nb' as s").Tables[0].Rows[0][0] == "a\nGO\nb"));
Check("Line and block comments", () => Assert(engine.Execute("-- hello\nselect /* internal */ 1 as n; /*end*/").Error == null));
Check("Invariant decimal parsing", () => { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR"); Assert((decimal)engine.Execute("select -10.13").Tables[0].Rows[0][0] == -10.13m); });
Check("Unterminated strings fail", () => Assert(engine.Execute("select 'broken").Error != null));
Check("Unterminated comments fail", () => Assert(engine.Execute("select 1 /*broken").Error != null));
Check("Database queries rejected", () => Assert(engine.Execute("select 1 from business_table").Error != null));
Check("DBCC rejected, never simulated", () => Assert(engine.Execute("dbcc sqltext(1)").Tables.Count == 0 && engine.Execute("dbcc sqltext(1)").Error != null));
Check("Mutation statements rejected", () => Assert(engine.Execute("delete from business_table").Error != null));
Check("Unsupported numeric functions rejected", () => Assert(engine.Execute("select getdate()").Error != null));
Check("Duplicate aliases rejected", () => Assert(engine.Execute("select 1 as n, 2 as n").Error != null));
Check("Trailing garbage rejected", () => Assert(engine.Execute("select 1 as n garbage").Error != null));
Check("Overflow explained", () => Assert(engine.Execute("select " + new string('9', 60)).Error != null));
Check("Empty script explained", () => Assert(engine.Execute("-- no SQL").Error != null));
Check("Long malformed whitespace bounded by linear matcher", () => Assert(engine.Execute("select 1" + new string(' ', 90000) + "bad").Error != null));
Check("Cancellation honored before parsing", () => {
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { engine.Execute("select 1", cancellation.Token); throw new Exception("Cancellation was ignored"); }
    catch (OperationCanceledException) { }
});
Check("Result capacity bounded with partial facts", () => { var output = engine.Execute(string.Concat(Enumerable.Repeat("select 1;", 33))); Assert(output.Error != null && output.Tables.Count == 32); });
Check("Column capacity bounded", () => Assert(engine.Execute("select " + string.Join(",", Enumerable.Repeat("1", 129))).Error != null));
Check("Script capacity bounded", () => Assert(engine.Execute(new string(' ', 100001)).Error != null));
Check("Partial successful results retained", () => { var output = engine.Execute("select 1; select getdate()"); Assert(output.Error != null && output.Tables.Count == 1); });
Check("CSV escaping and source values", () => {
    var table = engine.Execute("select 'a,\"b' as texto, 10.13 as n, NULL as vacio").Tables[0];
    var csv = CsvExport.Serialize(table);
    Assert(csv.Contains("\"a,\"\"b\"", StringComparison.Ordinal) && csv.Contains("\"10.13\",\"\"", StringComparison.Ordinal));
});
Check("Atomic save backs up original and preserves UTF8", () => {
    string folder = Path.Combine(Path.GetTempPath(), "ase-isql-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try {
        string file = Path.Combine(folder, "query.sql"); AtomicFiles.SaveUtf8(file, "select 'original'"); AtomicFiles.SaveUtf8(file, "select 'á漢字'");
        Assert(File.ReadAllText(file) == "select 'á漢字'");
        Assert(File.ReadAllText(Directory.GetFiles(folder, "*.bak").Single()) == "select 'original'");
        Assert(Directory.GetFiles(folder, "*.tmp").Length == 0);
    } finally { Directory.Delete(folder, true); }
});
Check("Server batches preserve original comments, semicolons and CRLF", () => {
    string first = "-- note\r\nDECLARE @n int; SELECT 1;\r\n";
    var batches = SqlScript.Split(first + "GO\r\nSELECT 2"); Assert(batches.Count == 2 && batches[0] == first && batches[1] == "SELECT 2");
});
Check("GO inside identifiers and nested comments is not a boundary", () => Assert(SqlScript.Split("SELECT [a\nGO\nb]; /* outer /* inner */\nGO\n*/ SELECT 2;").Count == 1));
Check("ODBC builder preserves password and caller fields without injection", () => {
    var profile = new ConnectionProfile("test;UID=other", 5000, "db;name", "my-user", false, "");
    const string secret = "dummy;}p;UID=unexpected";
    var builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile, secret));
    // ODBC's builder retains brace quoting in parsed values; the native driver
    // decodes it. Verify exact emitted grammar and independent keyword identity.
    Assert((string)builder["PWD"] == "{dummy;}}p;UID=unexpected}" && (string)builder["Server"] == "{test;UID=other}" && (string)builder["UID"] == profile.User && builder.Count == 12);
    Assert((string)builder["Encryption"] == "none" && (string)builder["EncryptPassword"] == "1");
});
Check("Every connection identifies itself as DBMON SQL and no caller field can change it", () => {
    const string attack = "x;ApplicationName=evil;APP=evil;AppName=evil";
    var profile = new ConnectionProfile(attack, 5000, attack, attack, false, "", Charset: attack, ClientHostname: attack, Language: attack);
    var builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile, attack));
    Assert(OdbcSession.ApplicationName == "DBMON SQL" && (string)builder["ApplicationName"] == "DBMON SQL" && !builder.ContainsKey("APP") && !builder.ContainsKey("AppName") && builder.Count == 13);
    Assert((string)new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(new ConnectionProfile("h", 5000, "db", "u", false, ""), ""))["ApplicationName"] == "DBMON SQL");
});
Check("Connection validation rejects invalid port before networking", () => {
    try { new ConnectionProfile("test", 0, "db", "user", true, "").Validate(); throw new Exception("Accepted invalid port"); } catch (ArgumentException) { }
});
Check("Only explicit password redacted, source names preserved", () => Assert(OdbcSession.Redact("server=test; pwd=dummy-password; db=secret", "dummy-password") == "server=test; pwd=[password]; db=secret"));
Check("Production result reader preserves source types, captions, NULL and multiple results", () => {
    var source = engine.Execute("select 'á漢字' as texto, NULL as vacio, 10.13 as n").Tables[0];
    using var reader = new StreamingTestReader(source, source.Copy()); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(100), CancellationToken.None);
    Assert(output.Tables.Count == 2 && output.Tables[0].Columns[0].Caption == "texto" && (string)output.Tables[0].Rows[0][0] == "á漢字" && output.Tables[0].Rows[0][1] is DBNull && (decimal)output.Tables[0].Rows[0][2] == 10.13m);
    Assert(CsvExport.Serialize(output.Tables[0]).StartsWith("\"texto\",\"vacio\",\"n\""));
});
Check("Production reader keeps partial rows at capacity", () => {
    var source = new DataTable(); source.Columns.Add("id", typeof(int)); source.Rows.Add(1); source.Rows.Add(2);
    using var reader = new StreamingTestReader(source); var output = new QueryOutput();
    try { ResultReader.Read(reader, output, new ResultBudget(1), CancellationToken.None); throw new Exception("Limit ignored"); } catch (ResultLimitException) { }
    Assert(output.Tables[0].Rows.Count == 1 && (int)output.Tables[0].Rows[0][0] == 1);
});
Check("Production reader rejects oversized cells without fake truncation", () => {
    var source = new DataTable(); source.Columns.Add("text", typeof(string)); source.Rows.Add(new string('x', ResultBudget.MaxCellBytes));
    using var reader = new StreamingTestReader(source); var output = new QueryOutput();
    try { ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None); throw new Exception("Cell limit ignored"); } catch (ResultLimitException) { }
    Assert(output.Tables[0].Rows.Count == 0);
});
Check("sql.ini reads ordered query endpoints and preserves client alias", () => {
    var catalog = SqlIniParser.Parse(";comment\n[ASE_QA]\nmaster=TCP,listener,9999\nquery=NLWNSCK,host,5005\nQUERY=TCP,second,5010,SSL\n", "fixture.ini");
    var server = catalog.Servers.Single();
    Assert(server.Name == "ASE_QA" && server.Queries.Count == 2 && server.Queries[0].Host == "host" && server.Queries[0].Port == 5005 && server.Queries[1].UseTls && !server.Duplicate);
    Assert(catalog.SourcePath == "fixture.ini" && catalog.Fingerprint == SqlIniParser.Parse(";comment\n[ASE_QA]\nmaster=TCP,listener,9999\nquery=NLWNSCK,host,5005\nQUERY=TCP,second,5010,SSL\n").Fingerprint);
});
Check("sql.ini master-only aliases have no client endpoint", () => Assert(SqlIniParser.Parse("[listener]\nmaster=TCP,host,5000").Servers.Single().Queries.Count == 0));
Check("sql.ini duplicate aliases remain visible and ambiguous", () => {
    var catalog = SqlIniParser.Parse("[QA]\nquery=TCP,a,5000\n[qa]\nquery=TCP,b,5001");
    Assert(catalog.Servers.Count == 2 && catalog.Servers.All(s => s.Duplicate) && catalog.Diagnostics.Count == 1);
});
Check("sql.ini unsupported and malformed endpoints retain evidence", () => {
    var catalog = SqlIniParser.Parse("[x]\nquery=PIPE,h,5000\nquery=TCP,,5000\nquery=TCP,h,service-name\nquery=TCP,h,70000\nquery=TCP,h,5000,unknown\nquery=TCP,h\nquery=TCP,h,5000,ssl,extra");
    Assert(catalog.Servers.Single().Queries.Count == 7 && catalog.Servers.Single().Queries.All(q => !q.IsUsable && q.Line > 1 && q.RawValue.Length > 0));
});
Check("sql.ini invalid sections do not leak entries into preceding alias", () => {
    var catalog = SqlIniParser.Parse("[good]\nquery=TCP,h,5000\n[broken\nquery=TCP,wrong,5001\n[IPv6] ; comment\nquery=TCP,::1,5002");
    Assert(catalog.Servers.Count == 2 && catalog.Servers[0].Queries.Count == 1 && catalog.Servers[1].Queries.Single().Host == "::1" && catalog.Diagnostics.Count == 2);
});
Check("ODBC forwards charset hostname and password encryption independently", () => {
    var profile = new ConnectionProfile("fixture", 5000, "master", "fixture-user", false, "", Charset: "utf8", ClientHostname: "fixture-client", EncryptPassword: false, ServerAlias: "display-alias", SqlIniPath: "fixture.ini");
    var builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile, "fixture-password"));
    Assert((string)builder["CharSet"] == "utf8" && (string)builder["ClientHostName"] == "fixture-client" && (string)builder["EncryptPassword"] == "0" && !builder.ContainsKey("ServerAlias") && !builder.ContainsKey("SqlIniPath"));
});
Check("Malformed saved profile null fields reject before networking", () => {
    try { new ConnectionProfile("fixture", 5000, "master", "user", true, null!).Validate(); throw new Exception("Accepted null certificate field"); } catch (ArgumentException) { }
});
Check("TLS without CA file fails before native driver access", () => {
    try { OdbcSession.BuildConnectionString(new ConnectionProfile("fixture", 5000, "master", "user", true, ""), "fixture-password"); throw new Exception("Accepted incomplete TLS configuration"); }
    catch (ArgumentException ex) { Assert(ex.Message.Contains("TrustedFile") && ex.Message.Contains("Encrypt password")); }
});
Check("TLS CA and password encryption retain independent caller choices", () => {
    string file = Path.GetTempFileName();
    try {
        var tlsProfile = new ConnectionProfile("fixture", 5000, "master", "user", true, file, EncryptPassword: false);
        var tls = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(tlsProfile, "fixture-password"));
        var tcp = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(tlsProfile with { UseTls = false, EncryptPassword = true }, "fixture-password"));
        Assert((string)tls["Encryption"] == "ssl" && (string)tls["EncryptPassword"] == "0" && (string)tls["TrustedFile"] == file);
        Assert((string)tcp["Encryption"] == "none" && (string)tcp["EncryptPassword"] == "1" && !tcp.ContainsKey("TrustedFile"));
    } finally { File.Delete(file); }
});
Check("SC001 certificate list diagnostic does not misclassify other crypto failures", () => {
    Assert(OdbcSession.IsCertificateAuthorityFailure("SC001", "The crypto provider encountered an error while setting the certificate authority list."));
    Assert(!OdbcSession.IsCertificateAuthorityFailure("SC001", "The requested crypto provider is unavailable."));
    Assert(!OdbcSession.IsCertificateAuthorityFailure("28000", "Login failed"));
    Assert(!OdbcSession.DescribeConnectionFailure(new ArgumentException("fixture-password was rejected"), "fixture-password").Contains("fixture-password"));
});
Check("Delimited copy escapes separators newlines quotes and preserves Unicode", () => {
    string text = DelimitedText.Serialize(["head,one", "two"], [new object?[] { "á,漢\nline", "a\"b" }], ',', includeHeaders: true);
    Assert(text == "\"head,one\",two\r\n\"á,漢\nline\",\"a\"\"b\"\r\n");
});
Check("Clipboard text uses full source binary date invariant numbers and NULL", () => {
    var date = new DateTime(2026, 10, 13, 12, 30, 0);
    string text = DelimitedText.Serialize(["binary", "date", "number", "null"], [new object?[] { new byte[] { 0, 255 }, date, 10.13m, DBNull.Value }], '|');
    Assert(text == "0x00FF|" + date.ToString("O", CultureInfo.InvariantCulture) + "|10.13|\r\n");
});
Check("Headers-only clipboard never enumerates source rows", () => {
    var brokenRows = Enumerable.Range(0, 1).Select<int, IReadOnlyList<object?>>(_ => throw new InvalidOperationException("Enumerated rows"));
    Assert(DelimitedText.Serialize(["a", "b"], brokenRows, headersOnly: true) == "a\tb\r\n");
});
Check("Clipboard output limits account for quote and binary expansion", () => {
    foreach (object value in new object[] { "\"\"\"", new byte[20] }) {
        try { DelimitedText.Serialize(["c"], [new object?[] { value }], ',', maxCharacters: 8); throw new Exception("Limit ignored"); }
        catch (ArgumentException) { }
    }
});
Check("Explicit language and server charset replace ambient Windows defaults", () => {
    var profile=new ConnectionProfile("fixture",5000,"master","user",false,"");
    var builder=new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile,"fixture-password"));
    Assert((string)builder["Language"]=="us_english" && (string)builder["CharSet"]=="ServerDefault");
    var customized=new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile with {Charset="utf8",Language="spanish"},"fixture-password"));
    Assert((string)customized["Language"]=="spanish" && (string)customized["CharSet"]=="utf8");
    Assert((string)new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile with {Charset=""},"fixture-password"))["CharSet"]=="ServerDefault");
});
Check("Cumulative SAP diagnostics display only appended frame with raw evidence",()=> {
    var reader=new DiagnosticMessages();
    const string first="[SAP][ASE ODBC Driver]Language warning.\n",second="[SAP][ASE ODBC Driver]Changed client character set.";
    Assert(reader.Read(4017,"ZZZZZ",first,"").Text==first);
    var message=reader.Read(5704,"ZZZZZ",first+second,"");
    Assert(message.Text==second && message.RawText==first+second && message.Number==5704);
    Assert(reader.Read(5704,"ZZZZZ",first+second,"").Text==first+second);
    Assert(reader.Read(1,"ZZZZZ","independent message","").Text=="independent message");
});
Check("Diagnostic normalizer preserves unframed payload and redacts raw transport secret",()=>{
    var reader=new DiagnosticMessages(); reader.Read(1,"x","prefix","");
    Assert(reader.Read(2,"x","prefixsuffix","").Text=="prefixsuffix");
    var message=reader.Read(3,"x","fixture-password","fixture-password");
    Assert(message.Text=="[password]" && message.RawText=="[password]");
});
Check("Known SAP transport frames are hidden without changing original diagnostic evidence",()=>{
    const string prefix="[SAP][ASE ODBC Driver][Adaptive Server Enterprise]";
    var reader=new DiagnosticMessages();
    string first=prefix+" Changed database context.\r\n",second=prefix+" Server warning.";
    Assert(reader.Read(5701,"01000",first,"").Text=="Changed database context.\r\n");
    var appended=reader.Read(5703,"01000",first+second,"");
    Assert(appended.Text=="Server warning." && appended.RawText==first+second);
    Assert(reader.Read(5703,"01000",first+second,"").Text=="Changed database context.\r\nServer warning.");
    Assert(reader.Read(1,"x","Payload mentions "+prefix,"").Text=="Payload mentions "+prefix);
    Assert(reader.Read(1,"x","[SAP][Other driver]message","").Text=="[SAP][Other driver]message");
    var secret=reader.Read(1,"x",prefix+" fixture-password","fixture-password");
    Assert(secret.Text=="[password]" && secret.RawText==prefix+" [password]");
});
Check("JSON exports duplicate captions without losing numeric types or NULL",()=> {
    var table=new DataTable(); table.Columns.Add("a",typeof(decimal)).Caption="same"; table.Columns.Add("b",typeof(string)).Caption="same"; table.Rows.Add(10.13m,DBNull.Value);
    using var json=System.Text.Json.JsonDocument.Parse(ResultFormats.Json(table));
    Assert(json.RootElement.GetProperty("columns").GetArrayLength()==2 && json.RootElement.GetProperty("rows")[0][0].GetDecimal()==10.13m && json.RootElement.GetProperty("rows")[0][1].ValueKind==System.Text.Json.JsonValueKind.Null);
});
Check("SQL generation quotes identifiers apostrophes binary and NULL without execution",()=>{
    var table=new DataTable();table.Columns.Add("display]name",typeof(string));table.Columns.Add("binary",typeof(byte[]));table.Rows.Add("O'Brien",new byte[]{0,255});
    Assert(ResultFormats.Inserts(table,"dbo.example").Contains("INSERT INTO [dbo].[example] ([display]]name], [binary]) VALUES ('O''Brien', 0x00FF);"));
    Assert(ResultFormats.InList(table)=="('O''Brien')" && ResultFormats.Literal(DBNull.Value)=="NULL");
});
Check("HTML report escapes SQL and cell markup; Markdown preserves full binary",()=>{
    var table=new DataTable();table.Columns.Add("caption");table.Columns.Add("bin",typeof(byte[]));table.Rows.Add("<script>|",new byte[]{1,255});
    string html=ResultFormats.Html(table,"<sql>","fixture");
    Assert(!html.Contains("<script>") && html.Contains("&lt;script&gt;") && html.Contains("&lt;sql&gt;") && html.Contains("0x01FF"));
    Assert(ResultFormats.Markdown(table).Contains("\\|") && ResultFormats.Markdown(table).Contains("0x01FF"));
});
Check("Transpose preserves logical values and bounds column expansion",()=>{
    var table=new DataTable();table.Columns.Add("id",typeof(int));table.Columns.Add("v",typeof(string));table.Rows.Add(42,DBNull.Value);
    var transpose=ResultFormats.Transpose(table);Assert(transpose.Rows.Count==2 && (int)transpose.Rows[0][1]==42 && transpose.Rows[1][1] is DBNull);
    for(int i=0;i<1001;i++)table.Rows.Add(i,"v");
    try{ResultFormats.Transpose(table);throw new Exception("Unbounded transpose");}catch(ArgumentException){}
});
Check("Product counts pluralize labels with invariant grouping", () => {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
    Assert(Counts.Of(1, "row") == "1 row" && Counts.Of(0, "row") == "0 rows" && Counts.Of(100000, "row") == "100,000 rows" && Counts.Of(2, "result") == "2 results");
});
Check("Only scripts that may contain USE trigger the database probe", () => {
    foreach (string sql in new[] { "use master", "USE [sales db]", "select 1\ngo\n  use\ttempdb", "exec x\nuse afilmed" }) Assert(OdbcSession.MayChangeDatabase(sql));
    foreach (string sql in new[] { "select @@spid", "select user_name()", "exec sp_who", "select * from house", "select reuse from t", "use" }) Assert(!OdbcSession.MayChangeDatabase(sql));
});
Check("Multi-server run executes every target in parallel and isolates failures", () => {
    var outcomes = MultiRun.ExecuteAsync([new RunTarget("alpha", new DemoQueryEngine()), new RunTarget("beta", new DemoQueryEngine()), new RunTarget("broken", new ThrowingEngine())], "select 1 as n, 'x' as s, NULL as v").GetAwaiter().GetResult();
    Assert(outcomes.Count == 3 && outcomes[0].Target.Label == "alpha" && outcomes[0].Output.Error == null && outcomes[1].Output.Tables.Count == 1 && outcomes[2].Output.Error!.Contains("fixture failure"));
    var combined = MultiRun.Combine(outcomes).Single().Table!;
    Assert(combined.Rows.Count == 2 && combined.Columns[0].Caption == MultiRun.ServerCaption && (string)combined.Rows[1][0] == "beta" && (decimal)combined.Rows[0][1] == 1m && combined.Rows[0][3] is DBNull && combined.Columns[1].Caption == "n");
});
Check("Multi-server results with different shapes stay separate", () => {
    var a = engine.Execute("select 1 as n"); var b = engine.Execute("select 'one' as n"); var c = engine.Execute("select 1 as m");
    RunTarget T(string name) => new(name, engine);
    Assert(MultiRun.Combine([new(T("a"), a, 1), new(T("b"), b, 1)]).Single() is { Table: null, Reason: not null });
    Assert(MultiRun.Combine([new(T("a"), a, 1), new(T("c"), c, 1)]).Single().Table == null);
    Assert(MultiRun.Combine([new(T("a"), a, 1), new(T("b"), new QueryOutput(), 1)]).Single() is { Table: null });
});
Check("Multi-server run rejects empty, oversized and ambiguous target lists before executing", () => {
    foreach (var targets in new[] { new RunTarget[0], Enumerable.Range(0, 9).Select(i => new RunTarget("s" + i, engine)).ToArray(), new[] { new RunTarget("A", engine), new RunTarget("a", engine) } })
        try { MultiRun.ExecuteAsync(targets, "select 1").GetAwaiter().GetResult(); throw new Exception("Accepted invalid targets"); } catch (ArgumentException) { }
});
Check("Multi-server cancellation reaches every target", () => {
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var outcomes = MultiRun.ExecuteAsync([new RunTarget("a", engine), new RunTarget("b", engine)], "select 1", cancelled.Token).GetAwaiter().GetResult();
    Assert(outcomes.All(o => o.Output.Error != null && o.Output.Tables.Count == 0));
});
DataTable DiffTable(params object[][] rows)
{
    var t = new DataTable(); t.Columns.Add("column_0", typeof(int)).Caption = "id"; t.Columns.Add("column_1", typeof(string)).Caption = "name"; t.Columns.Add("column_2", typeof(byte[])).Caption = "hash";
    foreach (var row in rows) t.Rows.Add(row); return t;
}
Check("Result diff keeps only keys that are missing or different, with typed NULL and binary equality", () => {
    var alpha = DiffTable([1, "a", new byte[] { 1 }], [2, "b", new byte[] { 2 }], [3, DBNull.Value, DBNull.Value], [5, "x", DBNull.Value], [5, "y", DBNull.Value]);
    var beta = DiffTable([1, "a", new byte[] { 1 }], [2, "z", new byte[] { 2 }], [3, DBNull.Value, DBNull.Value], [4, "d", DBNull.Value], [5, "y", DBNull.Value], [5, "x", DBNull.Value]);
    var diff = ResultDiff.Compare([("alpha", alpha), ("beta", beta)], [0]);
    Assert(diff.Keys == 5 && diff.DifferingKeys == 2 && diff.KeyDescription == "id" && diff.Table.Rows.Count == 3);
    Assert(diff.Table.Columns[0].Caption == "server" && diff.Table.Columns[1].Caption == "difference" && diff.Table.Columns[2].Caption == "id" && diff.Table.Columns[2].DataType == typeof(int));
    Assert((string)diff.Table.Rows[0][0] == "alpha" && (string)diff.Table.Rows[0][1] == "values differ: name" && (string)diff.Table.Rows[1][0] == "beta" && (string)diff.Table.Rows[2][1] == "missing on alpha" && (int)diff.Table.Rows[2][2] == 4);
    var byName = ResultDiff.Compare([("alpha", alpha), ("beta", beta)], [0, 1]);
    Assert(byName.KeyDescription == "id, name" && byName.Table.AsEnumerable().All(r => ((string)r[1]).StartsWith("missing on")));
    Assert(ResultDiff.Compare([("alpha", alpha), ("copy", alpha.Copy())], [0]).Table.Rows.Count == 0);
});
Check("Result diff rejects mismatched shapes and missing keys before comparing", () => {
    var other = new DataTable(); other.Columns.Add("column_0", typeof(string)).Caption = "id";
    foreach (var (parts, keys) in new (IReadOnlyList<(string, DataTable)>, int[])[] { ([("a", DiffTable()), ("b", other)], [0]), ([("a", DiffTable()), ("b", DiffTable())], []), ([("a", DiffTable())], [0]), ([("a", DiffTable()), ("b", DiffTable())], [7]) })
        try { ResultDiff.Compare(parts, keys); throw new Exception("Accepted an invalid comparison"); } catch (ArgumentException) { }
});
Check("Server groups round trip profiles without passwords and reject ambiguous groups", () => {
    string path = Path.Combine(Path.GetTempPath(), "ase-groups-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        var qa = new ConnectionProfile("qa-host", 5005, "master", "fixture-user", false, "", ServerAlias: "QA");
        var groups = ServerGroupStore.Upsert([], new ServerGroup("QA pair", [qa, qa with { Server = "qa2-host", ServerAlias = "QA2" }]));
        ServerGroupStore.Save(path, groups);
        string json = File.ReadAllText(path);
        Assert(!json.Contains("\"Password\"", StringComparison.OrdinalIgnoreCase) && !json.Contains("PWD", StringComparison.Ordinal) && json.Contains("\"EncryptPassword\""));
        var loaded = ServerGroupStore.Load(path);
        Assert(loaded.Single().Name == "QA pair" && loaded[0].Profiles.Count == 2 && loaded[0].Profiles[1] == qa with { Server = "qa2-host", ServerAlias = "QA2" });
        Assert(ServerGroupStore.Upsert(loaded, new ServerGroup("qa PAIR", [qa])).Single().Profiles.Count == 1 && ServerGroupStore.Remove(loaded, "QA PAIR").Count == 0);
        foreach (var bad in new[] { new ServerGroup(" ", [qa]), new ServerGroup("empty", []), new ServerGroup("many", Enumerable.Repeat(qa, 9).ToList()), new ServerGroup("partial", [qa with { User = "" }]) })
            try { ServerGroupStore.Validate([bad]); throw new Exception("Accepted invalid group"); } catch (ArgumentException) { }
        try { ServerGroupStore.Validate([new ServerGroup("A", [qa]), new ServerGroup("a", [qa])]); throw new Exception("Accepted duplicate names"); } catch (ArgumentException) { }
        File.WriteAllText(path, "[{\"Name\":\"x\",\"Profiles\":[],\"Password\":\"no\"}]");
        try { ServerGroupStore.Load(path); throw new Exception("Accepted unknown field"); } catch (System.Text.Json.JsonException) { }
    }
    finally { foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(file); }
});
Check("Parameters are found in code and strings, never in comments, times or brackets", () => {
    string sql = "-- :ignored\nwaitfor delay ':interval' /* :also_ignored */\nselect '00:00:10', [a:b], x::y from t where id = :spid and name = :Spid and 'Note:x' = ':db_name'";
    var names = SqlParameters.Names(sql);
    Assert(names.SequenceEqual(new[] { "interval", "spid", "db_name" }));
    Assert(SqlParameters.Scan(sql).Count(p => p.Name.Equals("spid", StringComparison.OrdinalIgnoreCase)) == 2);
});
Check("Parameter values are escaped inside quotes and limited to one token outside them", () => {
    string sql = "use :database\ngo\nsp_spaceused ':table', 1\nselect \":label\" from t where x = :n";
    string filled = SqlParameters.Apply(sql, new Dictionary<string, string> { ["database"] = "[sales db]", ["TABLE"] = "o'neil", ["label"] = "say \"hi\"", ["n"] = "-42" });
    Assert(filled == "use [sales db]\ngo\nsp_spaceused 'o''neil', 1\nselect \"say \"\"hi\"\"\" from t where x = -42");
    foreach (string bad in new[] { "1; drop table t", "x--", "a b", " a", "[open", "a/*", "" })
        Assert(SqlParameters.Problem("kill :spid", "spid", bad) != null);
    foreach (string good in new[] { "42", "dbo.orders", "sales..orders", "[my table]", "#tmp", "[a]]b].dbo.t" })
        Assert(SqlParameters.Problem("select * from :t", "t", good) == null);
    Assert(SqlParameters.Problem("select ':t'", "t", "line\nbreak") != null && SqlParameters.Problem("select ':t'", "t", "a b; c") == null);
    try { SqlParameters.Apply("kill :spid", new Dictionary<string, string>()); Assert(false); } catch (ArgumentException) { }
});
Check("Packaged recipes are generic: no site procedures or logins, and every placeholder is English", () => {
    using var recipes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AseIsql.Desktop", "Assets", "ase-recipes.json")));
    var entries = recipes.RootElement.EnumerateArray().ToList();
    Assert(entries.All(r => r.GetProperty("engine").GetString() is "ASE" or "IQ" or "RS") && entries.Count(r => r.GetProperty("engine").GetString() == "ASE") == 52 && entries.Count(r => r.GetProperty("engine").GetString() == "IQ") == 21 && entries.Count(r => r.GetProperty("engine").GetString() == "RS") == 21);
    // Replication Server speaks RCL: its recipes never use SELECT; IQ recipes never use ASE-only catalogs.
    Assert(entries.Where(r => r.GetProperty("engine").GetString() == "RS").All(r => !r.GetProperty("sql").GetString()!.Contains("select", StringComparison.OrdinalIgnoreCase)));
    Assert(entries.Where(r => r.GetProperty("engine").GetString() == "IQ").All(r => !r.GetProperty("sql").GetString()!.Contains("master..", StringComparison.OrdinalIgnoreCase)));
    var sql = entries.Select(r => r.GetProperty("sql").GetString()!).ToList();
    Assert(sql.Count == 94 && sql.All(s => !s.Contains("sp_reporte_tablas", StringComparison.OrdinalIgnoreCase) && !s.Contains("create proc", StringComparison.OrdinalIgnoreCase) && !s.Contains("'monitor'", StringComparison.Ordinal)));
    var allowed = new HashSet<string>(["interval", "min_spid", "idle_seconds", "spid", "table", "database", "open_minutes", "date_column", "history_table", "min_change_pct", "pattern", "login", "object", "since", "orig_db", "xactname", "databases", "data_server", "connection_id"]);
    Assert(sql.SelectMany(SqlParameters.Names).All(allowed.Contains));
});
Check("SAP IQ profiles use the IQ driver with Host, optional server and database names, and no ASE-only keys", () => {
    var profile = new ConnectionProfile("iq-host", 2638, "", "dba", false, "", Platform: ServerPlatform.Iq);
    var builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile, "secret"));
    Assert((string)builder["Driver"] == "{SAP IQ}" && (string)builder["Host"] == "iq-host:2638" && !builder.ContainsKey("ServerName") && !builder.ContainsKey("DatabaseName") && !builder.ContainsKey("Port") && !builder.ContainsKey("Database") && !builder.ContainsKey("Language") && !builder.ContainsKey("CharSet"));
    builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile with { IqServerName = "iq_prod", Database = "iqdb", Charset = "utf8" }, "secret"));
    Assert((string)builder["ServerName"] == "iq_prod" && (string)builder["DatabaseName"] == "iqdb" && (string)builder["CharSet"] == "utf8" && ((string)builder["AppInfo"]).Contains("DBMON SQL"));
    Assert(OdbcSession.DriverFor(ServerPlatform.Iq) == "SAP IQ" && OdbcSession.TestSql(ServerPlatform.Iq).StartsWith("SELECT", StringComparison.Ordinal));
});
Check("Replication Server profiles use the ASE driver without a database and test with RCL", () => {
    var profile = new ConnectionProfile("rs-host", 5510, "", "sa", false, "", Platform: ServerPlatform.Rs);
    var builder = new OdbcConnectionStringBuilder(OdbcSession.BuildConnectionString(profile, "secret"));
    Assert((string)builder["Driver"] == "{Adaptive Server Enterprise}" && (string)builder["Server"] == "rs-host" && !builder.ContainsKey("Database") && (string)builder["ApplicationName"] == "DBMON SQL");
    Assert(OdbcSession.TestSql(ServerPlatform.Rs) == "admin version");
});
Check("Each platform validates its own database rule and IQ server name", () => {
    foreach (var bad in new[] {
        new ConnectionProfile("h", 5000, "", "u", false, ""),
        new ConnectionProfile("h", 5000, "master", "u", false, "", Platform: ServerPlatform.Rs),
        new ConnectionProfile("h", 5000, "master", "u", false, "", IqServerName: "iq"),
        new ConnectionProfile("h", 5000, "master", "u", false, "", Platform: (ServerPlatform)7) })
    { try { bad.Validate(); Assert(false); } catch (ArgumentException) { } }
    new ConnectionProfile("h", 2638, "", "u", false, "", Platform: ServerPlatform.Iq).Validate();
    new ConnectionProfile("h", 5510, "", "u", false, "", Platform: ServerPlatform.Rs).Validate();
    string json = System.Text.Json.JsonSerializer.Serialize(new ConnectionProfile("h", 2638, "", "u", false, "", Platform: ServerPlatform.Iq, IqServerName: "iq1"));
    Assert(json.Contains("\"Platform\":\"Iq\"") && System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>(json)!.Platform == ServerPlatform.Iq);
    Assert(System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>("{\"Server\":\"h\",\"Port\":1,\"Database\":\"d\",\"User\":\"u\",\"UseTls\":false,\"TrustedFile\":\"\"}")!.Platform == ServerPlatform.Ase);
    Assert(Platforms.TryParseTag("rs", out var rs) && rs == ServerPlatform.Rs && !Platforms.TryParseTag("oracle", out _));
});
Check("sql.ini keeps master entries apart from query endpoints", () => {
    var server = SqlIniParser.Parse("[rep]\nmaster=TCP,rs-host,5510\n[iq]\nmaster=TCP,iq-host,2638\nquery=TCP,iq-host,2638").Servers;
    Assert(server[0].Queries.Count == 0 && server[0].MasterEntries!.Single() is { Host: "rs-host", Port: 5510 } && server[1].Queries.Single().Port == 2638);
});
Check("Repeat caution detects state changes in code only, ignoring temporary tables, strings and comments", () => {
    Assert(SqlRisk.StateChanges("select * from t where name = 'drop table x' -- delete\n/* update */").Count == 0);
    Assert(SqlRisk.StateChanges("select * into #T1 from t\ncreate table #x (a int)\ninsert into #x values (1)\ninsert #x values (2)\ndrop table #x\nexec sp_configure").Count == 0);
    Assert(SqlRisk.StateChanges("update t set x = 1").SequenceEqual(new[] { "update" }));
    Assert(SqlRisk.StateChanges("select a into archive_t from t").SequenceEqual(new[] { "select into" }));
    Assert(SqlRisk.StateChanges("exec sp_configure 'max memory', 100").SequenceEqual(new[] { "sp_configure" }));
    Assert(SqlRisk.StateChanges("kill 42\ngo\nresume connection to ds.db\nDELETE from t").SequenceEqual(new[] { "kill", "resume", "delete" }));
    using var recipes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AseIsql.Desktop", "Assets", "ase-recipes.json")));
    var byId = recipes.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("sql").GetString()!);
    Assert(SqlRisk.StateChanges(byId["ase_tablas_mas_accedidas"]).Count == 0 && SqlRisk.StateChanges(byId["ase_top10_cpu"]).Count == 0 && SqlRisk.StateChanges(byId["rs_who_sqm"]).Count == 0);
    Assert(SqlRisk.StateChanges(byId["ase_act_kill_spid"]).Count > 0 && SqlRisk.StateChanges(byId["rs_resume_skip"]).Count > 0 && SqlRisk.StateChanges(byId["iq_drop_connection"]).Count > 0);
});
Check("Session options send only what changed, with noexec last on and first off", () => {
    var off = AseSessionOptions.Default;
    Assert(AseSessionOptions.Script(off, off).Count == 0);
    Assert(AseSessionOptions.Script(off, off with { StatisticsIo = true }).SequenceEqual(new[] { "set statistics io on" }));
    // Turning on: noexec trails, so showplan and the statistics are already in force when nothing runs any more.
    var all = off with { NoExec = true, ShowPlan = true, StatisticsIo = true, StatisticsTime = true };
    Assert(AseSessionOptions.Script(off, all).SequenceEqual(new[] { "set showplan on", "set statistics io on", "set statistics time on", "set noexec on" }));
    // Turning off: noexec leads, because nothing after it would execute otherwise.
    Assert(AseSessionOptions.Script(all, off).SequenceEqual(new[] { "set noexec off", "set showplan off", "set statistics io off", "set statistics time off" }));
    Assert(AseSessionOptions.Script(all, all with { StatisticsTime = false }).SequenceEqual(new[] { "set statistics time off" }));
});
Check("Compatibility mode is set only when chosen and never claimed back to the server default", () => {
    var off = AseSessionOptions.Default;
    Assert(off.Compatibility == CompatibilityMode.ServerDefault);
    Assert(AseSessionOptions.Script(off, off with { Compatibility = CompatibilityMode.Off }).SequenceEqual(new[] { "set compatibility_mode off" }));
    Assert(AseSessionOptions.Script(off, off with { Compatibility = CompatibilityMode.On }).SequenceEqual(new[] { "set compatibility_mode on" }));
    // ASE has no statement that restores the server's own setting, so the move back emits nothing.
    Assert(AseSessionOptions.Script(off with { Compatibility = CompatibilityMode.On }, off).Count == 0);
});
Check("Session option tags name every option in force and nothing else", () => {
    Assert(AseSessionOptions.Default.Tags.Count == 0 && AseSessionOptions.Default.IsDefault);
    var options = AseSessionOptions.Default with { NoExec = true, StatisticsIo = true, Compatibility = CompatibilityMode.Off };
    Assert(options.Tags.SequenceEqual(new[] { "NOEXEC", "STATS IO", "COMPAT OFF" }) && !options.IsDefault);
    // Compatibility alone is not the default state either: the chip has to show it.
    Assert(!(AseSessionOptions.Default with { Compatibility = CompatibilityMode.On }).IsDefault);
});
Console.WriteLine($"{passed} checks passed. No ASE server was contacted.");

sealed class ThrowingEngine : IQueryEngine
{
    public string Name => "Throwing fixture";
    public QueryOutput Execute(string sql, CancellationToken cancellation = default) => throw new InvalidOperationException("fixture failure");
}
