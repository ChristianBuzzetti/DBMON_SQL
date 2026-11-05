using AseIsql;
using System.Text.RegularExpressions;

// 1.0.5: health/catalog space arithmetic never overflows ASE numeric precision on big servers.
static class Fix105Checks
{
    private static void Assert(bool value, string what = "") { if (!value) throw new Exception("1.0.5 assertion failed. " + what); }
    private static readonly Regex Pct = new(@"convert\(numeric\(9,2\),.*\)\s+as\s+used_pct\b", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static void Run(Action<string, Action> check)
    {
        check("ASE health space and connection percentages use bigint/float sums and a bounded numeric(9,2) result", () => {
            var probes = HealthCheck.Probes(ServerPlatform.Ase);
            foreach (var probe in probes)
            {
                Assert(!probe.Sql.Contains("numeric(20,0)", StringComparison.OrdinalIgnoreCase), probe.Id + " uses numeric(20,0)");
                Assert(!probe.Sql.Contains("100.0", StringComparison.Ordinal), probe.Id + " multiplies by an exact 100.0 literal");
                if (probe.Sql.Contains(" as used_pct", StringComparison.OrdinalIgnoreCase)) Assert(Pct.IsMatch(probe.Sql), probe.Id + " used_pct is not cast to numeric(9,2)");
            }
            foreach (string id in new[] { "data", "log", "connections" })
            {
                string sql = probes.Single(p => p.Id == id).Sql;
                Assert(Pct.IsMatch(sql) && sql.Contains("convert(float,", StringComparison.OrdinalIgnoreCase) && sql.Contains("nullif(", StringComparison.OrdinalIgnoreCase), id);
            }
            string log = probes.Single(p => p.Id == "log").Sql;
            Assert(log.Contains("case when lct_admin('logsegment_freepages',d.dbid) >= 0 then lct_admin('logsegment_freepages',d.dbid) end"), "lct_admin guard");
            Assert(!CatalogSql.AseTableDetails("app", 42).Contains("numeric(20,0)"), "table details");
        });
        check("ASE space recipes compute percentages in float and guard the divisor", () => {
            string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "AseIsql.Desktop", "Assets", "ase-recipes.json"));
            using var recipes = System.Text.Json.JsonDocument.Parse(text);
            foreach (var recipe in recipes.RootElement.EnumerateArray())
            {
                string sql = recipe.TryGetProperty("sql", out var value) ? value.GetString() ?? "" : "";
                Assert(!Regex.IsMatch(sql, @"100\.0\s*\*\s*(sum|lct_admin)\(", RegexOptions.IgnoreCase), "recipe multiplies an exact 100.0 by a page sum");
            }
        });
    }
}
