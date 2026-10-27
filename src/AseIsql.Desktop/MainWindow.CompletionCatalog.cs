using System.Data.Odbc;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private CancellationTokenSource? tableCompletionCancellation;
    private Task tableCompletionLoad = Task.CompletedTask;
    private long completionGeneration;

    private void CancelTableCompletion() => tableCompletionCancellation?.Cancel();

    private static bool TryTableCompletionTarget(string sql, int caret, out ObjectTarget? target)
    {
        target = null;
        if (!TryCompletionPrefix(sql, caret, out _, out string prefix, out int tokenStart)) return false;
        string before = sql[..tokenStart].TrimEnd();
        int word = before.Length;
        while (word > 0 && char.IsLetter(before[word - 1])) word--;
        if (before[word..].ToUpperInvariant() is not ("FROM" or "JOIN" or "UPDATE" or "INTO" or "TABLE")) return false;
        target = ParseObjectToken(sql[tokenStart..caret] + (prefix.Length == 0 ? "_" : ""));
        return target != null;
    }

    private async Task LoadTableCompletion(SessionWorkspace owner, IQueryEngine source, TextDocument document,
        int start, string prefix, ObjectTarget target)
    {
        var platform = PlatformOf(owner);
        if (owner.Busy || owner.Options.NoExec || platform == ServerPlatform.Rs ||
            platform == ServerPlatform.Iq && target.Database != null) return;
        using var cancellation = new CancellationTokenSource();
        tableCompletionCancellation = cancellation;
        string sqlText = document.Text;
        int caret = Editor.CaretOffset, run = owner.RunCounter;
        long generation = completionGeneration;
        string? database = (source as ISessionFacts)?.Facts.Database;
        bool Current(bool reading) => !cancellation.IsCancellationRequested && generation == completionGeneration && ReferenceEquals(activeSession, owner) &&
            ReferenceEquals(owner.Engine, source) && ReferenceEquals(Editor.Document, document) &&
            source is not AseIsql.Odbc.OdbcSession { IsOpen: false } &&
            (!reading || document.Text == sqlText && Editor.CaretOffset == caret) && owner.RunCounter == run &&
            string.Equals((source as ISessionFacts)?.Facts.Database, database, StringComparison.Ordinal) && sessions.Contains(owner);
        try
        {
            var output = await ReadEditorMetadata(owner, source, TableCompletionSql(platform, target, prefix), cancellation, preserveCompletion: true);
            if (!Current(true) || output.Error != null || output.OmittedMessages != 0 || output.Tables.Count != 2) return;
            var context = output.Tables[0];
            if (context.Columns.Count != 1 || context.Rows.Count != 1 || context.Rows[0][0] is not string { Length: > 0 } effectiveDatabase) return;
            var rows = ParseCatalogObjects(output.Tables[1], target.Database ?? effectiveDatabase, platform);
            if (rows.Count > CatalogPage + 1 || rows.Any(item => item.Id <= 0 || string.IsNullOrEmpty(item.Owner) ||
                item.Type is not ("U" or "S") || !item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                target.Owner != null && !string.Equals(item.Owner, target.Owner, StringComparison.OrdinalIgnoreCase))) return;
            var entries = rows.Take(CatalogPage).Select(item => {
                bool ambiguous = target.Owner == null && rows.Count(other => other.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) > 1;
                string insertion = (ambiguous ? UseTarget(item.Owner!) + "." : "") + UseTarget(item.Name);
                // For db..name, replacing the second dot as well yields db.owner.name.
                int replaceBack = ambiguous && target.Database != null ? 1 : 0;
                return (ICompletionData)new SqlCompletionItem(item.Name, false, () => Current(false) && !owner.Busy,
                    insertion, item.Database + "." + item.Owner + " · table", replaceBack);
            }).ToArray();
            // The popup's entries retain the same request lease after the read releases its lane.
            ShowEditorCompletion(start, prefix, entries);
        }
        catch (Exception ex) when (ex is OperationCanceledException or OdbcException or InvalidOperationException or
            ArgumentException or FormatException or InvalidCastException or OverflowException)
        { /* Optional metadata never replaces query diagnostics or executes the editor buffer. */ }
        finally
        {
            if (ReferenceEquals(tableCompletionCancellation, cancellation)) tableCompletionCancellation = null;
        }
    }

    private static string TableCompletionSql(ServerPlatform platform, ObjectTarget target, string prefix)
    {
        string filter = ResultFormats.Literal(LikeEscape(prefix) + "%");
        string candidates;
        if (platform == ServerPlatform.Ase)
        {
            string database = target.Database == null ? "" : ResultFormats.Identifier(target.Database) + "..";
            string owner = target.Owner == null ? "" : " and u.name = " + ResultFormats.Literal(target.Owner);
            candidates = $"select top {CatalogPage + 1} o.id, u.name as owner, o.name, o.type from {database}sysobjects o join {database}sysusers u on u.uid = o.uid where o.type in ('U','S') and o.name like {filter} escape '\\'{owner} order by o.name, o.id";
        }
        else
        {
            string owner = target.Owner == null ? "" : " and u.user_name = " + ResultFormats.Literal(target.Owner);
            candidates = $"select top {CatalogPage + 1} t.table_id as id, u.user_name as owner, t.table_name as name, 'U' as type from SYS.SYSTAB t join SYS.SYSUSER u on u.user_id = t.creator where t.table_type_str in ('BASE','GBL TEMP') and t.table_name like {filter} escape '\\'{owner} order by t.table_name, t.table_id";
        }
        return GuardedEditorMetadataSql(candidates);
    }
}
