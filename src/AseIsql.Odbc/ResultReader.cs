using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace AseIsql.Odbc;

public sealed class ResultLimitException(string message) : Exception(message);
// A single value the driver could not hand over in the type it described. The run keeps the rows read so far
// and names the column, instead of failing the whole result with a bare conversion message.
public sealed class ResultValueException(string message) : Exception(message);
public sealed class ResultBudget(int maxRows)
{
    public int Rows;
    public long Bytes;
    public int MaxRows { get; } = maxRows;
    public const long MaxBytes = 64 * 1024 * 1024;
    public const int MaxCellBytes = 1024 * 1024;
}

public static class ResultReader
{
    // System.Decimal carries 28-29 significant digits; ASE numeric/decimal reaches 38 (sysobjects.identburnmax is
    // numeric(38,0)). Wider columns are taken as server text, the way isql prints them, rather than overflowing.
    public const int MaxDecimalDigits = 28;

    private record struct ColumnPlan(string Name, Type Type, Type SourceType, bool AsText)
    {
        // Types whose .NET counterpart can hold less than the server sent and convert without complaining:
        // decimal rounds a numeric with more digits than it carries, and the driver's time conversion drops the
        // fraction of a second. Their values are taken as text and only kept typed when the text survives the
        // round trip, so a shortened value becomes visible instead of silently replacing the server's.
        public bool Verify => !AsText && (SourceType == typeof(decimal) || SourceType == typeof(TimeSpan));
    }

