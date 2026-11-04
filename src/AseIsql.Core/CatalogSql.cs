using System.Globalization;

namespace AseIsql;

// Read-only ASE metadata, fully qualified so comparing another database does not change the session's USE context.
public static class CatalogSql
{
    public static string Objects(string database, CompareObjectType type)
    {
        string code = type switch { CompareObjectType.Tables => "U", CompareObjectType.Procedures => "P", CompareObjectType.Views => "V", CompareObjectType.Triggers => "TR", _ => throw new ArgumentException("Unknown object type.") };
        return $"select top {ObjectCompare.MaxObjects + 1} o.id, u.name as owner, o.name from {Db(database)}sysobjects o join {Db(database)}sysusers u on u.uid=o.uid where o.type={ResultFormats.Literal(code)} order by u.name,o.name";
    }
    public static string ObjectText(string database, int objectId) =>
        $"select case when (isnull(c.status,0) & 1)=0 then c.text else null end as text from {Db(database)}syscomments c where c.id={Id(objectId)} and c.texttype=0 order by c.number,c.colid2,c.colid";
    public static string Columns(string database, int objectId) =>
        $"select c.name, t.name as type, c.length, case when (c.status & 8)=8 then 1 else 0 end as nullable, case when (c.status & 128)=128 then 1 else 0 end as [identity], isnull(c.prec,0) as [precision], isnull(c.scale,0) as scale from {Db(database)}syscolumns c join {Db(database)}systypes t on t.usertype=c.usertype where c.id={Id(objectId)} order by c.colid";
    public static string Indexes(string database, ObjectKey key) =>
        $"if exists (select 1 from {Db(database)}sysindexes where id=object_id({ResultFormats.Literal(ResultFormats.Identifier(database) + "." + ResultFormats.Identifier(key.Owner) + "." + ResultFormats.Identifier(key.Name))}) and indid between 1 and 254) exec {Db(database)}sp_helpindex {ResultFormats.Literal(ResultFormats.Identifier(key.Owner) + "." + ResultFormats.Identifier(key.Name))} else select convert(varchar(255),null) as index_name, convert(varchar(255),null) as index_description, convert(varchar(1024),null) as index_keys where 1=0";
    private static string Db(string database) => string.IsNullOrWhiteSpace(database) ? throw new ArgumentException("Enter an ASE database.") : ResultFormats.Identifier(database) + "..";
    private static string Id(int id) => id > 0 ? id.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException("Invalid catalog object ID.");

    public static IReadOnlyList<ObjectSnapshot> ReadObjects(IQueryEngine engine, string database, CompareObjectType type, CancellationToken cancellation = default)
    {
        var inventory = Read(engine, Objects(database, type), cancellation);
        if (inventory.Tables.Count != 1) throw new FormatException("Expected one object inventory result.");
        var rows = inventory.Tables[0].Rows.Cast<System.Data.DataRow>().ToArray();
        if (rows.Length > ObjectCompare.MaxObjects) throw new ArgumentException($"More than {ObjectCompare.MaxObjects} objects; choose a smaller catalog.");
        var snapshots = new List<ObjectSnapshot>();
        long characters = 0;
        foreach (var row in rows)
        {
            cancellation.ThrowIfCancellationRequested();
            var key = new ObjectKey(CatalogData.Text(row, "owner"), CatalogData.Text(row, "name"));
            int id = Convert.ToInt32(CatalogData.Value(row, "id"), CultureInfo.InvariantCulture);
            try
            {
                if (type == CompareObjectType.Tables)
                {
                    var columns = Read(engine, Columns(database, id), cancellation);
                    var indexes = Read(engine, Indexes(database, key), cancellation);
                    if (columns.Tables.Count != 1 || indexes.Tables.Count != 1) throw new FormatException("Column or index metadata is unavailable.");
                    snapshots.Add(new(key, ObjectCompare.TableDefinition(columns.Tables[0], indexes.Tables[0])));
                }
                else
                {
                    var code = Read(engine, ObjectText(database, id), cancellation);
                    if (code.Tables.Count != 1 || code.Tables[0].Columns.Count != 1 || code.Tables[0].Rows.Count == 0 || code.Tables[0].Rows.Cast<System.Data.DataRow>().Any(r => r[0] is DBNull)) throw new FormatException("Stored text is missing, protected or not visible to this login.");
                    string text = string.Concat(code.Tables[0].Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)));
                    if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Stored text is empty or protected.");
                    snapshots.Add(new(key, text));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException) { snapshots.Add(new(key, "", ex.Message)); }
            var snapshot = snapshots[^1];
            if (snapshot.Definition.Length > ObjectCompare.MaxDefinitionCharacters || (characters += snapshot.Definition.Length) > ObjectCompare.MaxSnapshotCharacters)
                throw new ArgumentException("Object definitions exceed the comparison text limit (4 MiB per object, 32 MiB per side).");
        }
        return snapshots;
    }
    private static QueryOutput Read(IQueryEngine engine, string sql, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = engine.Execute(sql, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (result.Error != null) throw new InvalidOperationException(result.Error);
        return result;
    }
}
