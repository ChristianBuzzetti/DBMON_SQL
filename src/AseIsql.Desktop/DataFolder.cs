using System.IO;

namespace AseIsql.Desktop;

// LocalAppData\DBMON_SQL holds appearance, saved profiles, My recipes, server groups and the Boberto runtime profile.
// Builds before 16/10/2026 used "ASE ISQL Desktop". The first start of a newer build moves that folder once; when the
// move is blocked (for example an older build still running) it copies what it can and leaves the old folder intact.
public static class DataFolder
{
    public const string Name = "DBMON_SQL", LegacyName = "ASE ISQL Desktop";
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Root => Path.Combine(LocalAppData, Name);
    public static string Legacy => Path.Combine(LocalAppData, LegacyName);
    public static string File(string name) => Path.Combine(Root, name);
    // What the last migration did, for Messages; null when nothing needed to move.
    public static string? MigrationNote { get; private set; }

    public static void MigrateDefault() => MigrationNote = Migrate(Root, Legacy);

    public static string? Migrate(string root, string legacy)
    {
        if (Directory.Exists(root) || !Directory.Exists(legacy)) return null;
        try { Directory.Move(legacy, root); return "Local data moved from " + legacy + " to " + root + "."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            int copied = 0, skipped = 0;
            void Copy(string from, string to)
            {
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from))
                    try { System.IO.File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false); copied++; }
                    catch (Exception fileError) when (fileError is IOException or UnauthorizedAccessException) { skipped++; }
                foreach (string folder in Directory.GetDirectories(from)) Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
            Copy(legacy, root);
            return "Local data copied from " + legacy + " to " + root + " (" + copied + " files" + (skipped > 0 ? ", " + skipped + " in use were skipped" : "") +
                "). The old folder was kept because it is in use: " + ex.Message;
        }
    }
}
