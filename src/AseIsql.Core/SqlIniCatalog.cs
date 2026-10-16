using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AseIsql;

public sealed record SqlIniEndpoint(string Protocol, string Host, int? Port, bool UseTls, string RawValue, int Line, string? Error)
{
    public bool IsUsable => Error == null;
    public override string ToString() => IsUsable ? $"{Host}:{Port}" + (UseTls ? " · SSL" : "") : RawValue + " · unsupported";
}
// Queries are client endpoints. Masters are listener entries, offered only as a fallback for IQ and Replication
// Server aliases that list no query entry.
public sealed record SqlIniServer(string Name, int Line, IReadOnlyList<SqlIniEndpoint> Queries, bool Duplicate, IReadOnlyList<SqlIniEndpoint>? MasterEntries = null)
{
    public override string ToString() => Name;
}
public sealed record SqlIniCatalog(string SourcePath, string Fingerprint, IReadOnlyList<SqlIniServer> Servers, IReadOnlyList<string> Diagnostics);

public static class SqlIniParser
{
    public static SqlIniCatalog Parse(string text, string path = "")
    {
        var servers = new List<SqlIniServer>(); var diagnostics = new List<string>();
        string? name = null; int sectionLine = 0, number = 0; var queries = new List<SqlIniEndpoint>(); var masters = new List<SqlIniEndpoint>();
        void Finish() { if (name != null) servers.Add(new(name, sectionLine, queries.ToArray(), false, masters.ToArray())); queries.Clear(); masters.Clear(); }
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) != null)
        {
            number++; string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                Finish(); name = null;
                int close = line.IndexOf(']');
                string tail = close >= 0 ? line[(close + 1)..].Trim() : "";
                if (close < 2 || tail.Length != 0 && tail[0] is not ';' and not '#')
                { diagnostics.Add($"Line {number}: invalid section header."); continue; }
                name = line[1..close]; sectionLine = number; continue;
            }
            int equals = line.IndexOf('=');
            if (name == null || equals < 1) { diagnostics.Add($"Line {number}: entry outside a section or missing '='."); continue; }
            string key = line[..equals].Trim();
            bool master = key.Equals("master", StringComparison.OrdinalIgnoreCase);
            if (!master && !key.Equals("query", StringComparison.OrdinalIgnoreCase)) continue;
            string value = line[(equals + 1)..].Trim(); string[] fields = value.Split(',').Select(p => p.Trim()).ToArray();
            string protocol = fields.ElementAtOrDefault(0) ?? "", host = fields.ElementAtOrDefault(1) ?? "";
            int? port = fields.Length >= 3 && int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed is > 0 and <= 65535 ? parsed : null;
            bool tls = fields.Length == 4 && fields[3].Equals("ssl", StringComparison.OrdinalIgnoreCase);
            string? error = fields.Length is < 3 or > 4 ? "Expected protocol, host, port and optional SSL." :
                !protocol.Equals("TCP", StringComparison.OrdinalIgnoreCase) && !protocol.Equals("NLWNSCK", StringComparison.OrdinalIgnoreCase) ? "This client supports TCP/NLWNSCK." :
                host.Length == 0 || port == null ? "Invalid TCP host or port; a numeric port is required." : fields.Length == 4 && !tls ? "Unknown transport option." : null;
            (master ? masters : queries).Add(new(protocol, host, port, tls, value, number, error));
        }
        Finish();
        var duplicates = servers.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string duplicate in duplicates) diagnostics.Add("Duplicate server name: " + duplicate);
        return new(path, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), servers.Select(s => s with { Duplicate = duplicates.Contains(s.Name) }).ToArray(), diagnostics);
    }
}
