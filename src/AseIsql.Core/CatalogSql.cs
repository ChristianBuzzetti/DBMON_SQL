using System.Globalization;

namespace AseIsql;

public static partial class CatalogSql
{
    // Prefix is the already-quoted database prefix (or empty for the current database).
    public static string ObjectText(string prefix, string name) =>
        ObjectTextQuery(prefix, "object_id(" + ResultFormats.Literal(name) + ")");

    // A resolved catalog object keeps its id, avoiding another name lookup in the session's context.
    public static string ObjectText(string prefix, int id) =>
        ObjectTextQuery(prefix, id.ToString(CultureInfo.InvariantCulture));

    private static string ObjectTextQuery(string prefix, string id) =>
        "select c.text from " + prefix + "syscomments c where c.id = " + id + " order by c.number, c.colid2, c.colid";
}
