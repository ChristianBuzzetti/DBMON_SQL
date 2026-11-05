using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AseIsql;

public enum HealthState { Ok, Warn, Fail }
public enum HealthMetric { Information, DatabaseFlags, Percent, DumpAge, Blocked, IqSpace, RsHealth, RsDown }
public sealed record HealthProbe(string Id, string Title, string Sql, HealthMetric Metric);
public sealed record HealthCell(HealthProbe Probe, HealthState State, string Summary, string Details)
{
    public bool Ignored { get; init; }
    public string Caption => State.ToString().ToUpperInvariant() + " · " + Summary;
}
public sealed record HealthServer(string Label, ServerPlatform Platform, IReadOnlyList<HealthCell> Cells)
{
    public HealthState State => Cells.Count == 0 ? HealthState.Fail : Cells.Where(c => !c.Ignored).Select(c => c.State).DefaultIfEmpty(HealthState.Ok).Max();
}
public sealed record HealthTarget(RunTarget Target, ServerPlatform Platform, bool Production = false);

public sealed record HealthSettings
{
    public double UsageWarnPercent { get; init; } = 85;
    public double UsageFailPercent { get; init; } = 95;
    public int BlockedWarnCount { get; init; } = 1;
    public int BlockedFailCount { get; init; } = 10;
    public int DumpWarnHours { get; init; } = 24;
    public int DumpFailHours { get; init; } = 48;
    public int ServerTimeoutSeconds { get; init; } = 60;
    public void Validate()
    {
        if (!double.IsFinite(UsageWarnPercent) || !double.IsFinite(UsageFailPercent) || UsageWarnPercent < 0 || UsageWarnPercent >= UsageFailPercent || UsageFailPercent > 100 ||
            BlockedWarnCount < 1 || BlockedFailCount <= BlockedWarnCount || DumpWarnHours < 1 || DumpFailHours <= DumpWarnHours || ServerTimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("Invalid health thresholds or server timeout (1–3600 seconds).");
    }
    public static HealthSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new IOException("Health settings exceed 64 KiB.");
        var settings = JsonSerializer.Deserialize<HealthSettings>(File.ReadAllText(path), new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new JsonException("Empty health settings.");
        settings.Validate(); return settings;
    }
}

public static class HealthCheck
{
    // A database whose log shares a fragment with data (segmap 7, e.g. master) has no separate log usage and cannot
    // dump its transaction log; the data probe reports it as data+log instead.
    private static string NoMixedLog(string dbid) =>
        "not exists (select 1 from master..sysusages m where m.dbid = " + dbid + " and (m.segmap & 4) != 0 and (m.segmap & 3) != 0)";

