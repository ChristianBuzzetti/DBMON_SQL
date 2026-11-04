using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql;

// Each table has an explicit, named metadata contract, shared by live catalog reads and fake DataTable checks.
public sealed record DdlTableMetadata(DataTable Header, DataTable Columns, DataTable IndexKeys,
    DataTable ForeignKeys, DataTable Text, DataTable Checks);

public static class DdlGenerator
{
    private static string Q(string name) => ResultFormats.Identifier(name);
    private static string S(DataRow row, string name) => MetadataRows.Text(row, name);
    private static int N(DataRow row, string name) => MetadataRows.Int(row, name);
    private static bool B(DataRow row, string name) => MetadataRows.Bit(row, name);
    private static string Name(string owner, string name) => Q(owner) + "." + Q(name);
    private static string Begin(string database) => "-- Generated from visible ASE catalog metadata. Review before execution.\n" +
        "-- Grants, physical placement, partition layout and storage options are not scripted.\nUSE " + Q(database) + "\nGO\n\n";

    public static string DataType(DataRow column)
    {
        string type = S(column, "type");
        // ASE alias datatype names are unique within the database, without an owner qualifier.
        if (B(column, "is_user_type")) return Q(type);
        string lower = type.ToLowerInvariant();
        if (lower is "numeric" or "decimal") {
            int precision = N(column, "precision"), scale = N(column, "scale");
            if (precision is < 1 or > 38 || scale < 0 || scale > precision) throw new FormatException("Invalid numeric precision/scale.");
            return lower + "(" + precision.ToString(CultureInfo.InvariantCulture) + "," + scale.ToString(CultureInfo.InvariantCulture) + ")";
        }
        if (lower is "char" or "varchar" or "binary" or "varbinary" or "nchar" or "nvarchar" or "unichar" or "univarchar") {
            int length = N(column, "length");
            // Catalog lengths are bytes; Unicode and national character declarations use characters.
            if (lower is "unichar" or "univarchar") {
                if (length % 2 != 0) throw new FormatException("Invalid Unicode byte length.");
                length /= 2;
            }
            if (lower is "nchar" or "nvarchar") {
                int size = N(column, "national_char_size");
                if (size <= 0 || length % size != 0) throw new FormatException("Invalid national character byte length.");
                length /= size;
            }
            if (length <= 0) throw new FormatException("Invalid datatype length.");
            return lower + "(" + length.ToString(CultureInfo.InvariantCulture) + ")";
        }
        // Built-in names are catalog values, but reject syntax rather than splicing an expression into a type.
        if (!Regex.IsMatch(lower, @"\A[a-z][a-z0-9_]*(?: [a-z][a-z0-9_]*)?\z", RegexOptions.CultureInvariant))
            throw new FormatException("Invalid built-in datatype name.");
        return lower;
    }

