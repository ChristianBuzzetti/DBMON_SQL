using System.Globalization;

namespace AseIsql;

// Menu headers with WPF access keys ('_' before the mnemonic letter). Kept in Core so the checks can prove they never collide.
public static class MenuHeaders
{
    public const string SessionConnect = "_Connect…", SessionDisconnect = "D_isconnect", SessionDuplicate = "_Duplicate session", SessionClose = "C_lose session";
    public const string MonitorCopy = "_Copy rows", MonitorDuplicate = "_Duplicate session", MonitorKill = "Kill spid…"; // Kill deliberately has no access key.

    public static IReadOnlyList<string> SessionMenu { get; } = [SessionConnect, SessionDisconnect, SessionDuplicate, SessionClose];
    public static IReadOnlyList<string> MonitorMenu { get; } = [MonitorCopy, MonitorDuplicate, MonitorKill];

    // The character after the first single '_'; "__" is a literal underscore.
    public static char? AccessKey(string header)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != '_') continue;
            if (i + 1 >= header.Length) return null;
            if (header[i + 1] == '_') { i++; continue; }
            return char.ToUpperInvariant(header[i + 1]);
        }
        return null;
    }

    public static bool UniqueAccessKeys(IEnumerable<string> headers)
    {
        var seen = new HashSet<char>();
        foreach (var header in headers) if (AccessKey(header) is char key && !seen.Add(key)) return false;
        return true;
    }
}
