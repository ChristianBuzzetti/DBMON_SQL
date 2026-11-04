using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private sealed record AseExplorerEntry(string Database, string Kind, int Id, string? Owner, string Name, string? Parent);
    private static readonly (string Kind, string Label)[] AseExtraGroups = [
        ("IX", "Indexes"), ("C", "Constraints / foreign keys"), ("UDT", "User-defined types"),
        ("USER", "Users"), ("GROUP", "Groups"), ("ROLE", "Roles"), ("SEG", "Segments")];

    private void AddAseCatalogBranches(TreeViewItem root, SessionWorkspace owner, string database)
    {
        foreach (var (kind, label) in AseExtraGroups)
            root.Items.Add(AseEntryGroup(owner, database, kind, label));
        if (database.Equals("master", StringComparison.OrdinalIgnoreCase))
            root.Items.Add(AseEntryGroup(owner, database, "LOGIN", "Logins"));
    }
    private TreeViewItem LazyCatalogNode(SessionWorkspace owner, string label, Func<TreeViewItem, Task<bool>> load)
    {
        var node = new TreeViewItem { Header = label }; node.Items.Add(new TreeViewItem { Header = "Expand to load" });
        var source = owner.Engine;
        bool loaded = false, loading = false;
        node.Expanded += (_, args) => {
            if (args.OriginalSource != node || loaded || loading || owner.Repeat != null || activeSession != owner || !ReferenceEquals(owner.Engine, source) || !sessions.Contains(owner)) return;
            loading = true;
            RunCatalogAction(async current => {
                try { if (ReferenceEquals(current, owner)) loaded = await load(node); }
                finally { loading = false; }
            }, label);
        };
        return node;
    }
    private TreeViewItem AseEntryGroup(SessionWorkspace owner, string database, string kind, string label) =>
        LazyCatalogNode(owner, label, node => LoadAseEntries(owner, database, kind, label, node, null));

    private static string AseEntriesSql(string database, string kind, string filter, AseExplorerEntry? after)
    {
        string db = ResultFormats.Identifier(database) + "..";
        const string noOwner = "convert(varchar(255), null) as owner";
        const string noParent = "convert(varchar(255), null) as parent";
        string source = kind switch {
            "IX" => $"select i.id, u.name as owner, i.name, o.name as parent from {db}sysindexes i join {db}sysobjects o on o.id = i.id join {db}sysusers u on u.uid = o.uid where i.indid between 1 and 254 and o.type = 'U'",
            "C" => $"select distinct o.id, u.name as owner, o.name, t.name as parent from {db}sysconstraints c join {db}sysobjects o on o.id = c.constrid join {db}sysobjects t on t.id = c.tableid join {db}sysusers u on u.uid = t.uid where o.type in ('R','RI') union select i.id, u.name, i.name, t.name from {db}sysindexes i join {db}sysobjects t on t.id = i.id join {db}sysusers u on u.uid = t.uid where i.indid between 1 and 254 and (i.status2 & 2) != 0",
            "UDT" => $"select t.usertype as id, u.name as owner, t.name, {noParent} from {db}systypes t join {db}sysusers u on u.uid = t.uid where t.usertype > 100",
            "USER" => $"select u.uid as id, {noOwner}, u.name, g.name as parent from {db}sysusers u left join {db}sysusers g on g.uid = u.gid where u.suid != -2",
            "GROUP" => $"select u.uid as id, {noOwner}, u.name, {noParent} from {db}sysusers u where u.uid = u.gid and not exists (select 1 from {db}sysroles r where r.lrid = u.uid)",
            "ROLE" => $"select u.uid as id, {noOwner}, u.name, {noParent} from {db}sysusers u join {db}sysroles r on r.lrid = u.uid",
            "SEG" => $"select s.segment as id, {noOwner}, s.name, {noParent} from {db}syssegments s",
            "LOGIN" => $"select l.suid as id, {noOwner}, l.name, l.dbname as parent from master..syslogins l",
            _ => throw new ArgumentException("Unknown ASE catalog branch.")
        };
        string where = "1 = 1";
        if (filter.Length > 0) where += " and x.name like " + ResultFormats.Literal("%" + LikeEscape(filter) + "%") + " escape '\\'";
        if (after != null) where += " and (x.name > " + ResultFormats.Literal(after.Name) + " or (x.name = " + ResultFormats.Literal(after.Name) + " and x.id > " + after.Id.ToString(CultureInfo.InvariantCulture) + "))";
        return $"select top {CatalogPage + 1} x.id, x.owner, x.name, x.parent from ({source}) x where {where} order by x.name, x.id";
    }
    private async Task<bool> LoadAseEntries(SessionWorkspace owner, string database, string kind, string label, TreeViewItem group, AseExplorerEntry? after)
    {
        var output = await ReadCatalog(owner, AseEntriesSql(database, kind, owner.Filter, after));
        if (output == null || output.Tables.Count == 0) return false;
        var table = output.Tables[0];
        if (table.Columns.Count != 4) throw new FormatException("Unexpected ASE catalog branch columns.");
        string? Value(DataRow row, int ordinal) => row[ordinal] is DBNull ? null : Convert.ToString(row[ordinal], CultureInfo.InvariantCulture);
        var entries = table.Rows.Cast<DataRow>().Select(row => new AseExplorerEntry(database, kind,
            Convert.ToInt32(row[0], CultureInfo.InvariantCulture), Value(row, 1), Value(row, 2) ?? throw new FormatException("Missing catalog name."), Value(row, 3))).ToArray();
        if (after == null) group.Items.Clear();
        else foreach (var old in group.Items.OfType<TreeViewItem>().Where(n => n.Tag is "more").ToArray()) group.Items.Remove(old);
        foreach (var entry in entries.Take(CatalogPage)) group.Items.Add(AseEntryNode(owner, entry));
        int count = group.Items.OfType<TreeViewItem>().Count(n => n.Tag is AseExplorerEntry);
        group.Header = label + " · " + count + (entries.Length > CatalogPage ? "+" : "") + (owner.Filter.Length == 0 ? "" : " · \"" + owner.Filter + "\"");
        if (count == 0) group.Items.Add(new TreeViewItem { Header = "No visible matching entries" });
        if (entries.Length > CatalogPage) {
            var more = new Button { Content = "Show 100 more…" }; var last = entries[CatalogPage - 1];
            more.Click += (_, _) => RunCatalogAction(async current => {
                if (!ReferenceEquals(current, owner)) return;
                more.IsEnabled = false;
                try { await LoadAseEntries(owner, database, kind, label, group, last); }
                finally { more.IsEnabled = true; }
            }, label);
            group.Items.Add(new TreeViewItem { Header = more, Tag = "more" });
        }
        if (output.Error != null) group.Items.Add(new TreeViewItem { Header = "Partial list · " + output.Error });
        return output.Error == null;
    }
    private TreeViewItem AseEntryNode(SessionWorkspace owner, AseExplorerEntry entry, bool lazy = true)
    {
        string label = (entry.Owner == null ? "" : entry.Owner + ".") + (entry.Kind is "IX" or "C" ? entry.Parent + "." : "") + entry.Name;
        var node = lazy ? LazyCatalogNode(owner, label, target => LoadCatalogDetails(owner, target, AseEntryDetailsSql(entry))) : new TreeViewItem { Header = label };
        var source = owner.Engine;
        node.Tag = entry;
        if (entry.Kind is "IX" or "UDT") {
            var menu = new ContextMenu(); var ddl = new MenuItem { Header = "Generate DDL" };
            ddl.Click += (_, _) => RunCatalogAction(current => ReferenceEquals(current, owner) && ReferenceEquals(owner.Engine, source) ? GenerateAseEntryDdl(owner, entry) : Task.CompletedTask, "Generate DDL");
            menu.Items.Add(ddl); node.ContextMenu = menu;
        }
        return node;
    }
    private static string AseEntryDetailsSql(AseExplorerEntry entry)
    {
        string db = ResultFormats.Identifier(entry.Database) + "..", id = entry.Id.ToString(CultureInfo.InvariantCulture);
        return entry.Kind switch {
            "IX" => CatalogSql.AseIndexKeys(entry.Database, entry.Owner ?? throw new FormatException("Missing index owner."), entry.Parent ?? throw new FormatException("Missing index table."), entry.Id, entry.Name),
            "UDT" => CatalogSql.AseUserType(entry.Database, entry.Id),
            "C" => $"select o.name, o.type, c.colid, c.status from {db}sysconstraints c join {db}sysobjects o on o.id = c.constrid where o.id = {id} and o.name = {ResultFormats.Literal(entry.Name)};\n" +
                "exec " + db + "sp_helpconstraint " + ResultFormats.Literal(ResultFormats.Identifier(entry.Owner ?? "dbo") + "." + ResultFormats.Identifier(entry.Parent ?? entry.Name)),
            "ROLE" => $"select u.name, u.uid as local_role_id, r.id as server_role_id from {db}sysusers u join {db}sysroles r on r.lrid = u.uid where u.uid = {id};\n" +
                $"select l.name as login_member from {db}sysroles r join master..sysloginroles m on m.srid = r.id join master..syslogins l on l.suid = m.suid where r.lrid = {id}",
            "USER" or "GROUP" => $"select u.name, u.uid, u.suid, u.gid, g.name as group_name from {db}sysusers u left join {db}sysusers g on g.uid = u.gid where u.uid = {id};\n" +
                $"select name as member_name, uid from {db}sysusers where gid = {id} and uid != {id}",
            "SEG" => $"select name, segment, status from {db}syssegments where segment = {id};\nexec {db}sp_helpsegment {ResultFormats.Literal(entry.Name)}",
            "LOGIN" => $"select name, suid, dbname as default_database, language, status, crdate from master..syslogins where suid = {id}",
            _ => throw new ArgumentException("Unknown ASE catalog entry.")
        };
    }
    private async Task<bool> LoadCatalogDetails(SessionWorkspace owner, TreeViewItem node, string sql)
    {
        var output = await ReadCatalog(owner, sql);
        if (output == null) return false;
        node.Items.Clear();
        foreach (var table in output.Tables) {
            foreach (DataRow row in table.Rows) {
                var detail = new TreeViewItem { Header = string.Join(" · ", table.Columns.Cast<DataColumn>().Select(column =>
                    column.Caption + ": " + (row[column] is DBNull ? "(unknown)" : Convert.ToString(row[column], CultureInfo.InvariantCulture)))) };
                node.Items.Add(detail);
            }
        }
        if (node.Items.Count == 0) node.Items.Add(new TreeViewItem { Header = "No visible details" });
        if (output.Error != null) node.Items.Add(new TreeViewItem { Header = "Partial / unavailable · " + output.Error });
        return output.Error == null;
    }
    private void AddAseTableChildren(TreeViewItem node, SessionWorkspace owner, CatalogObject table)
    {
        string db = ResultFormats.Identifier(table.Database) + "..", id = table.Id.ToString(CultureInfo.InvariantCulture);
        node.Items.Add(LazyCatalogNode(owner, "Rows, space, locks and partitions", target => LoadCatalogDetails(owner, target, CatalogSql.AseTableDetails(table.Database, table.Id))));
        node.Items.Add(LazyCatalogNode(owner, "Indexes and keys", target => LoadAseTableIndexes(owner, table, target)));
        node.Items.Add(LazyCatalogNode(owner, "Triggers", async target => {
            var output = await ReadCatalog(owner, $"select distinct o.id, u.name as owner, o.name, o.type from {db}sysobjects o join {db}sysusers u on u.uid = o.uid left join {db}sysconstraints c on c.constrid = o.id where o.type = 'TR' and (c.tableid = {id} or o.id in (select instrig from {db}sysobjects where id = {id}) or o.id in (select updtrig from {db}sysobjects where id = {id}) or o.id in (select deltrig from {db}sysobjects where id = {id})) order by o.name, o.id");
            if (output == null || output.Tables.Count == 0) return false;
            target.Items.Clear(); foreach (var trigger in ParseCatalogObjects(output.Tables[0], table.Database)) target.Items.Add(ObjectNode(trigger,owner));
            if (target.Items.Count == 0) target.Items.Add(new TreeViewItem { Header = "No visible triggers" });
            if (output.Error != null) target.Items.Add(new TreeViewItem { Header = "Partial · " + output.Error });
            return output.Error == null;
        }));
        node.Items.Add(LazyCatalogNode(owner, "Constraints", target => LoadCatalogDetails(owner, target, "exec " + db + "sp_helpconstraint " + table.Target.ProcedureArgument)));
        node.Items.Add(LazyCatalogNode(owner, "Foreign-key columns", target => LoadCatalogDetails(owner, target, CatalogSql.AseForeignKeys(table.Database, table.Id))));
    }
    private async Task<bool> LoadAseTableIndexes(SessionWorkspace owner, CatalogObject table, TreeViewItem node)
    {
        var output = await ReadCatalog(owner, CatalogSql.AseIndexKeys(table.Database, table.Owner ?? throw new FormatException("Missing table owner."), table.Name, table.Id));
        if (output == null || output.Tables.Count == 0) return false;
        node.Items.Clear();
        foreach (var group in NamedCatalogTable(output.Tables[0]).Rows.Cast<DataRow>().GroupBy(row => Convert.ToString(row["index_name"], CultureInfo.InvariantCulture) ?? "")) {
            var entry = new AseExplorerEntry(table.Database, "IX", table.Id, table.Owner, group.Key, table.Name);
            var indexNode = AseEntryNode(owner, entry, lazy:false);
            foreach (var key in group) indexNode.Items.Add(new TreeViewItem { Header = Convert.ToString(key["key_ordinal"], CultureInfo.InvariantCulture) + ". " + key["column_name"] + " " + key["sort_order"] });
            // Already have the keys; don't issue a second request on expansion.
            var first = group.First(); indexNode.ToolTip = "Clustered: " + first["clustered"] + " · Unique: " + first["is_unique"] + " · Primary key: " + first["primary_key"];
            node.Items.Add(indexNode);
        }
        if (node.Items.Count == 0) node.Items.Add(new TreeViewItem { Header = "No visible indexes" });
        if (output.Error != null) node.Items.Add(new TreeViewItem { Header = "Partial · " + output.Error });
        return output.Error == null;
    }
}
