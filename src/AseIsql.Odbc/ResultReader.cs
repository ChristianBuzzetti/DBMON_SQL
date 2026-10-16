using System.Data;
using System.Data.Common;
using System.Text;

namespace AseIsql.Odbc;

public sealed class ResultLimitException(string message) : Exception(message);
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
    public static void Read(DbDataReader reader, QueryOutput output, ResultBudget budget, CancellationToken cancellation)
    {
        do
        {
            cancellation.ThrowIfCancellationRequested();
            if (reader.FieldCount == 0) continue;
            if (reader.FieldCount > 1024 || output.Tables.Count >= 128) throw new ResultLimitException("Local column/result limit reached. Output is partial.");
            var table = new DataTable();
            for (int field = 0; field < reader.FieldCount; field++)
            {
                var column = table.Columns.Add("column_" + field, reader.GetFieldType(field));
                column.Caption = reader.GetName(field); column.ExtendedProperties["SourceName"] = reader.GetName(field);
            }
            output.Tables.Add(table);
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (budget.Rows >= budget.MaxRows) throw new ResultLimitException($"Local limit of {budget.MaxRows} rows reached. Results are partial; subsequent batches were not sent.");
                var values = new object[reader.FieldCount];
                for (int field = 0; field < values.Length; field++)
                {
                    if (reader.IsDBNull(field)) { values[field] = DBNull.Value; continue; }
                    Type type = table.Columns[field].DataType;
                    values[field] = type == typeof(string) ? ReadText(reader, field, cancellation) : type == typeof(byte[]) ? ReadBytes(reader, field, cancellation) : reader.GetValue(field);
                    budget.Bytes += values[field] switch { string text => text.Length * 2L + 32, byte[] bytes => bytes.Length + 32L, _ => 32L };
                    if (budget.Bytes > ResultBudget.MaxBytes) throw new ResultLimitException("Local limit of 64 MiB of values reached. Output is partial.");
                }
                table.Rows.Add(values); budget.Rows++;
            }
        } while (reader.NextResult());
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