    public static string Table(string database, string owner, string name, DdlTableMetadata metadata)
    {
        if (metadata.Header.Rows.Count != 1 || metadata.Columns.Rows.Count == 0)
            throw new FormatException("The table definition is missing or not visible.");
        if (S(metadata.Header.Rows[0], "name") != name || S(metadata.Header.Rows[0], "owner") != owner)
            throw new FormatException("The table was renamed or its owner changed; reload the explorer.");
        string scheme = S(metadata.Header.Rows[0], "lock_scheme").Trim().ToLowerInvariant();
        if (scheme is not ("allpages" or "datapages" or "datarows")) throw new FormatException("Unknown ASE lock scheme.");
        string target = Name(owner, name);
        var text = TextFragments(metadata.Text);
        var sql = new StringBuilder(Begin(database));
        var prerequisites = new HashSet<int>();
        var bindings = new List<string>();
        var columns = new List<string>();
        var rows = metadata.Columns.Rows.Cast<DataRow>().OrderBy(row => N(row, "ordinal")).ToArray();
        if (rows.Select(row => N(row, "ordinal")).Distinct().Count() != rows.Length || rows.Select(row => S(row, "name")).Distinct().Count() != rows.Length)
            throw new FormatException("Duplicate column metadata.");
        foreach (var column in rows) {
            int status = N(column, "status2");
            if ((status & (16 | 32 | 64 | 128)) != 0)
                throw new FormatException("Computed or encrypted columns need a server-specific script; no incomplete DDL was generated.");
            string columnName = S(column, "name");
            string definition = "    " + Q(columnName) + " " + DataType(column);
            if (B(column, "identity")) definition += " IDENTITY";
            int defaultId = N(column, "default_id");
            if (defaultId != 0) {
                string source = Definition(text, defaultId);
                if (IsBoundDefinition(source, "default")) {
                    Prerequisite(defaultId, BoundDefinition(source, "default", S(column, "default_owner"), S(column, "default_name")));
                    bindings.Add("exec sp_bindefault " + ResultFormats.Literal(Name(S(column, "default_owner"), S(column, "default_name"))) + ", " + ResultFormats.Literal(target + "." + Q(columnName)) + "\nGO\n");
                } else {
                    string expression = Regex.Replace(source.Trim().TrimEnd(';'), @"\A\s*default\s+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (expression.Length == 0) throw new FormatException("An inline default has no expression.");
                    definition += " DEFAULT " + expression + (expression.Contains("--", StringComparison.Ordinal) ? "\n        " : "");
                }
            }
            definition += B(column, "nullable") ? " NULL" : " NOT NULL";
            columns.Add(definition);
            int ruleId = N(column, "rule_id");
            if (ruleId != 0) {
                string source = Definition(text, ruleId);
                if (!IsBoundDefinition(source, "rule"))
                    throw new FormatException("A bound rule has no CREATE RULE definition.");
                Prerequisite(ruleId, BoundDefinition(source, "rule", S(column, "rule_owner"), S(column, "rule_name")));
                bindings.Add("exec sp_bindrule " + ResultFormats.Literal(Name(S(column, "rule_owner"), S(column, "rule_name"))) + ", " + ResultFormats.Literal(target + "." + Q(columnName)) + "\nGO\n");
            }
        }
        sql.Append("CREATE TABLE ").Append(target).Append(" (\n").Append(string.Join(",\n", columns)).Append("\n) LOCK ").Append(scheme).Append("\nGO\n\n");
        sql.Append(Indexes(owner, name, metadata.IndexKeys));
        foreach (var group in metadata.ForeignKeys.Rows.Cast<DataRow>().GroupBy(row => S(row, "constraint_name"))) {
            var keys = OrderedKeys(group);
            var first = keys[0];
            string referenced = Q(S(first, "ref_database")) + "." + Name(S(first, "ref_owner"), S(first, "ref_table"));
            if (keys.Any(row => S(row, "ref_database") != S(first, "ref_database") || S(row, "ref_owner") != S(first, "ref_owner") || S(row, "ref_table") != S(first, "ref_table")))
                throw new FormatException("Inconsistent foreign-key target.");
            sql.Append("ALTER TABLE ").Append(target).Append(" ADD CONSTRAINT ").Append(Q(group.Key))
                .Append(" FOREIGN KEY (").Append(string.Join(", ", keys.Select(row => Q(S(row, "column_name")))))
                .Append(") REFERENCES ").Append(referenced).Append(" (").Append(string.Join(", ", keys.Select(row => Q(S(row, "ref_column")))))
                .Append(')');
            if (B(first, "match_full")) sql.Append(" MATCH FULL");
            sql.Append("\nGO\n\n");
        }
        foreach (DataRow check in metadata.Checks.Rows) {
            string source = Definition(text, N(check, "id")).Trim().TrimEnd(';');
            var match = Regex.Match(source, @"\bcheck\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) throw new FormatException("A check constraint has no CHECK expression.");
            sql.Append("ALTER TABLE ").Append(target).Append(" ADD CONSTRAINT ").Append(Q(S(check, "name"))).Append(' ')
                .Append(source[match.Index..]).Append("\nGO\n\n");
        }
        foreach (string binding in bindings) sql.Append(binding).AppendLine();
        return sql.ToString();

        void Prerequisite(int id, string source) {
            if (prerequisites.Add(id)) sql.Append(source.Trim().TrimEnd(';')).Append("\nGO\n\n");
        }
    }

    public static string Index(string database, string owner, string table, DataTable indexKeys) =>
        indexKeys.Rows.Count == 0 ? throw new FormatException("No visible index keys.") : Begin(database) + Indexes(owner, table, indexKeys);

    private static string Indexes(string owner, string table, DataTable indexKeys)
    {
        var sql = new StringBuilder(); string target = Name(owner, table);
        foreach (var group in indexKeys.Rows.Cast<DataRow>().GroupBy(row => S(row, "index_name"))) {
            var keys = OrderedKeys(group); var first = keys[0];
            foreach (string flag in new[] { "clustered", "is_unique", "primary_key", "unique_constraint" })
                if (keys.Any(row => B(row, flag) != B(first, flag))) throw new FormatException("Inconsistent index metadata.");
            if (MetadataRows.Find(first.Table, "function_based") != null && B(first, "function_based"))
                throw new FormatException("Function-based indexes need a server-specific script.");
            string columns = string.Join(", ", keys.Select(row => {
                string order = S(row, "sort_order").Trim().ToUpperInvariant();
                if (order is not ("ASC" or "DESC")) throw new FormatException("Unknown index sort order.");
                return Q(S(row, "column_name")) + " " + order;
            }));
            string clustering = B(first, "clustered") ? "CLUSTERED" : "NONCLUSTERED";
            if (B(first, "primary_key") || B(first, "unique_constraint")) {
                sql.Append("ALTER TABLE ").Append(target).Append(" ADD CONSTRAINT ").Append(Q(group.Key)).Append(' ')
                    .Append(B(first, "primary_key") ? "PRIMARY KEY " : "UNIQUE ").Append(clustering).Append(" (").Append(columns).Append(')');
            } else {
                sql.Append("CREATE ").Append(B(first, "is_unique") ? "UNIQUE " : "").Append(clustering).Append(" INDEX ").Append(Q(group.Key))
                    .Append(" ON ").Append(target).Append(" (").Append(columns).Append(')');
            }
            sql.Append("\nGO\n\n");
        }
        return sql.ToString();
    }
    private static DataRow[] OrderedKeys(IEnumerable<DataRow> group)
    {
        var rows = group.OrderBy(row => N(row, "key_ordinal")).ToArray();
        if (!rows.Select(row => N(row, "key_ordinal")).SequenceEqual(Enumerable.Range(1, rows.Length)))
            throw new FormatException("Missing or duplicate key ordinals.");
        if (rows.Any(row => N(row, "key_count") != rows.Length)) throw new FormatException("Incomplete index or foreign-key metadata.");
        return rows;
    }
    private static bool IsBoundDefinition(string source, string kind) => Regex.IsMatch(SqlRisk.CodeOnly(source),
        @"\A\s*create\s+" + kind + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string BoundDefinition(string source, string kind, string owner, string name)
    {
        var marker = Regex.Match(SqlRisk.CodeOnly(source), @"\bas\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!marker.Success || string.IsNullOrWhiteSpace(source[(marker.Index + marker.Length)..]))
            throw new FormatException("Missing bound " + kind + " expression.");
        // Original source can omit its owner. Script the catalog owner explicitly, even when run by another login.
        return "CREATE " + kind.ToUpperInvariant() + " " + Name(owner, name) + " AS" + source[(marker.Index + marker.Length)..];
    }
    private static Dictionary<int, string> TextFragments(DataTable table)
    {
        MetadataRows.Require(table, "id"); var column = MetadataRows.Require(table, "text");
        // SQL orders number, colid2, colid; never trim individual fragments or tokens can be joined together.
        return table.Rows.Cast<DataRow>().GroupBy(row => N(row, "id")).ToDictionary(group => group.Key,
            group => string.Concat(group.Select(row => row[column] is DBNull ? throw new FormatException("Protected catalog text.") : Convert.ToString(row[column], CultureInfo.InvariantCulture))));
    }
    private static string Definition(Dictionary<int, string> text, int id) => text.TryGetValue(id, out string? source) && !string.IsNullOrWhiteSpace(source)
        ? source : throw new FormatException("Definition text is missing or protected (id " + id.ToString(CultureInfo.InvariantCulture) + ").");

    public static string Code(string database, DataTable fragments)
    {
        if (fragments.Columns.Count != 1 || fragments.Rows.Count == 0) throw new FormatException("No stored definition is visible.");
        string source = string.Concat(fragments.Rows.Cast<DataRow>().Select(row => row[0] is DBNull ? throw new FormatException("Protected definition text.") : Convert.ToString(row[0], CultureInfo.InvariantCulture)));
        if (string.IsNullOrWhiteSpace(source)) throw new FormatException("The stored definition is empty or protected.");
        return Begin(database) + source + "\nGO\n";
    }
    // ASE creates alias types with sp_addtype, rather than SQL Server's CREATE TYPE syntax.
    public static string UserType(string database, DataTable metadata)
    {
        if (metadata.Rows.Count != 1) throw new FormatException("No unique visible user-defined type.");
        var row = metadata.Rows[0];
        if (B(row, "is_user_type")) throw new FormatException("An alias type must have a built-in base datatype.");
        string owner = S(row, "owner"), name = S(row, "name");
        var sql = new StringBuilder(Begin(database));
        sql.Append("-- Run as ").Append(Q(owner)).Append(" to preserve the type owner.\nexec sp_addtype ").Append(ResultFormats.Literal(name)).Append(", ")
            .Append(ResultFormats.Literal(DataType(row))).Append(", ").Append(ResultFormats.Literal(B(row, "identity") ? "identity" : B(row, "nullable") ? "null" : "not null")).Append("\nGO\n");
        foreach (string kind in new[] { "default", "rule" }) {
            if (N(row, kind + "_id") == 0) continue;
            sql.Append("-- Requires the existing bound ").Append(kind).Append(" definition.\nexec ").Append(kind == "default" ? "sp_bindefault " : "sp_bindrule ")
                .Append(ResultFormats.Literal(Name(S(row, kind + "_owner"), S(row, kind + "_name")))).Append(", ").Append(ResultFormats.Literal(name)).Append("\nGO\n");
        }
        return sql.ToString();
    }
}
