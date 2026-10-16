using System.Data;

namespace AseIsql;

public sealed record ResultColumn(string Name, string Caption, string SourceName, Type DataType);

// A coherent capture of what a result grid shows: column order and visible rows are fixed when it is taken,
// so later sorting, filtering or closing the tab does not change an export already under way. It holds row
// references, not copied values; result tables are read-only once published, which makes background reads safe.
public sealed class ResultView
{
    private readonly IReadOnlyList<DataRow> rows;
    private readonly int[] ordinals;
    public IReadOnlyList<ResultColumn> Columns { get; }
    public int RowCount => rows.Count;
    public const int ProgressInterval = 2048;

    public ResultView(DataTable source, IReadOnlyList<int> visibleOrdinals, IReadOnlyList<DataRow> visibleRows)
    {
        if (visibleOrdinals.Any(o => o < 0 || o >= source.Columns.Count)) throw new ArgumentException("A visible column does not belong to the result.");
        ordinals = visibleOrdinals.ToArray(); rows = visibleRows;
        Columns = ordinals.Select(o => source.Columns[o]).Select(c => new ResultColumn(c.ColumnName, c.Caption, c.ExtendedProperties["SourceName"] as string ?? c.Caption, c.DataType)).ToArray();
    }

    public static ResultView FromTable(DataTable table) => new(table, Enumerable.Range(0, table.Columns.Count).ToArray(), table.Rows.Cast<DataRow>().ToArray());

    // Values in captured order. Cancellation and progress are checked between rows, never inside a value.
    public IEnumerable<object?[]> Rows(CancellationToken token = default, IProgress<int>? progress = null)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (i % ProgressInterval == 0) { token.ThrowIfCancellationRequested(); if (i > 0) progress?.Report(i); }
            var row = rows[i]; var values = new object?[ordinals.Length];
            for (int c = 0; c < ordinals.Length; c++) values[c] = row[ordinals[c]];
            yield return values;
        }
        token.ThrowIfCancellationRequested(); progress?.Report(rows.Count);
    }

    // Materializes a separate table only for operations that need one, after the caller's row bound is enforced.
    public DataTable ToTable(int maxRows = int.MaxValue, CancellationToken token = default)
    {
        if (rows.Count > maxRows) throw new ArgumentException($"The view has {rows.Count:N0} rows; this action accepts up to {maxRows:N0}.");
        var copy = new DataTable();
        foreach (var column in Columns) { var added = copy.Columns.Add(column.Name, column.DataType); added.Caption = column.Caption; added.ExtendedProperties["SourceName"] = column.SourceName; }
        foreach (var values in Rows(token)) copy.Rows.Add(values);
        return copy;
    }

    // Rough value size used by the pinned-results budget; strings count UTF-16 bytes, binaries their length.
    public static long EstimateBytes(DataTable table, CancellationToken token = default)
    {
        long bytes = 0; int seen = 0;
        foreach (DataRow row in table.Rows)
        {
            if (++seen % ProgressInterval == 0) token.ThrowIfCancellationRequested();
            foreach (object? value in row.ItemArray) bytes += value is string s ? s.Length * 2L : value is byte[] binary ? binary.Length : 16;
        }
        return bytes;
    }
}
