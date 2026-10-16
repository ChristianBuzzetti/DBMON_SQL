namespace AseIsql;

// The kind of SAP server a session talks to. The user chooses it at login; it is never inferred from an alias.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ServerPlatform>))]
public enum ServerPlatform { Ase, Iq, Rs }

public static class Platforms
{
    public static IReadOnlyList<ServerPlatform> All { get; } = [ServerPlatform.Ase, ServerPlatform.Iq, ServerPlatform.Rs];
    // Short tag used in session names, recipes and the status bar.
    public static string Tag(this ServerPlatform platform) => platform switch { ServerPlatform.Iq => "IQ", ServerPlatform.Rs => "RS", _ => "ASE" };
    public static string Title(this ServerPlatform platform) => platform switch { ServerPlatform.Iq => "SAP IQ", ServerPlatform.Rs => "Replication Server", _ => "SAP ASE" };
    public static bool TryParseTag(string? tag, out ServerPlatform platform)
    {
        platform = All.FirstOrDefault(p => p.Tag().Equals(tag, StringComparison.OrdinalIgnoreCase));
        return All.Any(p => p.Tag().Equals(tag, StringComparison.OrdinalIgnoreCase));
    }
}

// Database is required for ASE, optional for IQ (DatabaseName) and unused by Replication Server, which has none.
// IqServerName is the IQ engine name, optional when one server listens on the host and port.
public sealed record ConnectionProfile(string Server, int Port, string Database, string User, bool UseTls, string TrustedFile, int LoginTimeout = 15, int QueryTimeout = 60, int MaxRows = 10000, string Charset = "ServerDefault", string ClientHostname = "", bool EncryptPassword = true, string ServerAlias = "", string SqlIniPath = "", string Language = "us_english", ServerPlatform Platform = ServerPlatform.Ase, string IqServerName = "")
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveCharset => Charset.Length == 0 ? "ServerDefault" : Charset;
    public void Validate()
    {
        if (TrustedFile == null || Charset == null || ClientHostname == null || ServerAlias == null || SqlIniPath == null || Database == null || IqServerName == null || string.IsNullOrWhiteSpace(Language)) throw new ArgumentException("The profile contains null fields or an empty language.");
        if (!Enum.IsDefined(Platform)) throw new ArgumentException("Unknown server type.");
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(User)) throw new ArgumentException("Enter a server and username.");
        if (Platform == ServerPlatform.Ase && string.IsNullOrWhiteSpace(Database)) throw new ArgumentException("Enter a database for SAP ASE.");
        if (Platform == ServerPlatform.Rs && Database.Length != 0) throw new ArgumentException("Replication Server has no databases; leave Database empty.");
        if (Platform != ServerPlatform.Iq && IqServerName.Length != 0) throw new ArgumentException("An IQ server name applies only to SAP IQ.");
        if (Port is < 1 or > 65535) throw new ArgumentException("Port: 1 to 65535.");
        if (LoginTimeout is < 1 or > 120 || QueryTimeout is < 1 or > 3600) throw new ArgumentException("Login timeout: 1–120 s. Query timeout: 1–3600 s.");
        if (MaxRows is < 1 or > 1000000) throw new ArgumentException("Row limit: 1–1,000,000.");
        if (UseTls && TrustedFile.Length != 0 && !File.Exists(TrustedFile)) throw new ArgumentException("The trusted certificate file does not exist.");
    }
}

public sealed record QueryMessage(int Number, string Source, string Text, string? RawText = null);
