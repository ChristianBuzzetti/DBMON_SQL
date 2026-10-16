using System.Globalization;
using System.IO;
using System.Text;

namespace AseIsql.Desktop;

internal static class SqlIniFiles
{
    internal static string? Discover()
    {
        string? root = Environment.GetEnvironmentVariable("SYBASE");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(root)) candidates.Add(Path.Combine(root, "ini", "sql.ini"));
        candidates.Add(@"C:\sybase\ini\sql.ini");
        return candidates.FirstOrDefault(File.Exists);
    }
    internal static SqlIniCatalog Load(string path)
    {
        const int maxBytes = 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var bytes = new MemoryStream(); byte[] chunk = new byte[8192]; int count;
        while ((count = stream.Read(chunk)) > 0)
        { if (bytes.Length + count > maxBytes) throw new IOException("sql.ini exceeds the 1 MiB read limit."); bytes.Write(chunk, 0, count); }
        byte[] data = bytes.ToArray(); string text;
        try { text = new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(data); }
        return SqlIniParser.Parse(text, Path.GetFullPath(path));
    }
}
