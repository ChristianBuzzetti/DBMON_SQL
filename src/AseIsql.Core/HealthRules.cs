using System.Text.Json;
using System.Text.Json.Serialization;

namespace AseIsql;

public enum HealthSeverity { Ignore, Warn, Fail }

public sealed record HealthRule
{
    public string ProbeId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public HealthSeverity Severity { get; init; } = HealthSeverity.Fail;
    public double? WarnThreshold { get; init; }
    public double? FailThreshold { get; init; }

    public static bool HasThresholds(HealthProbe probe) => probe.Metric is HealthMetric.Percent or HealthMetric.DumpAge or HealthMetric.Blocked or HealthMetric.IqSpace;

    public (double Warn, double Fail) Thresholds(HealthProbe probe, HealthSettings settings) => probe.Metric switch {
        HealthMetric.Percent or HealthMetric.IqSpace => (WarnThreshold ?? settings.UsageWarnPercent, FailThreshold ?? settings.UsageFailPercent),
        HealthMetric.DumpAge => (WarnThreshold ?? settings.DumpWarnHours, FailThreshold ?? settings.DumpFailHours),
        HealthMetric.Blocked => (WarnThreshold ?? settings.BlockedWarnCount, FailThreshold ?? settings.BlockedFailCount),
        _ => throw new ArgumentException("This probe has no numeric thresholds.") };

    public void Validate(HealthProbe probe, HealthSettings settings)
    {
        if (ProbeId != probe.Id || !Enum.IsDefined(Severity)) throw new ArgumentException("Invalid rule for " + probe.Title + ".");
        if (!HasThresholds(probe))
        {
            if (WarnThreshold != null || FailThreshold != null) throw new ArgumentException(probe.Title + " has no numeric thresholds.");
            return;
        }
        var (warn, fail) = Thresholds(probe, settings);
        bool percent = probe.Metric is HealthMetric.Percent or HealthMetric.IqSpace;
        if (!double.IsFinite(warn) || !double.IsFinite(fail) || warn >= fail ||
            (percent ? warn < 0 || fail > 100 : warn < 1 || fail > int.MaxValue || warn != Math.Truncate(warn) || fail != Math.Truncate(fail)))
            throw new ArgumentException(probe.Title + (percent
                ? ": thresholds must satisfy 0 ≤ warn < fail ≤ 100."
                : ": thresholds must be whole numbers with 1 ≤ warn < fail ≤ 2147483647."));
    }

    // Apply to every outcome, including driver errors, cancellation and server deadlines.
    internal HealthCell Apply(HealthCell cell)
    {
        if (!Enabled || Severity == HealthSeverity.Ignore)
            return cell with { State = HealthState.Ok, Ignored = true, Summary = Enabled ? "ignored by rule" : "disabled by rule",
                Details = cell.Details + "\n\nObserved: " + cell.Caption };
        return Severity == HealthSeverity.Warn && cell.State == HealthState.Fail ? cell with { State = HealthState.Warn } : cell;
    }
}

public sealed record HealthRuleSet
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public List<HealthRule> Production { get; init; } = Defaults(true);
    public List<HealthRule> NonProduction { get; init; } = Defaults(false);

    public static IReadOnlyList<HealthProbe> AllProbes { get; } = new[] { ServerPlatform.Ase, ServerPlatform.Iq, ServerPlatform.Rs }
        .SelectMany(HealthCheck.Probes).DistinctBy(p => p.Id).ToArray();

    private static HealthRule Default(HealthProbe probe, bool production) => new() {
        ProbeId = probe.Id, Severity = probe.Id == "dump" && !production ? HealthSeverity.Ignore : HealthSeverity.Fail };

    private static List<HealthRule> Defaults(bool production) => AllProbes.Select(p => Default(p, production)).ToList();

    public HealthRule RuleFor(HealthProbe probe, bool production) =>
        (production ? Production : NonProduction).FirstOrDefault(r => r.ProbeId == probe.Id) ?? Default(probe, production);

    public void Validate(HealthSettings? settings = null)
    {
        settings ??= new(); settings.Validate();
        if (Version != 1 || Production == null || NonProduction == null) throw new ArgumentException("Invalid health rules version or rule lists.");
        foreach (bool production in new[] { true, false })
        {
            var rules = production ? Production : NonProduction;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.ProbeId) || !ids.Add(rule.ProbeId)) throw new ArgumentException("Empty or duplicate health probe id.");
            }
            // Retain future probe ids in the file; only known probes affect validation/execution.
            foreach (var probe in AllProbes) RuleFor(probe, production).Validate(probe, settings);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true, Converters = { new JsonStringEnumConverter<HealthSeverity>(allowIntegerValues: false) } };

    public static HealthRuleSet Load(string path, out string? warning, HealthSettings? settings = null)
    {
        warning = null;
        try
        {
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 262144) throw new IOException("Health rules exceed 256 KiB.");
            var rules = JsonSerializer.Deserialize<HealthRuleSet>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("Empty health rules.");
            rules.Validate(settings); return rules;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { warning = "Unable to load health rules; defaults are in use: " + ex.Message; return new(); }
    }

    public void Save(string path, HealthSettings? settings = null)
    {
        Validate(settings);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        AtomicFiles.SaveUtf8(fullPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
