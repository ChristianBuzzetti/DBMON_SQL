using AseIsql;
using System.Text.RegularExpressions;

// 1.0.7: the ASE statement cache makes a repeated compile skip the optimizer, so show_final_plan_xml writes nothing.
// A tiny pure-C# model of that behavior (no ODBC): server-wide statement cache, per-session SET state and message buffer.
static class Fix107Checks
{
    private const string UserSql = "select count(*) from master..sysobjects where type = 'U'";
    private static void Assert(bool value, string what = "") { if (!value) throw new Exception("1.0.7 assertion failed. " + what); }

    private sealed class FakeServer { public readonly HashSet<string> Cache = new(StringComparer.Ordinal); }

    private sealed class FakeSession(FakeServer server)
    {
        public bool NoExec, PlanOption, StatementCache = true;
        public string Buffer = "";
        private static readonly Regex Comment = new(@"^\s*/\*.*?\*/\s*", RegexOptions.Singleline | RegexOptions.NonBacktracking);
        private static string Normalize(string text) => Regex.Replace(Comment.Replace(text, ""), @"\s+", " ").Trim().ToLowerInvariant();

        // Returns the showplan_in_xml buffer for the read-back, null for everything else.
        public string? Run(string text)
        {
            string sql = Normalize(text);
            bool isSet = sql.StartsWith("set ", StringComparison.Ordinal);
            if (NoExec && isSet && sql != "set noexec off") return null; // ignored under noexec
            if (isSet)
            {
                switch (sql)
                {
                    case "set noexec off": NoExec = false; break;
                    case "set noexec on": NoExec = true; break;
                    case "set statement_cache off": StatementCache = false; break;
                    case "set statement_cache on": StatementCache = true; break;
                    case "set plan for show_final_plan_xml to message on": PlanOption = true; break;
                    case "set plan for show_final_plan_xml off": PlanOption = false; break;
                }
                return null;
            }
            if (sql.StartsWith("select showplan_in_xml(", StringComparison.Ordinal)) return Buffer;
            if (NoExec)
            {
                if (!PlanOption) return null;
                if (StatementCache && server.Cache.Contains(sql)) return null; // cached plan: the optimizer does not run
                Buffer = "<?xml version=\"1.0\"?><query><text>" + sql + "</text><opTree><Emit><est><rowCnt>1</rowCnt></est></Emit></opTree></query>";
                server.Cache.Add(sql);
                return null;
            }
            if (StatementCache) server.Cache.Add(sql); // a normal run compiles and caches
            return null;
        }

        public string Replay(IEnumerable<string> commands)
        {
            string xml = "";
            foreach (string command in commands)
                if (Run(command) is { } buffer && buffer.Length > 0) xml = buffer;
            return xml;
        }
    }

    public static void Run(Action<string, Action> check)
    {
        check("Fake ASE model: the 1.0.3/1.0.6 sequence (no statement_cache off) returns XML once, then an empty buffer", () => {
            var server = new FakeServer();
            var options = AseSessionOptions.Default;
            var legacy = EstimatedPlan.Sequence(UserSql, options).Where(c => c != EstimatedPlan.CacheOff && c != EstimatedPlan.CacheOn).ToList();
            Assert(legacy.Count == EstimatedPlan.Sequence(UserSql, options).Count - 2);
            Assert(new FakeSession(server).Replay(legacy).Length > 0, "first run");
            Assert(new FakeSession(server).Replay(legacy).Length == 0, "second run should be empty (cached)");
        });
        check("Estimated plan with statement_cache off returns XML on fresh sessions and after a user run of the same SQL, and restores the session", () => {
            var server = new FakeServer();
            var options = AseSessionOptions.Default;
            var sequence = EstimatedPlan.Sequence(UserSql, options);
            Assert(new FakeSession(server).Replay(sequence).Length > 0, "first");
            Assert(new FakeSession(server).Replay(sequence).Length > 0, "second");
            var used = new FakeSession(server);
            _ = used.Run(UserSql);
            Assert(server.Cache.Count > 0);
            Assert(used.Replay(sequence).Length > 0, "after a user run");
            Assert(used.StatementCache && !used.NoExec && !used.PlanOption, $"state cache={used.StatementCache} noexec={used.NoExec} plan={used.PlanOption}");

            var tracked = AseSessionOptions.Default with { NoExec = true, StatisticsIo = true };
            var prepare = EstimatedPlan.Prepare(tracked);
            Assert(prepare.ToList().IndexOf(EstimatedPlan.CacheOff) > prepare.ToList().IndexOf("set noexec off") && prepare.Contains(EstimatedPlan.CacheOff));
            var session = new FakeSession(server) { NoExec = true };
            Assert(session.Replay(EstimatedPlan.Sequence(UserSql, tracked)).Length > 0, "noexec session");
            Assert(session.StatementCache && session.NoExec && !session.PlanOption, "restored tracked options");
            Assert(EstimatedPlan.Sequence(UserSql, options, secondRead: true).Contains(EstimatedPlan.Read(-1)));
        });
    }
}
