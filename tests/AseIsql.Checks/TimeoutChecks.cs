using AseIsql;
using AseIsql.Odbc;
using System.Data;

// Query timeout default 0, the legacy-60 migration rule and OdbcSession.OpenSibling, all through the scripted driver seam.
static class TimeoutChecks
{
    private static readonly ConnectionProfile Profile = new("fixture", 5000, "master", "fixture_user", false, "");
    private static void Assert(bool value) { if (!value) throw new Exception("Timeout check assertion failed."); }
    private static DataTable One()
    {
        var table = new DataTable(); table.Columns.Add("n", typeof(int)); table.Rows.Add(1); return table;
    }

    public static void Run(Action<string, Action> check)
    {
        check("Default profile query timeout is 0 (no limit) and 0..3600 stays valid", () => {
            Assert(Profile.QueryTimeout == 0);
            Profile.ValidateFields();
            (Profile with { QueryTimeout = 3600 }).ValidateFields();
            foreach (int bad in new[] { -1, 3601 })
                try { (Profile with { QueryTimeout = bad }).ValidateFields(); Assert(false); } catch (ArgumentException) { }
        });
        check("Legacy query timeout 60 migrates to 0 only for stores older than format 2", () => {
            var legacy = Profile with { QueryTimeout = 60 };
            Assert(legacy.MigrateLegacyTimeout(0).QueryTimeout == 0 && legacy.MigrateLegacyTimeout(1).QueryTimeout == 0);
            Assert(legacy.MigrateLegacyTimeout(ConnectionProfile.CurrentStoreVersion).QueryTimeout == 60);
            Assert((Profile with { QueryTimeout = 120 }).MigrateLegacyTimeout(0).QueryTimeout == 120);
        });
        check("Server groups without a version migrate the old 60 s timeout and are saved as format 2", () => {
            string path = Path.Combine(Path.GetTempPath(), "dbmon-timeout-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var old = new ServerGroup("g", [Profile with { Server = "h", QueryTimeout = 60 }]);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { new { old.Name, old.Profiles } }));
                var loaded = ServerGroupStore.Load(path);
                Assert(loaded.Single().Profiles.Single().QueryTimeout == 0 && loaded.Single().Version == ConnectionProfile.CurrentStoreVersion);
                ServerGroupStore.Save(path, [old]);
                Assert(ServerGroupStore.Load(path).Single().Profiles.Single().QueryTimeout == 60);
            }
            finally { File.Delete(path); }
        });
        check("OdbcSession runs batches with CommandTimeout 0 by default and the explicit tool timeout when given", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("select 1", One()); connection.Add("select 2", One());
            using var session = OdbcSession.Open(Profile, "fixture-secret", "", connection);
            Assert(session.Execute("select 1").Error == null);
            Assert(connection.Executed[^1].CommandTimeout == 0);
            Assert(session.Execute("select 2", CancellationToken.None, userRun: false, timeoutSeconds: 10).Error == null);
            Assert(connection.Executed[^1].CommandTimeout == 10);
        });
        check("OdbcSession names the query timeout when the driver reports HYT00", () => {
            var connection = new ScriptedOdbcConnection().WithFacts();
            connection.Add("select slow").Error = new ScriptedOdbcException(new OdbcDiagnostic(0, "HYT00", "Timeout expired"));
            using var session = OdbcSession.Open(Profile with { QueryTimeout = 5 }, "fixture-secret", "", connection);
            var output = session.Execute("select slow");
            Assert(output.Error == "Query timeout (5 s) reached; set 0 in the connection window for no limit.");
        });
        check("OpenSibling opens a second connection with the same connection string and refuses a disposed session", () => {
            var first = new ScriptedOdbcConnection().WithFacts();
            var second = new ScriptedOdbcConnection().WithFacts();
            string? text = null;
            using var session = OdbcSession.Open(Profile, "fixture-secret", "", first);
            using (var sibling = session.OpenSibling(value => { text = value; return second; }))
            {
                Assert(sibling.IsOpen && !ReferenceEquals(sibling, session) && second.State == ConnectionState.Open);
                Assert(text == OdbcSession.BuildConnectionString(sibling.Profile, "fixture-secret"));
                Assert(sibling.Profile.Database == session.Facts.Database);
            }
            Assert(second.Disposed && !first.Disposed);
            session.Dispose();
            try { session.OpenSibling(_ => throw new Exception("unexpected connect")); Assert(false); } catch (ObjectDisposedException) { }
        });
    }
}
