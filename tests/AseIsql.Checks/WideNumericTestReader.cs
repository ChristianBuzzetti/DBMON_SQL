using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

// Boundary fixture for server numerics wider than System.Decimal (ASE numeric(38,0)). Values that do not fit
// overflow on GetValue and on IsDBNull, the way System.Data.Odbc does, since ODBC converts the cell to answer
// either one. This does not establish native SAP driver behavior.
sealed class WideNumericTestReader(string name, int precision, string?[] rows, bool chars = true, bool strings = true, Type? fieldType = null) : DbDataReader
{
    private int row = -1;
    public int ValueCalls { get; private set; }
    public int NullCalls { get; private set; }
    private string? Current => rows[row];
    // Parsing succeeds with rounding past 28-29 significant digits; only an unrepresentable magnitude throws.
    private static bool Fits(string? text) => text is not null && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _);

    public override DataTable GetSchemaTable()
    {
        var schema = new DataTable();
        schema.Columns.Add("ColumnName", typeof(string)); schema.Columns.Add("NumericPrecision", typeof(int));
        schema.Rows.Add(name, precision);
        return schema;
    }
    public override int FieldCount => 1;
    public override string GetName(int ordinal) => name;
    public override Type GetFieldType(int ordinal) => fieldType ?? typeof(decimal);
    public override string GetDataTypeName(int ordinal) => "numeric";
    public override bool Read() => ++row < rows.Length;
    public override bool NextResult() => false;
    public override bool IsDBNull(int ordinal)
    {
        NullCalls++;
        if (Current is null) return true;
        if (fieldType == typeof(TimeSpan)) return false;
        if (!Fits(Current)) throw new OverflowException("Value was either too large or too small for a Decimal.");
        return false;
    }
    public override object GetValue(int ordinal)
    {
        ValueCalls++;
        if (Current is null) return DBNull.Value;
        // The driver's own conversion: a time loses its fraction of a second, a numeric with more digits than
        // System.Decimal carries is rounded, and one too large to hold at all overflows.
        if (fieldType == typeof(TimeSpan)) { var span = TimeSpan.Parse(Current, CultureInfo.InvariantCulture); return new TimeSpan(span.Days, span.Hours, span.Minutes, span.Seconds); }
        if (!Fits(Current)) throw new OverflowException("Value was either too large or too small for a Decimal.");
        return decimal.Parse(Current, CultureInfo.InvariantCulture);
    }
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        if (!chars) throw new InvalidCastException("Chars access is not supported for this column.");
        string text = Current ?? throw new InvalidCastException("Data is Null.");
        if (buffer == null) return text.Length;
        int count = Math.Min(length, Math.Max(0, text.Length - checked((int)dataOffset)));
        text.AsSpan((int)dataOffset, count).CopyTo(buffer.AsSpan(bufferOffset)); return count;
    }
    public override string GetString(int ordinal) => strings ? Current ?? throw new InvalidCastException("Data is Null.") : throw new InvalidCastException("String access is not supported for this column.");
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string column] => GetValue(0);
    public override int Depth => 0;
    public override bool HasRows => rows.Length != 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override int GetValues(object[] values) { values[0] = GetValue(0); return 1; }
    public override int GetOrdinal(string column) => 0;
    public override bool GetBoolean(int ordinal) => throw new InvalidCastException();
    public override byte GetByte(int ordinal) => throw new InvalidCastException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new InvalidCastException();
    public override char GetChar(int ordinal) => throw new InvalidCastException();
    public override Guid GetGuid(int ordinal) => throw new InvalidCastException();
    public override short GetInt16(int ordinal) => throw new InvalidCastException();
    public override int GetInt32(int ordinal) => throw new InvalidCastException();
    public override long GetInt64(int ordinal) => throw new InvalidCastException();
    public override float GetFloat(int ordinal) => throw new InvalidCastException();
    public override double GetDouble(int ordinal) => throw new InvalidCastException();
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => throw new InvalidCastException();
    public override IEnumerator GetEnumerator() => throw new NotSupportedException();
}
