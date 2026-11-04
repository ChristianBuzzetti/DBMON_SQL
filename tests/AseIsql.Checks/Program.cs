using AseIsql;
using System.Data;
using System.Globalization;
using AseIsql.Odbc;
using System.Data.Odbc;

int passed = 0;
void Check(string name, Action body) { body(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
var engine = new DemoQueryEngine();
HealthObjectChecks.Run(Check, Assert);
Check("Query clipboard report preserves submitted SQL and escapes it before the table", () => {
    const string query = "select '<script>José & 🐱</script>'\r\nfrom t;";
    string report = ResultHtmlClipboard.QueryAndTable(query, ["n"], [new object?[] { 1 }]);
    Assert(report.IndexOf("Query", StringComparison.Ordinal) < report.IndexOf("<table", StringComparison.Ordinal));
    Assert(report.Contains("&lt;script&gt;José &amp; 🐱&lt;/script&gt;") && !report.Contains("<script>"));
    Assert(report.Contains("<br>from t;") && report.Contains("font-family:") && report.Contains("Results"));
    string payload = ResultHtmlClipboard.Payload(report);
    int Offset(string key) => int.Parse(payload.Split("\r\n").Single(line => line.StartsWith(key + ":"))[(key.Length + 1)..], CultureInfo.InvariantCulture);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);
    Assert(System.Text.Encoding.UTF8.GetString(bytes, Offset("StartFragment"), Offset("EndFragment") - Offset("StartFragment")) == report);
    Assert(Offset("EndHTML") == bytes.Length);
});
Check("Query clipboard text preserves exact SQL, separates output and bounds the entire report", () => {
    const string query = "select 1;\r\n-- exact SQL\n";
    Assert(ResultHtmlClipboard.QueryAndText(query, "n\r\n1\r\n") == "Query:\r\n" + query + "\r\n\r\nResults:\r\nn\r\n1\r\n");
    try { ResultHtmlClipboard.QueryAndText(query, new string('x', 100), maxCharacters: 110); Assert(false); } catch (ArgumentException) { }
    try { ResultHtmlClipboard.QueryAndTable(new string('&', 500), ["n"], [], maxCharacters: 2200); Assert(false); } catch (ArgumentException) { }
    foreach (string missing in new[] { "", " \r\n" }) {
        try { ResultHtmlClipboard.QueryAndText(missing, "output"); Assert(false); } catch (ArgumentException) { }
        try { ResultHtmlClipboard.QueryAndTable(missing, ["n"], []); Assert(false); } catch (ArgumentException) { }
    }
});
Check("HTML clipboard table encodes values, preserves types and bounds escaped output", () => {
    string html = ResultHtmlClipboard.Table(["<name>", "Value"], [
        new object?[] { "José 🐱<&\"\r\nnext", DBNull.Value },
        new object?[] { new byte[] { 0, 255 }, 12.5m },
        new object?[] { "", new DateTime(2026, 10, 26) }]);
    Assert(html.Contains("&lt;name&gt;") && !html.Contains("<name>"));
    Assert(html.Contains("José 🐱&lt;&amp;&quot;<br>next") && html.Contains(">NULL</span>"));
    Assert(html.Contains("0x00FF") && html.Contains("12.5") && html.Contains("2026-10-26T00:00:00.0000000"));
    Assert(html.Contains("style=") && html.Contains("<thead>") && html.Contains("<tbody>"));
    try { ResultHtmlClipboard.Table(["x"], [new object?[] { new string('&', 1000) }], maxCharacters: 2000); Assert(false); } catch (ArgumentException) { }
    try { ResultHtmlClipboard.Table(["x"], [new object?[] { 1, 2 }]); Assert(false); } catch (ArgumentException) { }
    Assert(ResultHtmlClipboard.Table(["x"], []).Contains("<tbody></tbody>"));
});
Check("HTML clipboard offsets count UTF-8 bytes around the complete table fragment", () => {
    string table = ResultHtmlClipboard.Table(["País"], [new object?[] { "日本 🐱" }]);
    string payload = ResultHtmlClipboard.Payload(table);
    int Offset(string name) => int.Parse(payload.Split("\r\n").Single(line => line.StartsWith(name + ":"))[(name.Length + 1)..], CultureInfo.InvariantCulture);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(payload);
    string Slice(int start, int end) => System.Text.Encoding.UTF8.GetString(bytes, start, end - start);
    Assert(Slice(Offset("StartFragment"), Offset("EndFragment")) == table);
    Assert(Slice(Offset("StartHTML"), Offset("EndHTML")).StartsWith("<!DOCTYPE html>"));
    Assert(Offset("EndHTML") == bytes.Length);
});
Check("Query history survives reload with exact SQL and newest dates first", () => {
    string directory = Path.Combine(Path.GetTempPath(), "dbmon-history-" + Guid.NewGuid().ToString("N"));
    string path = Path.Combine(directory, "history.json");
    var store = new QueryHistoryStore(path);
    Assert(store.Load().Count == 0);
    var older = new QueryHistoryEntry(DateTimeOffset.Parse("2026-10-19T20:00:00Z"), "select 'mon_db'\n-- exact SQL");
    var newer = new QueryHistoryEntry(DateTimeOffset.Parse("2026-10-20T10:00:00-03:00"), "select 2");
    store.Save([older, newer]);
    var loaded = new QueryHistoryStore(path).Load();
    Assert(loaded.SequenceEqual(new[] { newer, older }));
    store.Save(loaded.Prepend(new QueryHistoryEntry(newer.ExecutedAt.AddMinutes(1), "select 3")));
    Assert(new QueryHistoryStore(path).Load().Count == 3 && Directory.GetFiles(directory, "*.bak").Length == 1);
    Console.WriteLine("History fixture: " + directory);
});
Check("Query history bounds retention and preserves malformed files", () => {
    string directory = Path.Combine(Path.GetTempPath(), "dbmon-history-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, "history.json");
    var store = new QueryHistoryStore(path);
    var now = DateTimeOffset.UtcNow;
    store.Save(Enumerable.Range(0, 105).Select(i => new QueryHistoryEntry(now.AddMinutes(i), "select " + i)));
    Assert(store.Load().Count == 100 && store.Load()[0].Sql == "select 104" && store.Load()[99].Sql == "select 5");
    string valid = File.ReadAllText(path);
    try { store.Save([new QueryHistoryEntry(now, " ")]); Assert(false); } catch (System.Text.Json.JsonException) { }
    Assert(File.ReadAllText(path) == valid);
    try { new QueryHistoryStore(directory).Save([new QueryHistoryEntry(now, "select 1")]); Assert(false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    Assert(File.ReadAllText(path) == valid);
    foreach (string invalid in new[] { "{broken", "null", "[null]", "[{\"ExecutedAt\":\"2026-10-20T00:00:00Z\",\"Sql\":null}]", "[{\"Sql\":\"select 1\"}]" }) {
        File.WriteAllText(path, invalid);
        try { store.Load(); Assert(false); } catch (System.Text.Json.JsonException) { }
        Assert(File.ReadAllText(path) == invalid);
    }
    using (var oversized = File.Create(path)) oversized.SetLength(32L * 1024 * 1024 + 1);
    try { store.Load(); Assert(false); } catch (IOException) { }
    Assert(new FileInfo(path).Length == 32L * 1024 * 1024 + 1);
    try { new QueryHistoryStore(directory).Load(); Assert(false); } catch (UnauthorizedAccessException) { } catch (IOException) { }
    Console.WriteLine("History fixture: " + directory);
});
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
Check("Production reader keeps server numerics wider than System.Decimal as text", () => {
    const string wide = "261669575803947255606802382848";
    using var reader = new WideNumericTestReader("identburnmax", 38, [wide]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(string) && table.Columns[0].Caption == "identburnmax" && (string)table.Rows[0][0] == wide);
    Assert(reader.ValueCalls == 0 && (string?)table.Columns[0].ExtendedProperties["ReadAsText"] == "Decimal");
});
Check("Wide numerics fall back to the driver string when chars access is refused", () => {
    using var reader = new WideNumericTestReader("identburnmax", 38, ["123456789012345678901234567890"], chars: false); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    Assert((string)output.Tables[0].Rows[0][0] == "123456789012345678901234567890");
});
Check("A wide value the driver never described is still recovered as text", () => {
    const string wide = "99999999999999999999999999999999";
    using var reader = new WideNumericTestReader("saldo", 0, [wide]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(string) && (string)table.Rows[0][0] == wide && table.Columns[0].Caption == "saldo");
});
Check("Promoting an undescribed column to text keeps the rows already read, with NULL", () => {
    using var reader = new WideNumericTestReader("saldo", 0, ["10.13", null, "99999999999999999999999999999999", "2"]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(string) && table.Columns[0].Caption == "saldo" && table.Columns.Count == 1);
    Assert((string)table.Rows[0][0] == "10.13" && table.Rows[1][0] is DBNull && (string)table.Rows[2][0] == "99999999999999999999999999999999" && (string)table.Rows[3][0] == "2");
});
Check("A value no conversion can deliver names its column and row", () => {
    using var reader = new WideNumericTestReader("saldo", 0, ["99999999999999999999999999999999"], chars: false, strings: false); var output = new QueryOutput();
    try { ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None); throw new Exception("Overflow ignored"); }
    catch (ResultValueException ex) { Assert(ex.Message.Contains("'saldo'") && ex.Message.Contains("row 1") && ex.Message.Contains("Decimal")); }
    Assert(output.Tables[0].Rows.Count == 0);
});
Check("A numeric the driver would round is kept whole as text", () => {
    const string scaled = "1234567890123456789012345678.0123456789";
    using var reader = new WideNumericTestReader("wide_scale", 38, [scaled]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(string) && (string)table.Rows[0][0] == scaled);
});
Check("Numerics that fit stay typed, with their own scale", () => {
    using var reader = new WideNumericTestReader("amount", 18, ["10.13", "-0.500", "0", null, "123456789012.345678"]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(decimal) && (decimal)table.Rows[0][0] == 10.13m && (decimal)table.Rows[1][0] == -0.5m);
    Assert((decimal)table.Rows[2][0] == 0m && table.Rows[3][0] is DBNull && (decimal)table.Rows[4][0] == 123456789012.345678m);
});
Check("A time keeps the fraction of a second the driver drops", () => {
    using var reader = new WideNumericTestReader("bigtime_value", 0, ["16:30:45.123456", "00:00:00"], fieldType: typeof(TimeSpan)); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    var table = output.Tables[0];
    Assert(table.Columns[0].DataType == typeof(TimeSpan) && (TimeSpan)table.Rows[0][0] == TimeSpan.Parse("16:30:45.123456", CultureInfo.InvariantCulture));
    Assert((TimeSpan)table.Rows[1][0] == TimeSpan.Zero && ResultFormats.Cell(table.Rows[0][0]) == "16:30:45.1234560");
});
Check("Bit columns read as the server writes them", () => Assert(ResultFormats.Cell(true) == "1" && ResultFormats.Cell(false) == "0"));
Check("Every result set copies under its own label, bounded", () => {
    var sets = new[] { ("Result 1 · 1 row", "a\tb\n1\t2\n"), ("Result 2 · 1 row", "c\n3") };
    var copy = ResultFormats.Sets(sets, 4096);
    Assert(copy.Sets == 2 && copy.Complete);
    Assert(copy.Text.Contains("-- Result 1 · 1 row") && copy.Text.Contains("-- Result 2 · 1 row") && copy.Text.Contains("1\t2") && copy.Text.Contains("3"));
    var bounded = ResultFormats.Sets(sets, 30);
    Assert(bounded.Sets == 1 && !bounded.Complete && !bounded.Text.Contains("Result 2"));
});
Check("A verified column is never converted before its text is read", () => {
    const string scaled = "1234567890123456789012345678.0123456789";
    using var reader = new WideNumericTestReader("wide_scale", 0, [scaled]); var output = new QueryOutput();
    ResultReader.Read(reader, output, new ResultBudget(10), CancellationToken.None);
    Assert((string)output.Tables[0].Rows[0][0] == scaled && reader.NullCalls == 0 && reader.ValueCalls == 0);
});
Check("Bit columns copy as the server writes them", () => {
    var table = new DataTable(); table.Columns.Add("flag", typeof(bool)); table.Rows.Add(true); table.Rows.Add(false);
    string text = DelimitedText.Serialize(["flag"], table.Rows.Cast<DataRow>().Select(r => (IReadOnlyList<object?>)r.ItemArray), includeHeaders: true);
    Assert(text.Contains("1") && text.Contains("0") && !text.Contains("True") && !text.Contains("False"));
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
Check("A frame on its own line leaves the payload visible on the first line of the record",()=>{
    const string prefix="[SAP][ASE ODBC Driver][Adaptive Server Enterprise]";
    // A record can arrive with the frame occupying a line of its own; Messages shows one line per record, so a
    // leading blank would make it read as empty while its text sat out of sight below.
    const string stats="Table: Worktable1 scan count 1, logical reads: (regular=881 apf=0 total=881)";
    var message=new DiagnosticMessages().Read(3615,"ZZZZZ",prefix+"\n"+stats,"");
    Assert(message.Text==stats && message.RawText==prefix+"\n"+stats);
    // Trailing text stays exactly as the server wrote it, newlines included.
    Assert(new DiagnosticMessages().Read(1,"x",prefix+" Server warning.\r\n","").Text=="Server warning.\r\n");
    // A record that is nothing but the frame carries no server text: it comes back empty, which is how the
    // session and the failure summary know to drop it instead of showing a blank row. Raw evidence is kept.
    var bare=new DiagnosticMessages().Read(0,"ZZZZZ","\n"+prefix,"");
    Assert(bare.Text.Length==0 && bare.RawText=="\n"+prefix);
    Assert(new DiagnosticMessages().Read(0,"ZZZZZ",prefix,"").Text.Length==0);
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
Check("Result cells keep every digit a date and time carries", () => {
    Assert(ResultFormats.Cell(new DateTime(2008, 9, 15, 13, 46, 36, 596)) == "2008-09-15 13:46:36.596");
    Assert(ResultFormats.Cell(new DateTime(2008, 9, 15, 13, 46, 30)) == "2008-09-15 13:46:30");
    Assert(ResultFormats.Cell(new DateTime(2026, 2, 3, 0, 0, 0)) == "2026-02-03");
    Assert(ResultFormats.Cell(new DateTime(2026, 2, 3, 16, 30, 45, 123).AddTicks(4567)) == "2026-02-03 16:30:45.1234567");
    Assert(ResultFormats.Cell(new TimeSpan(0, 13, 46, 36, 596)) == "13:46:36.5960000" || ResultFormats.Cell(new TimeSpan(0, 13, 46, 36, 596)) == "13:46:36.596");
    Assert(ResultFormats.Cell(DBNull.Value) == "" && ResultFormats.Cell(null) == "" && ResultFormats.Cell(10.13m) == "10.13");
});
Check("SQL literals keep a whole second that ends in zero", () => {
    Assert(ResultFormats.Literal(new DateTime(2008, 9, 15, 13, 46, 30)) == "'2008-09-15T13:46:30'");
    Assert(ResultFormats.Literal(new DateTime(2008, 9, 15, 13, 46, 36, 596)) == "'2008-09-15T13:46:36.596'");
    Assert(ResultFormats.Literal(new DateTime(2026, 2, 3, 10, 20, 0)) == "'2026-02-03T10:20:00'");
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
Check("IQ TLS profiles supply both files while the identity password remains transient", () => {
    string ca = Path.GetTempFileName(), identity = Path.GetTempFileName();
    try {
        var profile = new ConnectionProfile("iq-host", 2638, "", "dba", true, ca, Platform: ServerPlatform.Iq, IqIdentityFile: identity);
        string json = System.Text.Json.JsonSerializer.Serialize(profile);
        Assert(System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>(json)!.IqIdentityFile == identity && !json.Contains("key-secret"));
        string wire = OdbcSession.BuildConnectionString(profile, "db-secret", "key-secret");
        Assert((string)new OdbcConnectionStringBuilder(wire)["PWD"] == "db-secret" && wire.EndsWith($";Encryption=TLS(TLS_TYPE=RSA;TRUSTED_CERTIFICATES={ca};IDENTITY={identity};IDENTITY_PASSWORD=key-secret)", StringComparison.Ordinal));
        string caOnly = OdbcSession.BuildConnectionString(profile with { IqIdentityFile = "" }, "db-secret");
        Assert(caOnly.EndsWith($";Encryption=TLS(TLS_TYPE=RSA;TRUSTED_CERTIFICATES={ca})", StringComparison.Ordinal));
        foreach (var bad in new[] { profile with { UseTls = false }, profile with { Platform = ServerPlatform.Ase, Database = "master" }, profile with { IqIdentityFile = identity + ".missing" } })
            try { bad.Validate(); throw new Exception("Accepted invalid identity profile"); } catch (ArgumentException) { }
        foreach (string secret in new[] { "", "a;SKIP_CERTIFICATE_NAME_CHECK=ON", "a)b", "a(b", "{secret}", "a}b", "a\0b", "a\nb", "a\"b", "a'b", " leading", "trailing " })
            try { OdbcSession.BuildConnectionString(profile, "db-secret", secret); throw new Exception("Accepted unsafe identity password"); } catch (ArgumentException) { }
        foreach (string suffix in new[] { ";SKIP_CERTIFICATE_NAME_CHECK=ON", "(identity)", "{identity}", "'identity'" })
        {
            string unsafePath = identity + suffix;
            try {
                File.WriteAllText(unsafePath, "fixture");
                foreach (var badPath in new[] { profile with { IqIdentityFile = unsafePath }, profile with { TrustedFile = unsafePath } })
                    try { OdbcSession.BuildConnectionString(badPath, "db-secret", "key-secret"); throw new Exception("Accepted ambiguous TLS file path"); } catch (ArgumentException) { }
            } finally { File.Delete(unsafePath); }
        }
        try { OdbcSession.BuildConnectionString(profile with { IqIdentityFile = "" }, "db-secret", "key-secret"); throw new Exception("Ignored identity password"); } catch (ArgumentException) { }
    } finally { File.Delete(ca); File.Delete(identity); }
});
Check("IQ identity secrets are redacted from displayed and original diagnostics", () => {
    const string raw = "db-secret key-secret key}}secret db-secret-long";
    var message = new DiagnosticMessages().Read(1, "08001", raw, "db-secret", "key-secret");
    Assert(message.Text == "[password] [password] key}}secret [password]-long" && message.RawText == message.Text);
    Assert(OdbcSession.Redact("key}}secret key}secret", "", "key}secret") == "[password] [password]");
    Assert(OdbcSession.Redact("db-secret-long db-secret", "db-secret", "db-secret-long") == "[password] [password]");
    Assert(OdbcSession.DescribeConnectionFailure(new ArgumentException(raw), "db-secret", "key-secret") == message.Text);
});
Check("IQ TLS can verify an explicit certificate name independently of the engine name", () => {
    string ca = Path.GetTempFileName();
    try {
        var profile = new ConnectionProfile("127.0.0.1", 4100, "", "synthetic-user", true, ca, Platform: ServerPlatform.Iq, IqServerName: "dw_qa", IqCertificateName: "IQDBQA");
        string wire = OdbcSession.BuildConnectionString(profile, "synthetic-db");
        Assert(wire.Contains("servername=dw_qa", StringComparison.OrdinalIgnoreCase));
        Assert(wire.EndsWith($";Encryption=TLS(TLS_TYPE=RSA;TRUSTED_CERTIFICATES={ca};CERTIFICATE_NAME=IQDBQA)", StringComparison.Ordinal));
        Assert(!wire.Contains("SKIP_CERTIFICATE_NAME_CHECK", StringComparison.OrdinalIgnoreCase));
        string json = System.Text.Json.JsonSerializer.Serialize(profile);
        Assert(System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>(json)!.IqCertificateName == "IQDBQA");
        foreach (string name in new[] { "a;SKIP_CERTIFICATE_NAME_CHECK=ON", "a)b", "{name}", "a\"b", "a\nb", " name" })
            try { OdbcSession.BuildConnectionString(profile with { IqCertificateName = name }, "synthetic-db"); throw new Exception("Accepted ambiguous certificate name"); } catch (ArgumentException) { }
        foreach (var bad in new[] { profile with { UseTls = false }, profile with { Platform = ServerPlatform.Ase, IqServerName = "", Database = "master" } })
            try { bad.Validate(); throw new Exception("Accepted certificate name outside IQ TLS"); } catch (ArgumentException) { }
        Assert(!OdbcSession.BuildConnectionString(profile with { IqCertificateName = "" }, "synthetic-db").Contains("CERTIFICATE_NAME", StringComparison.OrdinalIgnoreCase));
    } finally { File.Delete(ca); }
});
// ---- 0.4.0: B1 (no re-execution for Statistics IO), go N, continue after errors, timeouts, B2, B3, PROD and SqlRisk ----
Check("Statistics IO with an empty plan buffer reports 'plan not captured' and runs each batch exactly once", () => {
    foreach (string sql in new[] { "exec sp_who", "dbcc checkdb(master)", "checkpoint", "select 1", "exec p1\ngo\ncheckpoint\ngo\nexec sp_x" })
    {
        var channel = new CountingChannel { CapturesPlans = true };
        var output = new QueryOutput();
        var batches = SqlScript.SplitBatches(sql);
        var summary = ScriptRunner.Run(batches, channel, output, continueOnError: false, default);
        Assert(output.Error == null && summary.Executions == batches.Count && channel.Total == batches.Count);
        Assert(batches.All(b => channel.Executions[b.Text] == 1));
        // Only the plan buffer is read back, (0) then (-1), once per batch; the batch text never reaches the plan path.
        Assert(channel.PlanReads.SequenceEqual(Enumerable.Range(0, batches.Count).SelectMany(_ => new[] { 0, -1 })) && channel.Arms == batches.Count);
        Assert(output.Messages.Count(m => m.Text == ScriptRunner.PlanNotCaptured) == batches.Count && output.Tables.Count == 0);
    }
    var withPlan = new CountingChannel { CapturesPlans = true, PlanXml = "<?xml version=\"1.0\"?><query><statementNum>1</statementNum><lineNum>1</lineNum><text>select * from t</text><opTree><TableScan><act><rowCnt>1</rowCnt><lio>2</lio><pio>0</pio></act><objName>t</objName></TableScan></opTree></query>" };
    var planned = new QueryOutput();
    ScriptRunner.Run(SqlScript.SplitBatches("select * from t"), withPlan, planned, false, default);
    Assert(withPlan.Total == 1 && withPlan.PlanReads.SequenceEqual(new[] { 0 }) && planned.Tables.Single().TableName == ExecutionIo.ResultHeader);
    Assert(planned.Messages.Any(m => m.Number == 3615 && m.Text.StartsWith("Table: t logical reads: 2", StringComparison.Ordinal)) && planned.Messages.All(m => m.Text != ScriptRunner.PlanNotCaptured));
    var off = new CountingChannel(); ScriptRunner.Run(SqlScript.SplitBatches("select 1"), off, new QueryOutput(), false, default);
    Assert(off.Total == 1 && off.PlanReads.Count == 0 && off.Arms == 0);
});
Check("go N runs a batch N times like isql; counts are validated and GO inside literals is not a boundary", () => {
    var batches = SqlScript.SplitBatches("select 1\ngo 3\nselect 2\nGO\nselect 3\n  go   2  -- twice\nselect 4");
    Assert(batches.Select(b => (b.Text.Trim(), b.Repeat)).SequenceEqual(new[] { ("select 1", 3), ("select 2", 1), ("select 3", 2), ("select 4", 1) }) && SqlScript.Executions(batches) == 7);
    Assert(SqlScript.Split("select 1\ngo 3\nselect 2").SequenceEqual(new[] { "select 1\n", "select 2" }));
    var channel = new CountingChannel(); var output = new QueryOutput();
    ScriptRunner.Run(batches, channel, output, false, default);
    Assert(channel.Executions["select 1\n"] == 3 && channel.Executions["select 3\n"] == 2 && channel.Total == 7 && output.Messages.Any(m => m.Text == "Summary: 7 batches ok / 0 with error."));
    foreach (string bad in new[] { "select 1\ngo 0", "select 1\ngo 1001", "select 1\nGO 99999999999" })
        try { SqlScript.SplitBatches(bad); throw new Exception("Accepted an invalid go count: " + bad); } catch (FormatException ex) { Assert(ex.Message.Contains("1 to 1000")); }
    Assert(SqlScript.SplitBatches("select 'a\ngo 5\nb'").Single().Repeat == 1 && SqlScript.SplitBatches("/*\ngo 5\n*/ select 1").Single().Repeat == 1);
    Assert(SqlScript.SplitBatches("select 1\ngo abc").Single().Text.Contains("go abc"));
    Assert(engine.Execute("select 1 as n\ngo 3").Tables.Count == 3 && engine.Execute("select 1; select 2\ngo 2").Tables.Count == 4);
    // A plain script keeps its usual messages: no summary without go N or continue after errors.
    var plain = new QueryOutput(); ScriptRunner.Run(SqlScript.SplitBatches("select 1\ngo\nselect 2"), new CountingChannel(), plain, false, default);
    Assert(plain.Messages.All(m => !m.Text.StartsWith("Summary", StringComparison.Ordinal)));
});
Check("Continue after errors records each failure, runs the remaining batches and summarizes; off stops at the first", () => {
    const string sql = "select 1\ngo\nbad\ngo\nselect 3";
    var stop = new CountingChannel { Fail = b => b.StartsWith("bad") ? BatchOutcome.Failed : BatchOutcome.Ok }; var stopped = new QueryOutput();
    var first = ScriptRunner.Run(SqlScript.SplitBatches(sql), stop, stopped, continueOnError: false, default);
    Assert(first is { Executions: 2, Succeeded: 1, Failed: 1, Stopped: true } && !stop.Executions.ContainsKey("select 3") && stopped.Error == "fixture failure: bad\n");
    var go = new CountingChannel { Fail = stop.Fail }; var kept = new QueryOutput();
    var all = ScriptRunner.Run(SqlScript.SplitBatches(sql), go, kept, continueOnError: true, default);
    Assert(all is { Executions: 3, Succeeded: 2, Failed: 1, Stopped: false } && go.Executions["select 3"] == 1);
    Assert(kept.Messages.Any(m => m.Number == 1 && m.Text.StartsWith("Error in batch 2 of 3 · continuing", StringComparison.Ordinal)) && kept.Messages[^1].Text == "Summary: 2 batches ok / 1 with error.");
    Assert(kept.Error!.StartsWith("1 of 3 batches failed", StringComparison.Ordinal));
    // A lost connection (Fatal) stops even with continue after errors.
    var fatal = new CountingChannel { Fail = b => b.StartsWith("bad") ? BatchOutcome.Fatal : BatchOutcome.Ok }; var lost = new QueryOutput();
    Assert(ScriptRunner.Run(SqlScript.SplitBatches(sql), fatal, lost, true, default).Stopped && !fatal.Executions.ContainsKey("select 3") && lost.Messages[^1].Text == "Summary: 1 batch ok / 1 with error · 1 not run.");
    var repeated = new CountingChannel { Fail = _ => BatchOutcome.Failed };
    Assert(ScriptRunner.Run(SqlScript.SplitBatches("bad\ngo 3"), repeated, new QueryOutput(), true, default) is { Executions: 3, Failed: 3 });
    // All succeed with the option on: no error, and the summary says so.
    var clean = new QueryOutput(); ScriptRunner.Run(SqlScript.SplitBatches("select 1\ngo\nselect 2"), new CountingChannel(), clean, true, default);
    Assert(clean.Error == null && clean.Messages[^1].Text == "Summary: 2 batches ok / 0 with error.");
});
Check("The runner honours cancellation before each batch and bounds batches and go N executions", () => {
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var channel = new CountingChannel();
    try { ScriptRunner.Run(SqlScript.SplitBatches("select 1"), channel, new QueryOutput(), false, cancelled.Token); throw new Exception("Ran after cancellation"); } catch (OperationCanceledException) { }
    Assert(channel.Total == 0);
    foreach (var batches in new[] { Enumerable.Range(0, 257).Select(i => new SqlBatch("select " + i)).ToList(), Enumerable.Range(0, 11).Select(i => new SqlBatch("select " + i, 1000)).ToList() })
        try { ScriptRunner.Run(batches, channel, new QueryOutput(), false, default); throw new Exception("Accepted an oversized run"); } catch (FormatException) { }
    Assert(channel.Total == 0);
});
Check("Query timeout accepts 0 (no timeout) up to 3600 s; PROD is saved with the profile and old profiles are not PROD", () => {
    new ConnectionProfile("h", 5000, "master", "u", false, "", QueryTimeout: 0).Validate();
    new ConnectionProfile("h", 5000, "master", "u", false, "", QueryTimeout: 3600).Validate();
    foreach (int bad in new[] { -1, 3601 }) try { new ConnectionProfile("h", 5000, "master", "u", false, "", QueryTimeout: bad).Validate(); Assert(false); } catch (ArgumentException) { }
    var legacy = System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>("{\"Server\":\"h\",\"Port\":1,\"Database\":\"d\",\"User\":\"u\",\"UseTls\":false,\"TrustedFile\":\"\"}")!;
    Assert(!legacy.Production);
    var prod = new ConnectionProfile("prod-host", 5000, "master", "dba", false, "", Production: true);
    string json = System.Text.Json.JsonSerializer.Serialize(prod);
    Assert(json.Contains("\"Production\":true") && System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>(json) == prod);
});
Check("Server groups keep RS and IQ profiles without a database, with the same platform rules as a login", () => {
    string path = Path.Combine(Path.GetTempPath(), "ase-groups-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        var rs = new ConnectionProfile("rs-host", 5510, "", "sa", false, "", Platform: ServerPlatform.Rs);
        var iq = new ConnectionProfile("iq-host", 2638, "", "dba", false, "", Platform: ServerPlatform.Iq);
        var ase = new ConnectionProfile("ase-host", 5000, "master", "sa", false, "", Production: true);
        var groups = ServerGroupStore.Upsert(ServerGroupStore.Upsert([], new ServerGroup("RS only", [rs])), new ServerGroup("Mixed", [ase, iq, rs]));
        ServerGroupStore.Save(path, groups);
        var loaded = ServerGroupStore.Load(path);
        Assert(loaded.Count == 2 && loaded[0].Profiles.Single() == rs && loaded[1].Profiles.SequenceEqual(new[] { ase, iq, rs }) && loaded[1].Profiles[0].Production);
        foreach (var bad in new[] { rs with { Database = "master" }, ase with { Database = "" }, ase with { IqServerName = "iq1" } })
            try { ServerGroupStore.Validate([new ServerGroup("bad", [bad])]); throw new Exception("Accepted a profile the login rejects"); } catch (ArgumentException ex) { Assert(ex.Message.Contains("bad")); }
        // Files are not checked here: a moved CA must not make every group unreadable.
        ServerGroupStore.Validate([new ServerGroup("tls", [ase with { UseTls = true, TrustedFile = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".pem") }])]);
    }
    finally { foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(file); }
});
Check("A saved profile whose CA or identity file was deleted stays loadable and names the problem; the others are untouched", () => {
    string ca = Path.GetTempFileName(), identity = Path.GetTempFileName();
    try
    {
        var tls = new ConnectionProfile("tls-host", 5000, "master", "u", true, ca);
        var iq = new ConnectionProfile("iq-host", 2638, "", "u", true, ca, Platform: ServerPlatform.Iq, IqIdentityFile: identity);
        var plain = new ConnectionProfile("plain-host", 5000, "master", "u", false, "");
        tls.Validate(); iq.Validate();
        File.Delete(ca);
        // The store validates structure only (ValidateFields); the login still refuses with Validate.
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { plain, tls });
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<List<ConnectionProfile>>(json, new System.Text.Json.JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })!;
        foreach (var profile in reloaded) profile.ValidateFields();
        Assert(reloaded.Count == 2 && reloaded[1] == tls && reloaded[0].FileProblem() == null);
        Assert(reloaded[1].FileProblem() is { } problem && problem.Contains("trusted certificate file does not exist") && problem.Contains(ca));
        try { tls.Validate(); Assert(false); } catch (ArgumentException ex) { Assert(ex.Message.Contains(ca)); }
        File.Delete(identity);
        Assert(iq.FileProblem()!.Contains("IQ client identity file does not exist"));
        // Without TLS a stale CA path is never used, so it is not a problem.
        Assert((tls with { UseTls = false }).FileProblem() == null);
    }
    finally { File.Delete(ca); File.Delete(identity); }
});
Check("SqlRisk names every state-changing category, including RS commands, and ignores comments, strings and names", () => {
    var cases = new (string Sql, string Kind)[] {
        ("exec sp_dropuser u1", "sp_dropuser"), ("sp_addlogin x, 'secret'", "sp_addlogin"), ("exec master..sp_addumpdevice 'disk', 'd', '/d'", "sp_addumpdevice"),
        ("sp_dboption sales, 'select into', true", "sp_dboption"), ("sp_rename 't.c', 'd'", "sp_rename"), ("exec sp_configure 'max memory', 100", "sp_configure"),
        ("dbcc checkdb(master)", "dbcc checkdb"), ("DBCC  reindex(t)", "dbcc reindex"), ("dbcc traceon(3605, 302)", "dbcc traceon"), ("dbcc purgesqlcache", "dbcc purgesqlcache"),
        ("checkpoint", "checkpoint"), ("online database sales", "online database"), ("disk init name='d1', physname='/x', size='10M'", "disk init"),
        ("disk resize name='d1', size='1G'", "disk resize"), ("disk mirror name='d1', mirror='/m'", "disk mirror"), ("mount database all from '/m/manifest'", "mount database"),
        ("unmount database sales to '/m/manifest'", "unmount database"), ("quiesce database t1 hold sales", "quiesce"), ("kill 42", "kill"),
        ("dump database sales to '/d/s.dmp'", "dump"), ("dump tran sales with truncate_only", "dump"), ("load database sales from '/d/s.dmp'", "load"), ("shutdown with nowait", "shutdown"),
        ("truncate table t", "truncate"), ("reorg rebuild t", "reorg"), ("update statistics t", "update statistics"), ("update index statistics t i1", "update index statistics"),
        ("update t set x = 1", "update"), ("grant select on t to u", "grant"), ("revoke select on t from u", "revoke"),
        ("sysadmin sqm_purge_queue, 101, 0", "sysadmin"), ("ignore loss from ds1.db1 to ds2.db2", "ignore loss"), ("rebuild queues", "rebuild queues"),
        ("allow connections", "allow connections"), ("suspend connection to ds.db", "suspend"), ("resume connection to ds.db skip transaction", "resume"),
        ("drop connection to ds.db", "drop connection"), ("alter connection to ds.db set batch to 'off'", "alter connection"), ("create connection to ds.db set error class to rs_sqlserver_error_class", "create connection") };
    foreach (var (sql, kind) in cases)
        if (SqlRisk.StateChanges(sql).FirstOrDefault() != kind) throw new Exception($"{sql} → {string.Join(", ", SqlRisk.StateChanges(sql))}, expected {kind}");
    foreach (string safe in new[] {
        "select 'checkpoint; kill 1; dbcc checkdb(x); sp_dropuser u' as s -- shutdown; disk init\n/* sp_addlogin x; online database y; update statistics t */ select [truncate], \"dump database\" from t",
        "select reorganized, killer, checkpoints, loaded, dumped from t", "exec sp_helpdb\nexec sp_who\nexec sp_configure 'number of locks'",
        "dbcc traceon(3604)\ngo\ndbcc sqltext(12)\ngo\ndbcc stacktrace(12)\ngo\ndbcc traceoff(3604)", "admin who, sqm\nadmin health\nadmin version", "select * into #tmp from t\ndrop table #tmp" })
        if (SqlRisk.StateChanges(safe).Count != 0) throw new Exception("False positive: " + string.Join(", ", SqlRisk.StateChanges(safe)));
    using var recipes = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AseIsql.Desktop", "Assets", "ase-recipes.json")));
    var byId = recipes.RootElement.EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("sql").GetString()!);
    Assert(SqlRisk.StateChanges(byId["ase_tablas_mas_accedidas"]).Count == 0 && SqlRisk.StateChanges(byId["ase_top10_cpu"]).Count == 0 && SqlRisk.StateChanges(byId["rs_who_sqm"]).Count == 0);
    Assert(SqlRisk.StateChanges(byId["ase_act_kill_spid"]).Count > 0 && SqlRisk.StateChanges(byId["rs_resume_skip"]).Count > 0 && SqlRisk.StateChanges(byId["iq_drop_connection"]).Count > 0);
});
Check("PROD review lists each statement with its source line and names the go N repetitions of state-changing batches", () => {
    var findings = SqlRisk.Findings("select 1\n-- delete\nupdate t set x = 'a\nb'\ngo\ndelete from t");
    Assert(findings.Select(f => (f.Kind, f.Line, f.Statement)).SequenceEqual(new[] { ("update", 3, "update t set x = 'a"), ("delete", 6, "delete from t") }));
    var review = SqlRisk.Review("delete from log where id < 0\ngo 5\nselect 1\ngo 3");
    Assert(review.ChangesState && review.Repetitions.Single() == "Batch 1 runs 5 times (go 5): delete");
    Assert(!SqlRisk.Review("select 1\ngo 3").ChangesState && SqlRisk.Review("select 1\ngo 3").Repetitions.Count == 0);
});
// Light palettes added in 0.3.0 must read at WCAG AA (high contrast: AAA) with the colors the app actually applies:
// Themes.WithAccent derives OnAccent and Selection from Accent and Surface, so those two are recomputed here the same way.
// Earlier light palettes (Alba, Sandstone) are reported, not enforced: they predate this check and stay unchanged.
Check("New light palettes meet WCAG contrast for text, syntax, accent and selection", () => {
    var catalog = System.Text.Json.JsonSerializer.Deserialize<List<PaletteEntry>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dbmon-palettes.json")))!;
    string[] keys = ["Canvas", "Surface", "SurfaceRaised", "AlternateRow", "Ink", "Muted", "Rule", "RowRule", "Accent", "OnAccent", "Header", "HeaderInk", "Badge", "BadgeInk", "Notice", "NoticeInk", "LineNumber", "Selection", "SqlKeyword", "SqlString", "SqlComment", "SqlNumber", "ErrorInk"];
    (string Fore, string Back, bool Gutter)[] pairs = [("Ink", "Surface", false), ("Ink", "Canvas", false), ("Ink", "SurfaceRaised", false), ("Ink", "AlternateRow", false), ("Muted", "Surface", false), ("HeaderInk", "Header", false), ("BadgeInk", "Badge", false), ("NoticeInk", "Notice", false), ("OnAccent", "Accent", false), ("Accent", "Surface", false), ("LineNumber", "Surface", true), ("SqlKeyword", "Surface", false), ("SqlString", "Surface", false), ("SqlComment", "Surface", false), ("SqlNumber", "Surface", false), ("ErrorInk", "Surface", false), ("Ink", "Selection", false)];
    var enforced = new Dictionary<string, (double Text, double Gutter)> { ["papel"] = (4.5, 3), ["sepia"] = (4.5, 3), ["glaciar"] = (4.5, 3), ["contraste"] = (7, 4.5) };
    Assert(catalog.Count == 23 && catalog.Select(p => p.Id).Distinct().Count() == catalog.Count);
    var failures = new List<string>();
    foreach (var palette in catalog.Where(p => !p.DarkChrome))
    {
        bool enforce = enforced.TryGetValue(palette.Id, out var floor);
        if (enforce) Assert(keys.All(palette.Colors.ContainsKey));
        var colors = PaletteContrast.Effective(palette.Colors);
        var below = pairs.Select(p => (p, ratio: PaletteContrast.Ratio(colors[p.Fore], colors[p.Back]))).Where(x => x.ratio < (enforce ? (x.p.Gutter ? floor.Gutter : floor.Text) : (x.p.Gutter ? 3 : 4.5))).Select(x => $"{x.p.Fore}/{x.p.Back} {x.ratio:0.00}").ToList();
        if (enforce) failures.AddRange(below.Select(b => palette.Id + " " + b));
        else if (below.Count > 0) Console.WriteLine($"INFO existing light palette {palette.Id} (not enforced): " + string.Join(", ", below));
    }
    Assert(enforced.Keys.All(id => catalog.Any(p => p.Id == id && !p.DarkChrome)));
    if (failures.Count > 0) throw new Exception("Contrast below floor: " + string.Join("; ", failures));
    foreach (var palette in catalog.Where(p => enforced.ContainsKey(p.Id))) Console.WriteLine("INFO contrast " + PaletteContrast.Report(palette));
});
using (var iqDriver = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI\SAP IQ"))
{
    if (iqDriver?.GetValue("Driver") is not string iqLibrary || !File.Exists(iqLibrary))
        Console.WriteLine("SKIP native IQ TLS parser check: SAP IQ driver is not installed.");
    else Check("Installed IQ driver recognizes TLS with and without client identity on loopback", () => {
        string ca = Path.GetTempFileName(), identity = Path.GetTempFileName();
        try {
            foreach (bool withIdentity in new[] { false, true })
            {
                var profile = new ConnectionProfile("127.0.0.1", 1, "", "synthetic-user", true, ca, LoginTimeout: 1, Platform: ServerPlatform.Iq, IqIdentityFile: withIdentity ? identity : "", IqCertificateName: withIdentity ? "IQDBQA" : "");
                using var connection = new OdbcConnection(OdbcSession.BuildConnectionString(profile, "synthetic-db", withIdentity ? "synthetic-key" : ""));
                connection.ConnectionTimeout = 1;
                try { connection.Open(); throw new Exception("An unexpected local server accepted the synthetic connection"); }
                catch (OdbcException ex) {
                    // Port 1 has no IQ server. Reaching -100 proves the native driver accepted the TLS
                    // grammar, rather than rejecting it locally with -832/-895 before network lookup.
                    Assert(ex.Errors.Cast<OdbcError>().All(e => e.NativeError == -100));
                }
            }
        } finally { File.Delete(ca); File.Delete(identity); }
    });
}
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
    var legacy = System.Text.Json.JsonSerializer.Deserialize<ConnectionProfile>("{\"Server\":\"h\",\"Port\":1,\"Database\":\"d\",\"User\":\"u\",\"UseTls\":false,\"TrustedFile\":\"\"}")!;
    Assert(legacy.Platform == ServerPlatform.Ase && legacy.IqIdentityFile == "");
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
    Assert(AseSessionOptions.Script(off, off with { StatisticsTime = true }).SequenceEqual(new[] { "set statistics time on" }));
    Assert(AseSessionOptions.Script(off, off with { StatisticsIo = true }).SequenceEqual(new[] { "set textsize 16777216", "set plan for show_execio_xml to message on" }));
    // Turning on: noexec trails, and show_execio_xml to message on trails statistics time so the message buffer stays the active target.
    var all = off with { NoExec = true, StatisticsIo = true, StatisticsTime = true };
    Assert(AseSessionOptions.Script(off, all).SequenceEqual(new[] { "set statistics time on", "set textsize 16777216", "set plan for show_execio_xml to message on", "set noexec on" }));
    // Turning off: noexec leads, because nothing after it would execute otherwise.
    Assert(AseSessionOptions.Script(all, off).SequenceEqual(new[] { "set noexec off", "set statistics time off", "set plan for show_execio_xml off" }));
});
Check("A change made while noexec is on is bracketed, so it never depends on the server honouring SET there", () => {
    var off = AseSessionOptions.Default;
    var noexec = off with { NoExec = true };
    Assert(AseSessionOptions.Script(noexec, noexec with { StatisticsIo = true }).SequenceEqual(new[] { "set noexec off", "set textsize 16777216", "set plan for show_execio_xml to message on", "set noexec on" }));
    var all = off with { NoExec = true, StatisticsIo = true, StatisticsTime = true };
    Assert(AseSessionOptions.Script(all, all with { StatisticsTime = false }).SequenceEqual(new[] { "set noexec off", "set statistics time off", "set plan for show_execio_xml to message on", "set noexec on" }));
    Assert(AseSessionOptions.Script(noexec, noexec with { Compatibility = CompatibilityMode.Off }).SequenceEqual(new[] { "set noexec off", "set compatibility_mode off", "set noexec on" }));
    // Nothing to bracket when noexec itself is the only move, in either direction.
    Assert(AseSessionOptions.Script(noexec, off).SequenceEqual(new[] { "set noexec off" }));
    Assert(AseSessionOptions.Script(off, noexec).SequenceEqual(new[] { "set noexec on" }));
    Assert(AseSessionOptions.Script(noexec, noexec).Count == 0);
    // Both statistics measure execution, so noexec silences them and disables the plan read-back.
    Assert((noexec with { StatisticsTime = true }).StatisticsSilencedByNoExec && (noexec with { StatisticsIo = true }).StatisticsSilencedByNoExec);
    Assert(!(noexec with { StatisticsIo = true }).CapturesExecutionIo && (off with { StatisticsIo = true }).CapturesExecutionIo);
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
    var options = AseSessionOptions.Default with { NoExec = true, StatisticsIo = true, StatisticsTime = true, Compatibility = CompatibilityMode.Off };
    Assert(options.Tags.SequenceEqual(new[] { "NOEXEC", "STATS IO", "STATS TIME", "COMPAT OFF" }) && !options.IsDefault);
    // Compatibility alone is not the default state either: the chip has to show it.
    Assert(!(AseSessionOptions.Default with { Compatibility = CompatibilityMode.On }).IsDefault);
});
Check("Only the options that make ASE talk after the last row ask for a trailing flush, with a no-op re-assert", () => {
    var off = AseSessionOptions.Default;
    Assert(off.TrailingFlushStatement == null);
    // noexec alone executes nothing, so there is no trailing block to collect.
    Assert((off with { NoExec = true }).TrailingFlushStatement == null);
    Assert((off with { Compatibility = CompatibilityMode.Off }).TrailingFlushStatement == null);
    // Each one re-asserts an option that is already on, so the flush cannot change the session.
    Assert((off with { StatisticsTime = true }).TrailingFlushStatement == "set statistics time on");
    Assert((off with { StatisticsIo = true }).TrailingFlushStatement == null);
    var all = off with { StatisticsIo = true, StatisticsTime = true };
    Assert(all.TrailingFlushStatement == "set statistics time on" && AseSessionOptions.Script(all, all).Count == 0);
});
Check("ExecutionIo parses concatenated showplan_in_xml(0) documents, matches isql logical reads and filters internal read-backs", () => {
    const string buffer = """
<?xml version="1.0" encoding="UTF-8"?>
<query>
	<planVersion> 1.0 </planVersion>
	<statementNum>1</statementNum>
	<lineNum>1</lineNum>
	<text><![CDATA[Subordinate SQL Text: select count(*) from master..sysobjects]]></text>
	<opTree>
		<Emit><ScalarAgg>
			<IndexScan>
				<est><rowCnt>212</rowCnt><lio>3</lio><pio>3</pio></est>
				<act><rowCnt>217</rowCnt><lio>2</lio><pio>0</pio></act>
				<objName>master..sysobjects</objName>
				<scanType>IndexScan</scanType>
				<indName>csysobjects</indName>
			</IndexScan>
		</ScalarAgg></Emit>
	</opTree>
</query>
<?xml version="1.0" encoding="UTF-8"?>
<query>
	<statementNum>1</statementNum>
	<lineNum>2</lineNum>
	<text><![CDATA[Subordinate SQL Text: select count(*) from master..sysobjects o, master..syscolumns c where o.id = c.id]]></text>
	<optimizerStatistics>
		<statInfo><objName>syscolumns</objName><missingHistogram><column>id</column></missingHistogram></statInfo>
		<statInfo><objName>sysobjects</objName><missingHistogram><column>id</column></missingHistogram></statInfo>
	</optimizerStatistics>
	<opTree>
		<Emit><ScalarAgg><NestLoopJoin>
			<IndexScan>
				<est><rowCnt>212</rowCnt><lio>3</lio><pio>3</pio></est>
				<act><rowCnt>217</rowCnt><lio>2</lio><pio>0</pio></act>
				<objName>master..sysobjects</objName><corrName>o</corrName><scanType>IndexScan</scanType><indName>csysobjects</indName>
			</IndexScan>
			<IndexScan>
				<est><rowCnt>2309</rowCnt><lio>214</lio><pio>16</pio></est>
				<act><rowCnt>3147</rowCnt><lio>162</lio><pio>35</pio></act>
				<objName>master..syscolumns</objName><corrName>c</corrName><scanType>IndexScan</scanType><indName>csyscolumns</indName>
			</IndexScan>
		</NestLoopJoin></ScalarAgg></Emit>
	</opTree>
</query>
<?xml version="1.0" encoding="UTF-8"?>
<query><statementNum>1</statementNum><lineNum>1</lineNum><text><![CDATA[Subordinate SQL Text: select /* dbmon_execio */ @@textsize]]></text><opTree/></query>
<?xml version="1.0" encoding="UTF-8"?>
<query><statementNum>1</statementNum><lineNum>1</lineNum><text><![CDATA[Subordinate SQL Text: select showplan_in_xml(0)]]></text><opTree/></query>
""";
    var report = ExecutionIo.Parse(buffer);
    Assert(report.Unreadable == 0 && report.Plans.Count == 2);
    Assert(ExecutionIo.Summary(report.Plans[0]).SequenceEqual(new[] { "Table: master..sysobjects logical reads: 2, physical reads: 0, rows: 217" }));
    Assert(ExecutionIo.Summary(report.Plans[1]).SequenceEqual(new[] {
        "Table: master..sysobjects logical reads: 2, physical reads: 0, rows: 217",
        "Table: master..syscolumns logical reads: 162, physical reads: 35, rows: 3,147"
    }));
    Assert(report.Plans[1].MissingStatistics.SequenceEqual(new[] { "syscolumns.id", "sysobjects.id" }));
    var table = ExecutionIo.ToTable(report.Plans);
    Assert((string?)table.ExtendedProperties["ResultHeader"] == "Statistics IO" && table.Rows.Count == 3);
    Assert((string)table.Rows[2]["object"] == "master..syscolumns" && (string)table.Rows[2]["alias"] == "c" && (long)table.Rows[2]["logical_reads"] == 162L && (long)table.Rows[2]["est_logical_reads"] == 214L);
});
Console.WriteLine($"{passed} checks passed. No ASE server was contacted.");

sealed record PaletteEntry(string Id, string Label, bool DarkChrome, Dictionary<string, string> Colors);

static class PaletteContrast
{
    // #RRGGBB or #AARRGGBB (alpha ignored: only opaque keys are measured).
    static (int R, int G, int B) Rgb(string hex) { var h = hex.TrimStart('#'); if (h.Length == 8) h = h[2..]; return (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16)); }
    static double Linear(int channel) { double v = channel / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    static double Luminance(string hex) { var (r, g, b) = Rgb(hex); return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b); }
    public static double Ratio(string a, string b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    // Same derivation as Themes.WithAccent(palette, null) for a light palette.
    public static Dictionary<string, string> Effective(IReadOnlyDictionary<string, string> source)
    {
        var colors = new Dictionary<string, string>(source);
        var accent = Rgb(colors["Accent"]); var surface = Rgb(colors["Surface"]);
        static string Hex((int R, int G, int B) c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        // Light palettes deepen the accent by 10 % steps until it reads at 4.5:1; OnAccent and Selection use that result.
        while (Ratio(Hex(accent), colors["Surface"]) < 4.5) accent = ((int)(accent.R * .9), (int)(accent.G * .9), (int)(accent.B * .9));
        string derived = Hex(accent);
        colors["OnAccent"] = Ratio(derived, "#FFFFFF") >= Ratio(derived, "#07101F") ? "#FFFFFF" : "#07101F";
        int Mix(int fore, int back) => (int)Math.Round(fore * .45 + back * .55);
        colors["Selection"] = $"#{Mix(accent.R, surface.R):X2}{Mix(accent.G, surface.G):X2}{Mix(accent.B, surface.B):X2}";
        return colors;
    }
    public static string Report(PaletteEntry palette)
    {
        var c = Effective(palette.Colors);
        return $"{palette.Id} · {palette.Label} · Ink/Surface {Ratio(c["Ink"], c["Surface"]):0.0} · Muted/Surface {Ratio(c["Muted"], c["Surface"]):0.0} · Accent/Surface {Ratio(c["Accent"], c["Surface"]):0.0} · OnAccent/Accent {Ratio(c["OnAccent"], c["Accent"]):0.0} · Ink/Selection {Ratio(c["Ink"], c["Selection"]):0.0}";
    }
}

// Test double for ScriptRunner: counts every execution of each batch text and every plan read-back.
sealed class CountingChannel : IBatchChannel
{
    public readonly Dictionary<string, int> Executions = new(StringComparer.Ordinal);
    public readonly List<int> PlanReads = [];
    public int Arms, Total;
    public string PlanXml = "";
    public Func<string, BatchOutcome>? Fail;
    public string SourceTag => "ASE";
    public bool CapturesPlans { get; set; }
    public void ArmPlanCapture(CancellationToken cancellation) => Arms++;
    public string? ReadPlan(int index, QueryOutput output, CancellationToken cancellation) { PlanReads.Add(index); return PlanXml; }
    public BatchOutcome Execute(string batch, QueryOutput output, CancellationToken cancellation)
    {
        Executions[batch] = Executions.GetValueOrDefault(batch) + 1; Total++;
        var outcome = Fail?.Invoke(batch) ?? BatchOutcome.Ok;
        if (outcome != BatchOutcome.Ok) output.Error = "fixture failure: " + batch;
        return outcome;
    }
}

sealed class ThrowingEngine : IQueryEngine
{
    public string Name => "Throwing fixture";
    public QueryOutput Execute(string sql, CancellationToken cancellation = default) => throw new InvalidOperationException("fixture failure");
}
