using System.Data;
using System.Globalization;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Document;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void AddDdlAction(ItemsControl menu, CatalogObject item, SessionWorkspace owner)
    {
        if (item.Platform != ServerPlatform.Ase || item.Type is not ("U" or "V" or "P" or "TR")) return;
        var source = owner.Engine;
        var action = new MenuItem { Header = "Generate DDL" };
        action.Click += (_, _) => RunCatalogAction(current => ReferenceEquals(current, owner) && ReferenceEquals(owner.Engine, source)
            ? GenerateObjectDdl(owner, item) : Task.CompletedTask, "Generate DDL · " + item.Name);
        menu.Items.Add(action);
    }
    private async Task<DataTable[]> ReadDdlMetadata(SessionWorkspace owner, string sql, int count)
    {
        if (owner.Options.NoExec) throw new InvalidOperationException("Disable NOEXEC before generating DDL.");
        // Do not start an implicit catalog transaction or read through the user's uncommitted schema changes.
        var output = await ReadCatalog(owner, "if @@tranchained = 0 and @@trancount = 0 begin\n" + sql + ";\nend", "Generate DDL");
        if (output == null || output.Error != null || output.OmittedMessages != 0 || output.Tables.Count != count)
            throw new FormatException("DDL metadata is unavailable or partial. Check transaction mode, catalog permissions and row limits; no script was generated.");
        return output.Tables.Select(NamedCatalogTable).ToArray();
    }
    // ResultReader uses column_N as its stable binding name; SQL aliases live in Caption/SourceName.
    // These internal metadata queries have unique aliases, so name a copy for the UI's direct lookups.
    private static DataTable NamedCatalogTable(DataTable source)
    {
        var names = source.Columns.Cast<DataColumn>().Select(ResultColumns.Name).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new FormatException("The catalog returned missing or duplicate column headings.");
        var table = source.Copy();
        for (int index = 0; index < names.Length; index++) table.Columns[index].ColumnName = "__metadata_" + index.ToString(CultureInfo.InvariantCulture);
        for (int index = 0; index < names.Length; index++) { table.Columns[index].ColumnName = names[index]; table.Columns[index].Caption = names[index]; }
        return table;
    }
    private async Task GenerateObjectDdl(SessionWorkspace owner, CatalogObject item)
    {
        if (item.Platform != ServerPlatform.Ase || PlatformOf(owner) != ServerPlatform.Ase) return;
        string script;
        string objectOwner = item.Owner ?? throw new FormatException("Missing owner.");
        if (item.Type == "U") {
            var tables = await ReadDdlMetadata(owner, CatalogSql.AseTableDdl(item.Database, objectOwner, item.Name, item.Id), 6);
            await ResolveDdlReferenceOwners(owner, tables[3]);
            script = DdlGenerator.Table(item.Database, objectOwner, item.Name, new(tables[0], tables[1], tables[2], tables[3], tables[4], tables[5]));
        } else if (item.Type is "V" or "P" or "TR") {
            string prefix = ResultFormats.Identifier(item.Database) + "..";
            var tables = await ReadDdlMetadata(owner, CatalogSql.ObjectText(prefix, item.Id) +
                ";\nselect count(*) as hidden_fragments from " + prefix + "syscomments where id = " + item.Id.ToString(CultureInfo.InvariantCulture) + " and (status & 1) != 0", 2);
            if (tables[1].Rows.Count != 1 || Convert.ToInt32(tables[1].Rows[0][0], CultureInfo.InvariantCulture) != 0)
                throw new FormatException("The stored definition is protected; no DDL was generated.");
            script = DdlGenerator.Code(item.Database, tables[0]);
        } else return;
        OpenDdlEditor(owner, item.Name, script);
    }
    private async Task GenerateAseEntryDdl(SessionWorkspace owner, AseExplorerEntry entry)
    {
        if (PlatformOf(owner) != ServerPlatform.Ase) return;
        var tables = await ReadDdlMetadata(owner, AseEntryDetailsSql(entry), 1);
        string script = entry.Kind == "UDT" ? DdlGenerator.UserType(entry.Database, tables[0]) :
            DdlGenerator.Index(entry.Database, entry.Owner ?? throw new FormatException("Missing index owner."), entry.Parent ?? throw new FormatException("Missing table name."), tables[0]);
        OpenDdlEditor(owner, entry.Name, script);
    }
    private async Task ResolveDdlReferenceOwners(SessionWorkspace owner, DataTable keys)
    {
        if (!keys.Columns.Contains("ref_owner_id") || !keys.Columns.Contains("ref_database")) throw new FormatException("Missing referenced owner metadata.");
        keys.Columns.Add("ref_owner", typeof(string));
        foreach (var database in keys.Rows.Cast<DataRow>().GroupBy(row => Convert.ToString(row["ref_database"], CultureInfo.InvariantCulture) ?? "")) {
            if (database.Key.Length == 0) throw new FormatException("Missing referenced database.");
            var ids = database.Select(row => row["ref_owner_id"] is DBNull ? throw new FormatException("Referenced owner is not visible.") : Convert.ToInt32(row["ref_owner_id"], CultureInfo.InvariantCulture)).Distinct().ToArray();
            var tables = await ReadDdlMetadata(owner, CatalogSql.AseUsers(database.Key, ids), 1);
            var users = tables[0].Rows.Cast<DataRow>().ToDictionary(row => Convert.ToInt32(row["uid"], CultureInfo.InvariantCulture), row => Convert.ToString(row["name"], CultureInfo.InvariantCulture));
            foreach (var row in database) {
                if (!users.TryGetValue(Convert.ToInt32(row["ref_owner_id"], CultureInfo.InvariantCulture), out var name) || string.IsNullOrWhiteSpace(name))
                    throw new FormatException("A referenced owner is not visible; no incomplete DDL was generated.");
                row["ref_owner"] = name;
            }
        }
    }
    private void OpenDdlEditor(SessionWorkspace source, string name, string sql)
    {
        // A new offline workspace gives the script a real editor, undo/save/close protection, and no shared connection.
        var editor = new SessionWorkspace { Document = new TextDocument(sql), EditorLabel = "DDL · " + name };
        sessions.Add(editor); AddSessionHeader(editor);
        if (IsOnScreen(source)) SelectSession(editor);
        SetStatus(source, "DDL generated · " + name + " · opened in a new editor tab");
        SetStatus(editor, "Generated DDL · review, edit and save; connect explicitly to run");
    }
}
