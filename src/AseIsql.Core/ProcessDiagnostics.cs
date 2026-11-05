using System.Globalization;
using System.Text.RegularExpressions;

namespace AseIsql;

public enum DiagnosticStatus { Ok, Empty, NoPermission, Failed }

public sealed record DiagnosticText(DiagnosticStatus Status, string Text, string Detail);

// DBCC and sp_showplan print diagnostics as messages, including when ASE buffers them until batch end.
public static class ProcessDiagnostics
{
    private static string Spid(int spid) => spid > 0 ? spid.ToString(CultureInfo.InvariantCulture)
        : throw new ArgumentOutOfRangeException(nameof(spid));

    public static string SqlTextSql(int spid) =>
        "dbcc traceon(3604)\ndbcc sqltext(" + Spid(spid) + ")\ndbcc traceoff(3604)";

    public static string ShowplanSql(int spid) =>
        "dbcc traceon(3604)\nexec sp_showplan " + Spid(spid) + ", null, null, null\ndbcc traceoff(3604)";

    private static bool PermissionText(string text) => new[] { "You must have the following role", "sa_role", "sybase_ts_role",
        "permission denied", "No permission" }.Any(part => text.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static DiagnosticText? Failure(IReadOnlyList<QueryMessage> messages, string? error)
    {
        var denied = messages.Where(message => message.Number is 10353 or 10330 or 567 or 10351 || PermissionText(message.Text)).ToArray();
        if (PermissionText(error ?? "") || denied.Length > 0)
            return new(DiagnosticStatus.NoPermission, "", error ?? string.Join("\n", denied.Select(message => message.Text.TrimEnd())));
        return error != null ? new(DiagnosticStatus.Failed, "", error) : null;
    }

    private static IEnumerable<string> Lines(IReadOnlyList<QueryMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Number == 2528) continue;
            foreach (string line in message.Text.TrimEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                if (!line.TrimStart().StartsWith("DBCC execution completed", StringComparison.OrdinalIgnoreCase))
                    yield return line.TrimEnd();
            }
        }
    }

    public static DiagnosticText ParseSqlText(IReadOnlyList<QueryMessage> messages, string? error)
    {
        if (Failure(messages, error) is { } failure) return failure;
        var lines = new List<string>();
        var source = Lines(messages).ToList();
        int sqlStart = source.FindIndex(line => line.TrimStart().StartsWith("SQL Text:", StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < source.Count; i++)
        {
            string line = source[i];
            string trimmed = line.TrimStart();
            if (i == sqlStart)
                lines.Add(trimmed["SQL Text:".Length..].TrimStart());
            else if (trimmed.StartsWith("Executing procedure:", StringComparison.OrdinalIgnoreCase))
                lines.Add("-- " + trimmed);
            else if (trimmed.StartsWith("Subordinate SQL Text:", StringComparison.OrdinalIgnoreCase))
                lines.Add(trimmed["Subordinate SQL Text:".Length..].TrimStart());
            else if (i > sqlStart) lines.Add(line);
        }
        string text = string.Join("\n", lines).Trim('\n');
        return string.IsNullOrWhiteSpace(text) ? new(DiagnosticStatus.Empty, "", "") : new(DiagnosticStatus.Ok, text, "");
    }

    public static DiagnosticText ParseShowplan(IReadOnlyList<QueryMessage> messages, string? error)
    {
        if (Failure(messages, error) is { } failure) return failure;
        string text = string.Join("\n", Lines(messages).Where(line => !Regex.IsMatch(line,
            @"^\s*\(return status\s*=\s*[+-]?\d+\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))).Trim('\n');
        return text.Contains("QUERY PLAN FOR STATEMENT", StringComparison.OrdinalIgnoreCase)
            ? new(DiagnosticStatus.Ok, text, "")
            : new(DiagnosticStatus.Empty, "", string.IsNullOrWhiteSpace(text) ? "" : text);
    }
}
