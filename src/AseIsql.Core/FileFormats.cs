using System.Data;
using System.Globalization;
using System.Text;

namespace AseIsql;

public enum QueryExportFormat { Csv, Tsv, JsonLines }

// One writer per script, shared across GO batches. Only the current row is held in memory.
// CSV/TSV sets are separated by a blank line and each has its own header (including empty sets).
// The caller owns the UTF-8 file and keeps it on cancellation or failure as partial output.
public sealed class QueryExportWriter
{
    private readonly TextWriter output;
    private readonly QueryExportFormat format;
    private long rowsWritten;
    public long RowsWritten => Interlocked.Read(ref rowsWritten);
    public int ResultSets { get; private set; }

    public QueryExportWriter(TextWriter output, QueryExportFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        this.output = output; this.format = format;
    }

    public void Write(IDataReader reader, CancellationToken cancellation = default)
    {
        do { cancellation.ThrowIfCancellationRequested(); WriteResult(reader, cancellation); }
        while (NextResult(reader, cancellation));
    }

    private static bool NextResult(IDataReader reader, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); return reader.NextResult(); }

    // The ODBC adapter supplies its lossless cell conversion; Core and other engines use GetValue directly.
    public void WriteResult(IDataReader reader, CancellationToken cancellation = default, Func<int, object?>? readValue = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (reader.FieldCount == 0) return;
        var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        int set = ++ResultSets;
        IEnumerable<IReadOnlyList<object?>> Rows()
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!reader.Read()) break;
                var values = new object?[headers.Length];
                for (int field = 0; field < values.Length; field++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    values[field] = readValue == null ? reader.GetValue(field) : readValue(field);
                }
                cancellation.ThrowIfCancellationRequested();
                yield return values;
                // The consumer resumes only after a complete row was written.
                Interlocked.Increment(ref rowsWritten);
            }
        }
        if (format == QueryExportFormat.JsonLines)
        {
            foreach (var row in Rows()) { output.Write(ResultFormats.JsonLine(set, headers, row)); output.Write('\n'); }
        }
        else
        {
            if (set > 1) output.Write("\r\n");
            DelimitedText.Write(output, headers, Rows(), format == QueryExportFormat.Csv ? ',' : '\t', includeHeaders: true, maxCharacters: long.MaxValue);
        }
        output.Flush();
    }
}

public static class CsvExport
{
    // Export source values, never reinterpret them through UI formatting.
    // CSV has no native NULL type: both NULL and an empty string appear empty.
    public static string Serialize(DataTable table)
    {
        return DelimitedText.Serialize(table.Columns.Cast<DataColumn>().Select(c => c.ExtendedProperties["SourceName"] is string sourceName ? sourceName : c.ColumnName).ToArray(),
            table.Rows.Cast<DataRow>().Select(row => (IReadOnlyList<object?>)row.ItemArray), ',', includeHeaders: true, forceQuotes: true, maxCharacters: int.MaxValue);
    }
    // Streams a captured view to the writer: no complete copy of the table or of the text is held in memory.
    public static void Write(TextWriter writer, ResultView view, CancellationToken token = default, IProgress<int>? progress = null) =>
        DelimitedText.Write(writer, view.Columns.Select(c => c.SourceName).ToArray(), view.Rows(token, progress), ',', includeHeaders: true, forceQuotes: true, maxCharacters: long.MaxValue);
}

public static class DelimitedText
{
    public const int ClipboardCharacterLimit = 16 * 1024 * 1024;

    // Serialize source values once, independently of truncated grid display text.
    // Sparse selection holes and DBNull are empty fields; this text format has no NULL type.
    public static string Serialize(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, char delimiter = '\t',
        bool includeHeaders = false, bool headersOnly = false, bool forceQuotes = false, int maxCharacters = ClipboardCharacterLimit, bool quoteFields = true)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        Write(output, headers, rows, delimiter, includeHeaders, headersOnly, forceQuotes, maxCharacters, quoteFields);
        return output.ToString();
    }

    public static void Write(TextWriter output, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, char delimiter = '\t',
        bool includeHeaders = false, bool headersOnly = false, bool forceQuotes = false, long maxCharacters = ClipboardCharacterLimit, bool quoteFields = true)
    {
        if (delimiter is '\r' or '\n' or '"' || maxCharacters < 1) throw new ArgumentException("Invalid delimiter or limit.");
        long written = 0;
        void Check(long count)
        { if (count > maxCharacters - written) throw new ArgumentException("The copy exceeds the clipboard text limit. Select fewer values or export to CSV."); written += count; }
        void Field(object? value)
        {
            if (value is byte[] bytes && 2L + 2L * bytes.Length > maxCharacters - written) Check(2L + 2L * bytes.Length);
            string text = value switch { null or DBNull => "", byte[] binary => "0x" + Convert.ToHexString(binary),
                DateTime time => time.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset time => time.ToString("O", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
                TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
                TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
                // ASE bit columns hold 1 and 0; True/False is .NET's spelling, not a value the server ever sent.
                bool bit => bit ? "1" : "0", _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
            // Ordinary clipboard text must preserve generated SQL; explicit delimited formats retain CSV escaping.
            bool quote = forceQuotes || (quoteFields && text.IndexOfAny([delimiter, '\r', '\n', '"']) >= 0);
            Check((long)text.Length + (quote ? 2L + text.Count(c => c == '"') : 0));
            if (!quote) { output.Write(text); return; }
            output.Write('"');
            foreach (char character in text) { output.Write(character); if (character == '"') output.Write('"'); }
            output.Write('"');
        }
        void Row(IEnumerable<object?> values)
        {
            bool first = true;
            foreach (object? value in values) { if (!first) { Check(1); output.Write(delimiter); } first = false; Field(value); }
            Check(2); output.Write("\r\n");
        }
        if (includeHeaders || headersOnly) Row(headers.Cast<object?>());
        if (!headersOnly)
            foreach (var row in rows) { if (row.Count != headers.Count) throw new ArgumentException("Row values do not match the columns."); Row(row); }
    }
}

public static class AtomicFiles
{
    // Stage alongside the destination so rename/replace stays on the same volume.
    // Existing files receive a unique backup; failed writes preserve the original.
    public static void SaveUtf8(string path, string text) => WriteUtf8(path, writer => writer.Write(text));

    // Text producers stream into the staging file. Any exception, including cancellation, deletes the staging
    // file and leaves the destination and its earlier backups untouched.
    public static void WriteUtf8(string path, Action<TextWriter> write) => Write(path, stream =>
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false, true), 1 << 16, leaveOpen: true);
        write(writer); writer.Flush();
    });

    public static void Write(string path, Action<Stream> write, CancellationToken token = default)
    {
        string fullPath = Path.GetFullPath(path);
        string staging = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16)) { write(stream); stream.Flush(true); }
            token.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Replace(staging, fullPath, fullPath + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "." + Guid.NewGuid().ToString("N") + ".bak");
            else File.Move(staging, fullPath);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