    public static void Read(DbDataReader reader, QueryOutput output, ResultBudget budget, CancellationToken cancellation)
    {
        do
        {
            cancellation.ThrowIfCancellationRequested();
            if (reader.FieldCount == 0) continue;
            if (reader.FieldCount > 1024 || output.Tables.Count >= 128) throw new ResultLimitException("Local column/result limit reached. Output is partial.");
            var plans = PlanColumns(reader);
            var table = new DataTable();
            for (int field = 0; field < plans.Length; field++)
            {
                var column = table.Columns.Add("column_" + field, plans[field].Type);
                column.Caption = plans[field].Name; column.ExtendedProperties["SourceName"] = plans[field].Name;
                if (plans[field].AsText) column.ExtendedProperties["ReadAsText"] = plans[field].SourceType.Name;
            }
            output.Tables.Add(table);
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (budget.Rows >= budget.MaxRows) throw new ResultLimitException($"Local limit of {budget.MaxRows} rows reached. Results are partial; subsequent batches were not sent.");
                var values = new object[plans.Length];
                for (int field = 0; field < values.Length; field++)
                {
                    values[field] = ReadCell(reader, table, field, ref plans[field], budget.Rows + 1, cancellation);
                    budget.Bytes += values[field] switch { string text => text.Length * 2L + 32, byte[] bytes => bytes.Length + 32L, _ => 32L };
                    if (budget.Bytes > ResultBudget.MaxBytes) throw new ResultLimitException("Local limit of 64 MiB of values reached. Output is partial.");
                }
                table.Rows.Add(values); budget.Rows++;
            }
        } while (reader.NextResult());
    }

    // The column types are decided once per result, before any value is fetched: System.Data.Odbc caches the
    // converted cell, so a column that would overflow must never be asked for its native value first. Drivers that
    // do not report a precision leave the overflow to be discovered on the first wide row.
    private static ColumnPlan[] PlanColumns(DbDataReader reader)
    {
        DataTable? schema = null;
        try { schema = reader.GetSchemaTable(); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or DbException or IndexOutOfRangeException) { }
        var plans = new ColumnPlan[reader.FieldCount];
        for (int field = 0; field < plans.Length; field++)
        {
            string name = SafeName(reader, field);
            Type? source = null;
            try { source = reader.GetFieldType(field); }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IndexOutOfRangeException or DbException) { }
            // An undescribed type is read as text rather than dropping the column or guessing a numeric shape.
            bool asText = source is null || (source == typeof(decimal) && SchemaInt(schema, field, "NumericPrecision") > MaxDecimalDigits);
            source ??= typeof(string);
            plans[field] = new ColumnPlan(name, asText ? typeof(string) : source, source, asText);
        }
        return plans;
    }

    private static string SafeName(DbDataReader reader, int field)
    {
        try { return reader.GetName(field) ?? ""; }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IndexOutOfRangeException or DbException) { return "column_" + field; }
    }

    private static int SchemaInt(DataTable? schema, int field, string column)
    {
        if (schema is null || field >= schema.Rows.Count || !schema.Columns.Contains(column)) return 0;
        object value = schema.Rows[field][column];
        if (value is null or DBNull) return 0;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) { return 0; }
    }

    private static object ReadCell(DbDataReader reader, DataTable table, int field, ref ColumnPlan plan, int row, CancellationToken cancellation)
    {
        bool asText = plan.AsText;
        // The server's own text comes first for these columns. System.Data.Odbc caches the converted cell, so any
        // call that converts it beforehand - IsDBNull included - is what a later text conversion would return: the
        // rounded decimal, the time without its fraction. Nothing has touched the cell at this point.
        if (asText || plan.Verify)
        {
            string? text = TryReadText(reader, field, cancellation);
            if (text is { Length: > 0 })
            {
                if (!plan.Verify) return text;
                if (Typed(text, plan.SourceType) is { } typed) return typed;
                PromoteColumnToText(table, field, plan.SourceType);
                plan = plan with { Type = typeof(string), AsText = true };
                return text;
            }
            // Empty or refused: only now is it worth asking, and the answer can no longer cost anything.
            if (IsNull(reader, field)) return DBNull.Value;
            if (text is not null && plan.AsText) return text;
        }
        // System.Data.Odbc converts the value to answer this, so a cell wider than its .NET type fails here, before
        // it is ever fetched. A failure means the value exists: only its described type cannot hold it.
        try { if (reader.IsDBNull(field)) return DBNull.Value; }
        catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException) { asText = true; }
        if (!asText)
        {
            try
            {
                return plan.Type == typeof(string) ? ReadText(reader, field, cancellation)
                    : plan.Type == typeof(byte[]) ? ReadBytes(reader, field, cancellation)
                    : reader.GetValue(field);
            }
            catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException) { asText = true; }
        }
        if (!plan.AsText)
        {
            PromoteColumnToText(table, field, plan.SourceType);
            plan = plan with { Type = typeof(string), AsText = true };
        }
        try { return ReadDriverText(reader, field, cancellation); }
        catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException or ArgumentException or NotSupportedException)
        {
            throw new ResultValueException($"Column '{plan.Name}' (row {row}) holds a value the driver could not hand over as {plan.SourceType.Name} or as text: {ex.Message} Output is partial; no value was truncated or replaced.");
        }
    }

    // The typed value, when it carries everything the server's text carried; null when the conversion would drop
    // digits. Leading zeros, a leading plus and the trailing zeros of a fraction are formatting, not value.
    internal static object? Typed(string text, Type type)
    {
        string value = text.Trim();
        if (type == typeof(decimal))
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number)
                && SameNumber(value, number.ToString(CultureInfo.InvariantCulture)) ? number : null;
        if (type == typeof(TimeSpan))
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span)
                && SameNumber(value.Replace(":", ""), span.ToString("c", CultureInfo.InvariantCulture).Replace(":", "")) ? span : null;
        return null;
    }

    private static bool SameNumber(string left, string right) => Normalize(left) == Normalize(right);

    private static string Normalize(string value)
    {
        bool negative = value.StartsWith('-');
        string digits = value.TrimStart('+', '-').TrimStart('0');
        if (digits.Contains('.')) digits = digits.TrimEnd('0').TrimEnd('.');
        if (digits.Length == 0) return "0";
        if (digits[0] == '.') digits = "0" + digits;
        return (negative ? "-" : "") + digits;
    }

    // A column the driver described as numeric but whose values do not fit becomes a text column mid-read. Rows
    // already taken keep their values, rewritten with the invariant culture so the column stays comparable.
    private static void PromoteColumnToText(DataTable table, int field, Type sourceType)
    {
        var original = table.Columns[field];
        original.ExtendedProperties["ReadAsText"] = sourceType.Name;
        if (table.Rows.Count == 0) { original.DataType = typeof(string); return; }
        var replacement = new DataColumn(original.ColumnName + "_text", typeof(string)) { Caption = original.Caption };
        foreach (DictionaryEntry entry in original.ExtendedProperties) replacement.ExtendedProperties[entry.Key] = entry.Value;
        table.Columns.Add(replacement);
        foreach (DataRow existing in table.Rows)
            existing[replacement] = existing[original] is DBNull ? DBNull.Value : (object)(Convert.ToString(existing[original], CultureInfo.InvariantCulture) ?? "");
        string name = original.ColumnName;
        table.Columns.Remove(original);
        replacement.ColumnName = name; replacement.SetOrdinal(field);
    }

    // The driver's own text for this cell, or null when it refuses every text conversion. Unlike ReadDriverText it
    // never falls back to the typed value, whose text would be the shortened one this path exists to avoid.
    private static string? TryReadText(DbDataReader reader, int field, CancellationToken cancellation)
    {
        try { return ReadText(reader, field, cancellation); }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or InvalidOperationException or IndexOutOfRangeException or ArgumentException or FormatException or OverflowException) { }
        try { return reader.GetString(field); }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or InvalidOperationException or IndexOutOfRangeException or ArgumentException or FormatException or OverflowException) { }
        return null;
    }

    private static bool IsNull(DbDataReader reader, int field)
    {
        try { return reader.IsDBNull(field); }
        catch (Exception ex) when (ex is OverflowException or InvalidCastException or FormatException) { return false; }
    }

    // A value whose server type is wider than its .NET counterpart. The driver's own text conversion is asked for
    // first, so the digits arrive exactly as the server prints them.
    private static string ReadDriverText(DbDataReader reader, int field, CancellationToken cancellation)
    {
        try { return ReadText(reader, field, cancellation); }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or InvalidOperationException or IndexOutOfRangeException or ArgumentException) { }
        try { return reader.GetString(field); }
        catch (Exception ex) when (ex is InvalidCastException or NotSupportedException or InvalidOperationException or IndexOutOfRangeException or ArgumentException) { }
        return Convert.ToString(reader.GetValue(field), CultureInfo.InvariantCulture) ?? "";
    }

    private static string ReadText(DbDataReader reader, int field, CancellationToken cancellation)
    {
        char[] buffer = new char[4096]; var text = new StringBuilder();
        long count;
        while ((count = reader.GetChars(field, text.Length, buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((text.Length + count) * 2 > ResultBudget.MaxCellBytes) throw new ResultLimitException("A cell exceeds 1 MiB. Output is partial; the value was not replaced by truncated data.");
            text.Append(buffer, 0, (int)count);
        }
        return text.ToString();
    }

    private static byte[] ReadBytes(DbDataReader reader, int field, CancellationToken cancellation)
    {
        byte[] buffer = new byte[4096]; using var stream = new MemoryStream();
        long count;
        while ((count = reader.GetBytes(field, stream.Length, buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (stream.Length + count > ResultBudget.MaxCellBytes) throw new ResultLimitException("A binary cell exceeds 1 MiB. Output is partial.");
            stream.Write(buffer, 0, (int)count);
        }
        return stream.ToArray();
    }
}
