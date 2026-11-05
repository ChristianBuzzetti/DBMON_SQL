using AseIsql;
using System.Data;
using System.Text.Json;

internal static class HealthRuleChecks
{
    private static DataTable Table(string[] names, params object[][] values)
    {
        var table = new DataTable();
        for (int i = 0; i < names.Length; i++)
        {
            var column = table.Columns.Add("column_" + i, typeof(object));
            column.Caption = names[i]; column.ExtendedProperties["SourceName"] = names[i];
        }
        foreach (var row in values) table.Rows.Add(row);
        return table;
    }

    private static QueryOutput Output(DataTable table) { var output = new QueryOutput(); output.Tables.Add(table); return output; }

    public static void Run(Action<string, Action> check, Action<bool> assert)
    {
        var settings = new HealthSettings();
        var defaults = new HealthRuleSet();
        var ase = HealthCheck.Probes(ServerPlatform.Ase);
        var dump = ase.Single(p => p.Id == "dump");
        HealthCell Evaluate(string id, DataTable table, HealthRule rule, HealthSettings? global = null) =>
            HealthCheck.Evaluate(HealthRuleSet.AllProbes.Single(p => p.Id == id), Output(table), global ?? settings, rule);
        HealthRule Rule(string id, double? warn = null, double? fail = null) => new() { ProbeId = id, WarnThreshold = warn, FailThreshold = fail };

        check("Health rules default to ignoring non-PROD log dumps while preserving PROD and legacy evaluation", () => {
            var output = Output(Table(["database_name", "age_hours"], ["dev", 4156]));
            var nonProduction = HealthCheck.Evaluate(dump, output, settings, defaults.RuleFor(dump, false));
            var production = HealthCheck.Evaluate(dump, output, settings, defaults.RuleFor(dump, true));
            assert(nonProduction.State == HealthState.Ok && nonProduction.Ignored && nonProduction.Caption == "OK · ignored by rule");
            assert(nonProduction.Details.Contains("4156") && production.State == HealthState.Fail);
            assert(HealthCheck.Evaluate(dump, output, settings).State == HealthState.Fail);
            HealthServer[] servers = [new("dev", ServerPlatform.Ase, [nonProduction]), new("prod", ServerPlatform.Ase, [production])];
            var matrix = HealthCheck.Matrix(servers);
            assert(servers[0].State == HealthState.Ok && (string)matrix.Rows[0]["Overall"] == "OK" && (string)matrix.Rows[1]["Overall"] == "FAIL");
            assert(matrix.Columns["probe_dump"]!.Caption == "Last log dump");
            assert(defaults.Production.All(r => r.Enabled && r.Severity == HealthSeverity.Fail));
            assert(defaults.NonProduction.Where(r => r.ProbeId != "dump").All(r => r.Enabled && r.Severity == HealthSeverity.Fail));
        });
        check("Health execution selects each target's PROD flag and still reads ignored probes", () => {
            var dev = new CountingEngine(HealthyAse); var prod = new CountingEngine(HealthyAse);
            var results = HealthCheck.ExecuteAsync([new(new("dev", dev), ServerPlatform.Ase), new(new("prod", prod), ServerPlatform.Ase, true)], settings).GetAwaiter().GetResult();
            assert(results[0].State == HealthState.Ok && results[1].State == HealthState.Fail);
            assert(dev.Sql.Count == ase.Count && prod.Sql.Count == ase.Count && dev.Sql.Contains(dump.Sql));
            assert(results[0].Cells.Single(c => c.Probe.Id == "dump").Ignored);
        });
        check("Health rules override each percentage probe independently and inherit unset global thresholds", () => {
            foreach (string id in new[] { "data", "log", "connections" })
            {
                assert(Evaluate(id, Table(["used_pct"], [80]), Rule(id)).State == HealthState.Ok);
                assert(Evaluate(id, Table(["used_pct"], [80]), Rule(id, 60, 75)).State == HealthState.Fail);
                assert(Evaluate(id, Table(["used_pct"], [80]), Rule(id, fail: 99), settings with { UsageWarnPercent = 70 }).State == HealthState.Warn);
                assert(Evaluate(id, Table(["used_pct"], [75]), Rule(id, 60, 75)).State == HealthState.Warn);
            }
            var iq = Table(["Name", "Value"], ["Main IQ Blocks Used:", "80%"], ["Temporary IQ Blocks Used:", "5%"]);
            assert(Evaluate("iqspace", iq, Rule("iqspace")).State == HealthState.Ok);
            assert(Evaluate("iqspace", iq, Rule("iqspace", 60, 75)).State == HealthState.Fail);
            assert(Evaluate("data", Table(["used_pct"], [80]), Rule("data")).State == HealthState.Ok);
        });
        check("Health rules override dump hours and inclusive blocked counts", () => {
            assert(Evaluate("dump", Table(["age_hours"], [50]), Rule("dump")).State == HealthState.Fail);
            assert(Evaluate("dump", Table(["age_hours"], [50]), Rule("dump", 100, 200)).State == HealthState.Ok);
            assert(Evaluate("blocked", Table(["blocked_count"], [10]), Rule("blocked")).State == HealthState.Fail);
            assert(Evaluate("blocked", Table(["blocked_count"], [10]), Rule("blocked", 10, 20)).State == HealthState.Warn);
            assert(Evaluate("blocked", Table(["blocked_count"], [20]), Rule("blocked", 10, 20)).State == HealthState.Fail);
        });
        check("Health severity Warn caps failures and Ignore suppresses errors and unavailable data", () => {
            var warning = Rule("dump") with { Severity = HealthSeverity.Warn };
            assert(Evaluate("dump", Table(["age_hours"], [4156]), warning).State == HealthState.Warn);
            assert(HealthCheck.Evaluate(dump, new QueryOutput { Error = "permission denied" }, settings, warning).State == HealthState.Warn);
            assert(HealthCheck.Evaluate(dump, new QueryOutput { Error = "permission denied" }, settings, defaults.RuleFor(dump, false)).State == HealthState.Ok);
            assert(HealthCheck.Evaluate(dump, new QueryOutput(), settings, defaults.RuleFor(dump, false)).Ignored);
            assert(Evaluate("dump", Table(["age_hours"], [1]), warning).State == HealthState.Ok);
            var ignoredFailure = new HealthCell(dump, HealthState.Fail, "ignored", "") { Ignored = true };
            assert(new HealthServer("dev", ServerPlatform.Ase, [ignoredFailure]).State == HealthState.Ok);
        });
        check("Disabled health probes never execute SQL and all-disabled servers stay OK", () => {
            var rules = defaults with { NonProduction = defaults.NonProduction.Select(r => r.ProbeId == "dump" ? r with { Enabled = false } : r).ToList() };
            var engine = new CountingEngine(HealthyAse);
            var result = HealthCheck.ExecuteAsync([new(new("dev", engine), ServerPlatform.Ase)], settings, rules).GetAwaiter().GetResult().Single();
            assert(engine.Sql.Count == ase.Count - 1 && !engine.Sql.Contains(dump.Sql));
            var cell = result.Cells.Single(c => c.Probe.Id == "dump");
            assert(cell.Ignored && cell.Summary == "disabled by rule" && result.State == HealthState.Ok);
            var allDisabled = rules with { NonProduction = rules.NonProduction.Select(r => r with { Enabled = false }).ToList() };
            var unused = new CountingEngine(_ => throw new Exception("A disabled probe executed"));
            result = HealthCheck.ExecuteAsync([new(new("dev", unused), ServerPlatform.Ase)], settings, allDisabled).GetAwaiter().GetResult().Single();
            assert(unused.Sql.Count == 0 && result.State == HealthState.Ok && result.Cells.Count == ase.Count && result.Cells.All(c => c.Ignored));
        });
        check("Health rules cap thrown driver failures and cancellation outcomes", () => {
            var rules = defaults with { NonProduction = defaults.NonProduction.Select(r => r with { Severity = HealthSeverity.Warn }).ToList() };
            var broken = new CountingEngine(_ => throw new InvalidOperationException("denied"));
            var result = HealthCheck.ExecuteAsync([new(new("rs", broken), ServerPlatform.Rs)], settings, rules).GetAwaiter().GetResult().Single();
            assert(result.State == HealthState.Warn && result.Cells.All(c => c.State == HealthState.Warn));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var unused = new CountingEngine(_ => throw new Exception("SQL executed after cancellation"));
            rules = rules with { NonProduction = rules.NonProduction.Select(r => r.ProbeId == "rsdown" ? r with { Severity = HealthSeverity.Ignore } : r).ToList() };
            result = HealthCheck.ExecuteAsync([new(new("rs", unused), ServerPlatform.Rs)], settings, rules, cancelled.Token).GetAwaiter().GetResult().Single();
            assert(unused.Sql.Count == 0 && result.State == HealthState.Warn && result.Cells.Single(c => c.Probe.Id == "rsdown").Ignored);
        });
        check("Health rule validation rejects invalid ranges and compares overrides to inherited globals", () => {
            void Invalid(HealthRule rule)
            {
                try { rule.Validate(HealthRuleSet.AllProbes.Single(p => p.Id == rule.ProbeId), settings); assert(false); }
                catch (ArgumentException) { }
            }
            Invalid(Rule("data", 95, 95)); Invalid(Rule("data", -1, 95)); Invalid(Rule("data", 85, 101));
            Invalid(Rule("iqspace", double.NaN, 95)); Invalid(Rule("iqspace", 85, double.PositiveInfinity));
            Invalid(Rule("blocked", 0, 10)); Invalid(Rule("dump", 0, 48)); Invalid(Rule("blocked", 1.5, 10));
            Invalid(Rule("data", warn: 96)); Invalid(Rule("data", fail: 84)); Invalid(Rule("version", 1, 2));
            Invalid(Rule("data") with { Severity = (HealthSeverity)99 });
            var custom = settings with { UsageWarnPercent = 50, UsageFailPercent = 70 };
            Rule("data", 60).Validate(ase.Single(p => p.Id == "data"), custom);
            try { Rule("data", 80).Validate(ase.Single(p => p.Id == "data"), custom); assert(false); } catch (ArgumentException) { }
        });
        check("Health rules JSON round-trips both environments and preserves unknown probe ids", () => {
            string folder = Path.Combine(Path.GetTempPath(), "dbmon-health-rules-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "health-rules.json");
            try
            {
                var future = Rule("future-probe", -5, -10) with { Enabled = false, Severity = HealthSeverity.Ignore };
                var rules = defaults with {
                    Production = [Rule("data", 70, 90) with { Severity = HealthSeverity.Warn }, future],
                    NonProduction = [Rule("dump", 100, 200) with { Enabled = false }] };
                rules.Save(path);
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                assert(json.RootElement.GetProperty("version").GetInt32() == 1);
                assert(json.RootElement.GetProperty("production")[0].GetProperty("severity").GetString() == "Warn");
                var loaded = HealthRuleSet.Load(path, out string? warning);
                assert(warning == null && loaded.Production.SequenceEqual(rules.Production) && loaded.NonProduction.SequenceEqual(rules.NonProduction));
                assert(loaded.Production.Single(r => r.ProbeId == future.ProbeId) == future);
                assert(loaded.RuleFor(dump, true).Severity == HealthSeverity.Fail && !loaded.RuleFor(dump, false).Enabled);
                loaded.Save(path);
                loaded = HealthRuleSet.Load(path, out warning);
                assert(warning == null && loaded.Production.Contains(future));
                // Validation fails before replacing an existing file.
                string previous = File.ReadAllText(path);
                try { (rules with { Production = [Rule("data", 99, 90)] }).Save(path); assert(false); } catch (ArgumentException) { }
                assert(File.ReadAllText(path) == previous);
                File.WriteAllText(path, "{\"version\":1,\"production\":[{\"probeId\":\"data\",\"warnThreshold\":60}],\"nonProduction\":[]}");
                loaded = HealthRuleSet.Load(path, out warning, settings with { UsageWarnPercent = 50, UsageFailPercent = 70 });
                assert(warning == null && loaded.Production.Single().WarnThreshold == 60);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
        check("Missing health rules use defaults silently; corrupt or invalid JSON returns defaults with a warning", () => {
            string folder = Path.Combine(Path.GetTempPath(), "dbmon-health-rules-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "health-rules.json");
            try
            {
                var loaded = HealthRuleSet.Load(path, out string? warning);
                assert(warning == null && loaded.RuleFor(dump, false).Severity == HealthSeverity.Ignore);
                Directory.CreateDirectory(folder);
                foreach (string json in new[] { "{broken", "null", "{}", "{\"version\":2}", "{\"version\":1,\"production\":null}",
                    "{\"version\":1,\"nonProduction\":[null]}",
                    "{\"version\":1,\"production\":[{\"probeId\":\"data\",\"warnThreshold\":95,\"failThreshold\":90}]}",
                    "{\"version\":1,\"production\":[{\"probeId\":\"dump\"},{\"probeId\":\"dump\"}]}" })
                {
                    File.WriteAllText(path, json); loaded = HealthRuleSet.Load(path, out warning);
                    assert(!string.IsNullOrWhiteSpace(warning) && loaded.RuleFor(dump, false).Severity == HealthSeverity.Ignore && loaded.RuleFor(dump, true).Severity == HealthSeverity.Fail);
                    assert(File.ReadAllText(path) == json);
                }
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
    }

    private static QueryOutput HealthyAse(string sql)
    {
        var probe = HealthCheck.Probes(ServerPlatform.Ase).Single(p => p.Sql == sql);
        return Output(probe.Id switch {
            "version" => Table(["version"], ["ASE fixture"]),
            "uptime" => Table(["boot_time", "uptime_hours"], [new DateTime(2026, 1, 1), 100]),
            "databases" => Table(["name", "status", "status2"], ["app", 0, 0]),
            "dump" => Table(["name", "last_dump", "age_hours"], ["app", new DateTime(2026, 1, 1), 4156]),
            "blocked" => Table(["blocked_count"], [0]),
            _ => Table(["used_pct"], [10]) });
    }

    private sealed class CountingEngine(Func<string, QueryOutput> execute) : IQueryEngine
    {
        public string Name => "Health rules fixture";
        public List<string> Sql { get; } = [];
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) { Sql.Add(sql); return execute(sql); }
    }
}
