namespace AseIsql.Odbc;

// SAP can return cumulative native diagnostic strings. Only remove an exact,
// previously observed prefix followed by another SAP diagnostic frame.
// Keep raw evidence and preserve repeated independent messages.
public sealed class DiagnosticMessages
{
    private const string DriverPrefix = "[SAP][ASE ODBC Driver][Adaptive Server Enterprise]";
    private string previous = "";
    public QueryMessage Read(int number, string state, string raw, string password)
    {
        string display = raw;
        if (previous.Length > 0 && raw.Length > previous.Length && raw.StartsWith(previous, StringComparison.Ordinal))
        {
            string tail = raw[previous.Length..].TrimStart('\r', '\n', ' ');
            if (tail.StartsWith("[SAP]", StringComparison.Ordinal)) display = tail;
        }
        previous = raw;
        // Strip only the known transport frame at the beginning of each record.
        // Identical text embedded in the server payload is still user evidence.
        display = string.Join('\n', display.Split('\n').Select(line =>
            line.StartsWith(DriverPrefix, StringComparison.Ordinal) ? line[DriverPrefix.Length..].TrimStart(' ') : line));
        // The frame can own a whole line, and removing it then leaves the record starting with a blank one. Messages
        // shows a single line per record, so such a record reads as empty while its text sits out of sight below.
        // Only the leading blank goes: trailing text is left exactly as the server wrote it.
        display = display.TrimStart('\r', '\n', ' ');
        // ASE separates its trailing blocks with records that are nothing but the transport frame. Unframing leaves
        // them empty, and an empty display is the caller's signal to drop the record: it carries no server text, and
        // the frame is the one thing this class exists to hide. RawText still holds it for anyone who asks.
        return new(number, "ODBC " + state, OdbcSession.Redact(display, password), OdbcSession.Redact(raw, password));
    }
}
