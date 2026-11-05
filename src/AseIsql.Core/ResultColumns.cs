using System.Data;

namespace AseIsql;

// ResultReader keeps stable binding names in ColumnName and server aliases in Caption/SourceName.
public static class ResultColumns
{
    public static DataColumn? Find(DataTable table, params string[] names) => Find(table, names, name => name.Trim());

    // Metadata procedures also vary punctuation in their headings between server releases.
    internal static DataColumn? Find(DataTable table, string[] names, Func<string, string> normalize)
    {
        foreach (string name in names)
        {
            string expected = normalize(name);
            bool Matches(string? value) => value != null && string.Equals(normalize(value), expected, StringComparison.OrdinalIgnoreCase);
            foreach (DataColumn column in table.Columns)
                if (Matches(column.ColumnName) || Matches(column.Caption) || Matches(column.ExtendedProperties["SourceName"] as string))
                    return column;
        }
        return null;
    }

    public static string Name(DataColumn column) => (column.ExtendedProperties["SourceName"] as string ?? column.Caption ?? column.ColumnName).Trim();
    public static string Describe(DataTable table) => string.Join(", ", table.Columns.Cast<DataColumn>().Select(Name));
}
