using System.Collections;
using System.Data;
using System.Data.Common;

// In-memory boundary fixture. DataTableReader.GetChars expects char[] rather
// than a SQL string, so provide the DbDataReader streaming-string contract.
// This does not establish native SAP driver behavior.
sealed class StreamingTestReader(params DataTable[] tables) : DbDataReader
{
    private readonly DataTableReader inner = new(tables);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        string text = inner.GetString(ordinal);
        if (buffer == null) return text.Length;
        int count = Math.Min(length, Math.Max(0, text.Length - checked((int)dataOffset)));
        text.AsSpan((int)dataOffset, count).CopyTo(buffer.AsSpan(bufferOffset)); return count;
    }
    public override object this[int ordinal] => inner[ordinal];
    public override object this[string name] => inner[name];
    public override int Depth => inner.Depth;
    public override int FieldCount => inner.FieldCount;
    public override bool HasRows => inner.HasRows;
    public override bool IsClosed => inner.IsClosed;
    public override int RecordsAffected => inner.RecordsAffected;
    public override bool Read() => inner.Read();
    public override bool NextResult() => inner.NextResult();
    public override string GetName(int ordinal) => inner.GetName(ordinal);
    public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
    public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
    public override object GetValue(int ordinal) => inner.GetValue(ordinal);
    public override int GetValues(object[] values) => inner.GetValues(values);
    public override int GetOrdinal(string name) => inner.GetOrdinal(name);
    public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => inner.GetChar(ordinal);
    public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
    public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
    public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
    public override string GetString(int ordinal) => inner.GetString(ordinal);
    public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
    public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
    public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
    public override IEnumerator GetEnumerator() => inner.GetEnumerator();
    public override void Close() => inner.Close();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
