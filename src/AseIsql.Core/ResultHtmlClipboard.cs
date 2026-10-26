using System.Data;
using System.Globalization;
using System.Text;

namespace AseIsql;

// A portable table fragment: inline styles survive rich-text clipboard consumers better than a stylesheet.
public static class ResultHtmlClipboard
{
    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows,
        int maxCharacters = DelimitedText.ClipboardCharacterLimit)
    {
        if (maxCharacters < 1) throw new ArgumentException("Invalid clipboard limit.");
        var output = new StringBuilder();
        void Append(string text)
        {
            if (text.Length > maxCharacters - output.Length)
                throw new ArgumentException("The HTML copy exceeds the clipboard text limit. Select fewer values or export to a file.");
            output.Append(text);
        }
        void Value(object? value)
        {
            if (value is byte[] bytes && 2L + 2L * bytes.Length > maxCharacters - output.Length)
                throw new ArgumentException("The HTML copy exceeds the clipboard text limit. Select fewer values or export to a file.");
            if (value is DBNull) { Append("<span style=\"color:#64748b;font-style:italic\">NULL</span>"); return; }
            string text = value switch {
                null => "", byte[] binary => "0x" + Convert.ToHexString(binary),
                DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
            };
            // Encode incrementally so escaped expansion is bounded before allocating the whole result.
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                if (c == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", '\'' => "&#39;", '\r' or '\n' => "<br>", '\0' => "&#xfffd;", _ => c.ToString() });
            }
        }
        const string cellStyle = "padding:7px 12px;border:1px solid #cbd5e1;text-align:left;vertical-align:top;white-space:pre-wrap;";
        Append("<table style=\"border-collapse:collapse;font:13px 'Segoe UI',Arial,sans-serif;color:#1e293b;background:#ffffff\"><thead><tr>");
        foreach (string header in headers)
        {
            Append("<th scope=\"col\" style=\"" + cellStyle + "background:#e2e8f0;font-weight:600\">");
            Value(header); Append("</th>");
        }
        Append("</tr></thead><tbody>");
        int rowIndex = 0;
        foreach (var row in rows)
        {
            if (row.Count != headers.Count) throw new ArgumentException("Row values do not match the columns.");
            Append("<tr>");
            foreach (object? value in row)
            {
                Append("<td style=\"" + cellStyle + (rowIndex % 2 == 0 ? "background:#ffffff" : "background:#f8fafc") + "\">");
                Value(value); Append("</td>");
            }
            Append("</tr>"); rowIndex++;
        }
        Append("</tbody></table>");
        return output.ToString();
    }

    // CF_HTML offsets are UTF-8 byte counts, including the ASCII header, not .NET character offsets.
    public static string Payload(string table)
    {
        if (table.Length > DelimitedText.ClipboardCharacterLimit)
            throw new ArgumentException("The HTML copy exceeds the clipboard text limit.");
        const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        int startHtml = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(prefix);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(table);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(suffix);
        return string.Format(CultureInfo.InvariantCulture, header, startHtml, endHtml, startFragment, endFragment) + prefix + table + suffix;
    }
}
