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
public sealed record ConnectionProfile(string Server, int Port, string Database, string User, bool UseTls, string TrustedFile, int LoginTimeout = 15, int QueryTimeout = 60, int MaxRows = 10000, string Charset = "ServerDefault", string ClientHostname = "", bool EncryptPassword = true, string ServerAlias = "", string SqlIniPath = "", string Language = "us_english", ServerPlatform Platform = ServerPlatform.Ase, string IqServerName = "", string IqIdentityFile = "", string IqCertificateName = "", bool Production = false)
{
    // QueryTimeout 0 means no timeout, as in isql: long maintenance (dbcc, reorg, dump) runs until it ends or Esc cancels it.
    public const int MaxQueryTimeout = 3600;
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveCharset => Charset.Length == 0 ? "ServerDefault" : Charset;
    // Production marks the profile as PROD: its session shows a red PROD band and SQL that changes state asks first.
    // Profiles saved before 0.4.0 have no such field and load as not PROD.
    public void Validate()
    {
        ValidateFields();
        if (FileProblem() is { } problem) throw new ArgumentException(problem);
    }

    // The first referenced file that is missing, as a sentence for the login window; null when every file exists.
    public string? FileProblem()
    {
        if (!string.IsNullOrEmpty(IqIdentityFile) && !File.Exists(IqIdentityFile)) return "The IQ client identity file does not exist: " + IqIdentityFile;
        if (UseTls && !string.IsNullOrEmpty(TrustedFile) && !File.Exists(TrustedFile)) return "The trusted certificate file does not exist: " + TrustedFile;
        return null;
    }

    // Every rule except the files on disk. A saved profile whose CA or identity file moved stays readable, and is kept
    // when the store is saved again; the login still runs Validate before anything reaches the driver.
    public void ValidateFields()
    {
        if (TrustedFile == null || Charset == null || ClientHostname == null || ServerAlias == null || SqlIniPath == null || Database == null || IqServerName == null || IqIdentityFile == null || IqCertificateName == null || string.IsNullOrWhiteSpace(Language)) throw new ArgumentException("The profile contains null fields or an empty language.");
        if (!Enum.IsDefined(Platform)) throw new ArgumentException("Unknown server type.");
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(User)) throw new ArgumentException("Enter a server and username.");
        if (Platform == ServerPlatform.Ase && string.IsNullOrWhiteSpace(Database)) throw new ArgumentException("Enter a database for SAP ASE.");
        if (Platform == ServerPlatform.Rs && Database.Length != 0) throw new ArgumentException("Replication Server has no databases; leave Database empty.");
        if (Platform != ServerPlatform.Iq && IqServerName.Length != 0) throw new ArgumentException("An IQ server name applies only to SAP IQ.");
        if (IqCertificateName.Length != 0 && (Platform != ServerPlatform.Iq || !UseTls)) throw new ArgumentException("An expected certificate name requires SAP IQ with Encrypt data (TLS) enabled.");
        if (IqIdentityFile.Length != 0 && (Platform != ServerPlatform.Iq || !UseTls)) throw new ArgumentException("A client identity file requires SAP IQ with Encrypt data (TLS) enabled.");
        if (Port is < 1 or > 65535) throw new ArgumentException("Port: 1 to 65535.");
        if (LoginTimeout is < 1 or > 120 || QueryTimeout is < 0 or > MaxQueryTimeout) throw new ArgumentException("Login timeout: 1–120 s. Query timeout: 0 (no timeout) to 3600 s.");
        if (MaxRows is < 1 or > 1000000) throw new ArgumentException("Row limit: 1–1,000,000.");
    }
}

public sealed record QueryMessage(int Number, string Source, string Text, string? RawText = null);