    // Each query runs independently: missing privileges or a version-dependent column cannot hide other probes.
    public static IReadOnlyList<HealthProbe> Probes(ServerPlatform platform) => platform switch
    {
        ServerPlatform.Ase => [
            new("version", "Version", "select @@version as version", HealthMetric.Information),
            new("uptime", "Uptime", "select crdate as boot_time, datediff(hour, crdate, getdate()) as uptime_hours from master..sysdatabases where name = 'tempdb'", HealthMetric.Information),
            new("databases", "Database flags", "select name, status, status2 from master..sysdatabases order by name", HealthMetric.DatabaseFlags),
            new("data", "Data usage", "select d.name as database_name, sum(convert(numeric(20,0), u.size)) as total_pages, sum(convert(numeric(20,0), curunreservedpgs(u.dbid,u.lstart,u.unreservedpgs))) as free_pages, 100.0 * (1.0 - sum(convert(numeric(20,0), curunreservedpgs(u.dbid,u.lstart,u.unreservedpgs))) / nullif(sum(convert(numeric(20,0),u.size)),0)) as used_pct, case when max(case when (u.segmap & 4)!=0 then 1 else 0 end)=1 then 'data+log' else 'data' end as segments from master..sysdatabases d join master..sysusages u on d.dbid=u.dbid where u.segmap != 4 and (d.status & 288)=0 and (isnull(d.status2,0) & 48)=0 group by d.name order by d.name", HealthMetric.Percent),
            new("log", "Log usage", "select d.name as database_name, sum(convert(numeric(20,0),u.size)) as total_pages, lct_admin('logsegment_freepages',d.dbid) as free_pages, 100.0 * (1.0 - convert(numeric(20,0),lct_admin('logsegment_freepages',d.dbid)) / nullif(sum(convert(numeric(20,0),u.size)),0)) as used_pct from master..sysdatabases d join master..sysusages u on d.dbid=u.dbid where (u.segmap & 4)!=0 and (d.status & 288)=0 and (isnull(d.status2,0) & 48)=0 and " + NoMixedLog("d.dbid") + " group by d.name,d.dbid order by d.name", HealthMetric.Percent),
            new("dump", "Last log dump", "select name, dumptrdate as last_dump, datediff(hour,dumptrdate,getdate()) as age_hours from master..sysdatabases d where dbid > 3 and name not like 'syb%' and (status & 8)=0 and (isnull(status3,0) & 4194304)=0 and " + NoMixedLog("d.dbid") + " order by name", HealthMetric.DumpAge),
            new("blocked", "Blocked processes", "select count(*) as blocked_count from master..sysprocesses where blocked != 0", HealthMetric.Blocked),
            new("connections", "User connections", "select count(*) as user_connections, (select c.value from master..syscurconfigs c join master..sysconfigures s on s.config=c.config where s.name='number of user connections') as max_connections, 100.0 * count(*) / nullif((select c.value from master..syscurconfigs c join master..sysconfigures s on s.config=c.config where s.name='number of user connections'),0) as used_pct from master..sysprocesses where suid > 0 and spid != 0", HealthMetric.Percent)],
        ServerPlatform.Iq => [new("version", "Version", "select @@version as version", HealthMetric.Information), new("iqspace", "IQ main / temp space", "sp_iqstatus", HealthMetric.IqSpace)],
        ServerPlatform.Rs => [new("rshealth", "RS health", "admin health", HealthMetric.RsHealth), new("rsdown", "RS threads down", "admin who_is_down", HealthMetric.RsDown)],
        _ => throw new ArgumentException("Unknown platform.")
    };

    public static HealthState Threshold(double value, double warn, double fail, bool inclusive = false) =>
        (inclusive ? value >= fail : value > fail) ? HealthState.Fail : (inclusive ? value >= warn : value > warn) ? HealthState.Warn : HealthState.Ok;

    public static HealthCell Evaluate(HealthProbe probe, QueryOutput output, HealthSettings settings) =>
        Evaluate(probe, output, settings, new HealthRule { ProbeId = probe.Id });

