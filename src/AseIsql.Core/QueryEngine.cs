using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AseIsql
{
    public interface IQueryEngine
    {
        string Name { get; }
        QueryOutput Execute(string sql, CancellationToken cancellation = default);
    }

    // What the server reports about the session itself, for the status bar. Never credentials; unknown stays null.
    public sealed record SessionFacts(string Database, int? Spid, string? Login, string? Charset, ServerPlatform Platform = ServerPlatform.Ase);
    public interface ISessionFacts { SessionFacts Facts { get; } }

    public static class Counts
    {
        // "1 row", "2 rows", "100,000 rows": product labels only; source values are never formatted here.
        public static string Of(long count, string noun) =>
            count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
    }

    public sealed class QueryOutput
    {
        public readonly List<DataTable> Tables = new List<DataTable>();
        public readonly List<QueryMessage> Messages = new();
        public int OmittedMessages;
        public string? Error;
        public string? ErrorRawText;
        public string ErrorSource = "CLIENT";
    }

    public sealed class DisconnectedQueryEngine : IQueryEngine
    {
        public string Name => "Disconnected";
        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        { cancellation.ThrowIfCancellationRequested(); return new QueryOutput { Error = "Connect to ASE to execute a query." }; }
    }

    // This local demonstrator evaluates a deliberately small SQL subset.
    // It never opens a network connection and is not an ASE emulator.
    public sealed class DemoQueryEngine : IQueryEngine
    {
        public string Name { get { return "Test fixture · no ASE connection"; } }

        public QueryOutput Execute(string sql, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            QueryOutput output = new QueryOutput();
            if (sql.Length > 100000) { output.Error = "Fixture limit: 100,000 SQL characters per execution."; return output; }
            try
            {
                foreach (string statement in SqlScript.Split(sql, demoStatements: true, cancellation))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (output.Tables.Count >= 32) throw new FormatException("Fixture limit: 32 results per execution.");
                    output.Tables.Add(SqlDemoParser.SelectConstants(statement));
                }
                if (output.Tables.Count == 0) output.Error = "No query to execute.";
            }
            catch (FormatException ex) { output.Error = ex.Message; }
            return output;
        }
    }

    internal static class SqlDemoParser
    {
        public static DataTable SelectConstants(string statement)
        {
            Match select = Regex.Match(statement, @"^SELECT\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking);
            if (!select.Success) throw new FormatException("The fixture supports constant SELECT statements only. Connect to ASE for server queries or DBCC.");
            List<string> expressions = SplitColumns(select.Groups[1].Value);
            DataTable table = new DataTable();
            List<object> values = new List<object>();
            foreach (string expression in expressions)
            {
                // Anchored grammar: FROM, functions and trailing garbage cannot be silently accepted.
                Match item = Regex.Match(expression.Trim(), @"\A(?<value>'(?:[^']|'')*'|NULL|[+-]?[0-9]+(?:\.[0-9]+)?)(?:\s+AS\s+(?<alias>[A-Za-z_][A-Za-z_0-9]*|\[[^\]\r\n]+\]))?\z", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking);
                if (!item.Success) throw new FormatException("Use numbers, strings or NULL with optional AS aliases. The fixture does not support tables, functions or expressions.");
                string literal = item.Groups["value"].Value;
                string name = item.Groups["alias"].Success ? item.Groups["alias"].Value.Trim('[', ']') : "column" + (values.Count + 1);
                if (table.Columns.Contains(name)) throw new FormatException("Fixture: duplicate column alias: " + name);
                object value;
                Type type;
                if (literal.Equals("NULL", StringComparison.OrdinalIgnoreCase)) { value = DBNull.Value; type = typeof(object); }
                else if (literal.StartsWith("'")) { value = literal.Substring(1, literal.Length - 2).Replace("''", "'"); type = typeof(string); }
                else
                {
                    decimal number;
                    if (!decimal.TryParse(literal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
                        throw new FormatException("Fixture number is outside the decimal range.");
                    value = number; type = typeof(decimal);
                }
                table.Columns.Add(name, type);
                values.Add(value);
            }
            table.Rows.Add(values.ToArray());
            return table;
        }

        private static List<string> SplitColumns(string source)
        {
            List<string> parts = new List<string>();
            bool quoted = false, bracket = false;
            int start = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char ch = source[i];
                if (ch == '\'' && !bracket)
                {
                    if (quoted && i + 1 < source.Length && source[i + 1] == '\'') { i++; continue; }
                    quoted = !quoted;
                }
                if (!quoted && ch == '[') bracket = true;
                if (!quoted && ch == ']') bracket = false;
                if (!quoted && !bracket && ch == ',') { parts.Add(source.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(source.Substring(start));
            if (parts.Count > 128) throw new FormatException("Fixture limit: 128 columns per result.");
            return parts;
        }
    }
}