    public static HealthCell Evaluate(HealthProbe probe, QueryOutput output, HealthSettings settings, HealthRule rule)
    {
        settings.Validate(); rule.Validate(probe, settings);
        string details = "SQL:\n" + probe.Sql + "\n\n" + Details(output);
        HealthCell Cell(HealthState severity, string summary) => rule.Apply(new(probe, severity, summary, details));
        if (!rule.Enabled) return Cell(HealthState.Ok, "Probe not executed");
        if (output.Error != null) return Cell(HealthState.Fail, output.Error);
        var rows = output.Tables.SelectMany(t => t.Rows.Cast<DataRow>()).ToArray();
        if (probe.Metric == HealthMetric.RsDown) return output.Tables.Count == 0 ? Cell(HealthState.Warn, "No thread result received") : Cell(rows.Length == 0 ? HealthState.Ok : HealthState.Fail, rows.Length + " threads down");
        if (rows.Length == 0) return Cell(HealthState.Warn, "No data / not available for this login");
        HealthState state = HealthState.Ok;
        try
        {
            void Include(HealthState next) { if (next > state) state = next; }
            switch (probe.Metric)
            {
                case HealthMetric.Information:
                    if (rows.Any(r => r.ItemArray.All(v => v is DBNull || string.IsNullOrWhiteSpace(ResultFormats.Cell(v))))) return Cell(HealthState.Warn, "Empty information");
                    return Cell(HealthState.Ok, probe.Id == "uptime" ? CatalogData.Text(rows[0], "uptime_hours") + " hours · since " + CatalogData.Text(rows[0], "boot_time") : ResultFormats.Cell(rows[0][0]));
                case HealthMetric.DatabaseFlags:
                    int flagged = 0;
                    foreach (var row in rows)
                    {
                        int status = Convert.ToInt32(CatalogData.Value(row, "status"), CultureInfo.InvariantCulture);
                        int status2 = CatalogData.Value(row, "status2") is DBNull ? 0 : Convert.ToInt32(CatalogData.Value(row, "status2"), CultureInfo.InvariantCulture);
                        var next = (status & 288) != 0 || (status2 & 176) != 0 ? HealthState.Fail : (status & 6144) != 0 ? HealthState.Warn : HealthState.Ok;
                        if (next != HealthState.Ok)
                        {
                            flagged++;
                            var flags = new List<string>();
                            if ((status & 256) != 0) flags.Add("suspect");
                            if ((status & 32) != 0) flags.Add("for load / recovery prevented");
                            if ((status2 & 48) != 0) flags.Add("offline / recovering");
                            if ((status2 & 128) != 0) flags.Add("suspect pages");
                            if ((status & 2048) != 0) flags.Add("dbo only");
                            if ((status & 4096) != 0) flags.Add("single user");
                            string name = CatalogData.TryValue(row, "name", out var value) ? ResultFormats.Cell(value) : "database";
                            details += "\n" + name + ": " + string.Join(", ", flags);
                        }
                        Include(next);
                    }
                    return Cell(state, flagged + " databases flagged (see details)");
                case HealthMetric.Percent:
                case HealthMetric.DumpAge:
                case HealthMetric.Blocked:
                    string key = probe.Metric == HealthMetric.Percent ? "used_pct" : probe.Metric == HealthMetric.DumpAge ? "age_hours" : "blocked_count";
                    double max = 0; int unknown = 0;
                    foreach (var row in rows)
                    {
                        var raw = CatalogData.Value(row, key);
                        if (raw is DBNull) { unknown++; Include(HealthState.Warn); continue; }
                        double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        if (!double.IsFinite(value) || value < 0 || (probe.Metric == HealthMetric.Percent && value > 100)) { unknown++; Include(HealthState.Warn); continue; }
                        max = Math.Max(max, value);
                        var thresholds = rule.Thresholds(probe, settings);
                        Include(Threshold(value, thresholds.Warn, thresholds.Fail, inclusive: probe.Metric == HealthMetric.Blocked));
                    }
                    return Cell(state, "max " + max.ToString("0.##", CultureInfo.InvariantCulture) + (probe.Metric == HealthMetric.Percent ? "% used" : probe.Metric == HealthMetric.DumpAge ? " hours" : " blocked") + (unknown == 0 ? "" : $" · {unknown} unavailable"));
                case HealthMetric.IqSpace:
                    bool main = false, temp = false;
                    foreach (var row in rows)
                    {
                        string text = string.Join(" ", row.ItemArray.Select(ResultFormats.Cell));
                        bool isMain = text.Contains("Main IQ Blocks Used", StringComparison.OrdinalIgnoreCase);
                        bool isTemp = text.Contains("Temporary IQ Blocks Used", StringComparison.OrdinalIgnoreCase) && !text.Contains("Shared Temporary", StringComparison.OrdinalIgnoreCase);
                        if (!isMain && !isTemp) continue;
                        var match = Regex.Match(text, @"([0-9]+(?:[.,][0-9]+)?)\s*%", RegexOptions.CultureInvariant);
                        if (!match.Success) { Include(HealthState.Warn); continue; }
                        double used = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                        if (isMain) main = true; if (isTemp) temp = true;
                        var thresholds = rule.Thresholds(probe, settings);
                        Include(used > 100 ? HealthState.Warn : Threshold(used, thresholds.Warn, thresholds.Fail));
                    }
                    if (!main || !temp) Include(HealthState.Warn);
                    return Cell(state, main && temp ? "Main and temporary usage (see details)" : "Main / temporary usage missing or unrecognized");
                case HealthMetric.RsHealth:
                    foreach (var row in rows)
                    {
                        string status = CatalogData.Text(row, "Status").Trim();
                        Include(status.Equals("HEALTHY", StringComparison.OrdinalIgnoreCase) ? HealthState.Ok : status.Equals("SUSPECT", StringComparison.OrdinalIgnoreCase) ? HealthState.Fail : HealthState.Warn);
                        if (CatalogData.TryValue(row, "Mode", out var mode) && !ResultFormats.Cell(mode).Trim().Equals("NORMAL", StringComparison.OrdinalIgnoreCase)) Include(HealthState.Warn);
                        if (CatalogData.TryValue(row, "Loss Status", out var loss) && !ResultFormats.Cell(loss).Trim().Equals("NO LOSS", StringComparison.OrdinalIgnoreCase)) Include(ResultFormats.Cell(loss).Trim().Equals("SUSPECT", StringComparison.OrdinalIgnoreCase) ? HealthState.Fail : HealthState.Warn);
                    }
                    return Cell(state, "See RS status and loss status");
                default: return Cell(HealthState.Warn, "Unknown metric");
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        { return Cell(state == HealthState.Fail ? HealthState.Fail : HealthState.Warn, "Unrecognized result: " + ex.Message); }
    }

    public static string Details(QueryOutput output)
    {
        var text = new StringBuilder();
        foreach (var table in output.Tables) text.AppendLine(ResultFormats.Markdown(table));
        foreach (var message in output.Messages) text.AppendLine(message.Source + ": " + message.Text);
        if (output.OmittedMessages > 0) text.AppendLine(output.OmittedMessages + " messages omitted");
        if (output.Error != null) text.AppendLine("ERROR: " + output.Error);
        return text.ToString();
    }

    public static Task<IReadOnlyList<HealthServer>> ExecuteAsync(IReadOnlyList<HealthTarget> targets, HealthSettings settings, CancellationToken cancellation = default) =>
        ExecuteAsync(targets, settings, new HealthRuleSet(), cancellation);

    public static async Task<IReadOnlyList<HealthServer>> ExecuteAsync(IReadOnlyList<HealthTarget> targets, HealthSettings settings, HealthRuleSet rules, CancellationToken cancellation = default)
    {
        settings.Validate(); rules.Validate(settings);
        var workers = targets.Select(t => new ProbeEngine(t, settings, rules)).ToArray();
        var outcomes = await MultiRun.ExecuteAsync(workers.Select(w => new RunTarget(w.Target.Target.Label, w)).ToArray(), "health", cancellation);
        return workers.Select((worker, index) => new HealthServer(worker.Target.Target.Label, worker.Target.Platform,
            Probes(worker.Target.Platform).Select(p => worker.Cells.FirstOrDefault(c => c.Probe.Id == p.Id) ??
                rules.RuleFor(p, worker.Target.Production).Apply(new(p, HealthState.Fail, outcomes[index].Output.Error ?? "Probe did not complete", ""))).ToArray())).ToArray();
    }

    // Cancellation is cooperative, as in MultiRun. We await the driver before releasing session ownership.
    private sealed class ProbeEngine(HealthTarget target, HealthSettings settings, HealthRuleSet rules) : IQueryEngine
    {
        public HealthTarget Target { get; } = target;
        public List<HealthCell> Cells { get; } = [];
        public string Name => Target.Target.Label;
        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(settings.ServerTimeoutSeconds));
            foreach (var probe in Probes(Target.Platform))
            {
                var rule = rules.RuleFor(probe, Target.Production);
                if (!rule.Enabled) { Cells.Add(rule.Apply(new(probe, HealthState.Ok, "Probe not executed", "SQL was not executed."))); continue; }
                if (deadline.IsCancellationRequested) { Cells.Add(rule.Apply(new(probe, HealthState.Fail, cancellation.IsCancellationRequested ? "Cancelled" : "Server timeout", "Probe not completed within this server's deadline."))); continue; }
                try
                {
                    var output = Target.Target.Engine.Execute(probe.Sql, deadline.Token);
                    Cells.Add(deadline.IsCancellationRequested ? rule.Apply(new(probe, HealthState.Fail, cancellation.IsCancellationRequested ? "Cancelled" : "Server timeout", Details(output))) : Evaluate(probe, output, settings, rule));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Cells.Add(rule.Apply(new(probe, HealthState.Fail, deadline.IsCancellationRequested ? "Cancelled / server timeout" : ex.Message, ex.Message))); }
            }
            return new QueryOutput();
        }
    }

    public static DataTable Matrix(IReadOnlyList<HealthServer> servers)
    {
        var table = new DataTable(); table.Columns.Add("Server"); table.Columns.Add("Platform"); table.Columns.Add("Overall");
        var probes = servers.SelectMany(s => s.Cells).Select(c => c.Probe).DistinctBy(p => p.Id).ToArray();
        foreach (var probe in probes) { var column = table.Columns.Add("probe_" + probe.Id); column.Caption = probe.Title; column.ExtendedProperties["SourceName"] = probe.Title; }
        foreach (var server in servers) table.Rows.Add(new object[] { server.Label, server.Platform.Tag(), server.State.ToString().ToUpperInvariant() }.Concat(probes.Select(p => (object?)server.Cells.FirstOrDefault(c => c.Probe.Id == p.Id)?.Caption ?? "—")).ToArray());
        return table;
    }
}
