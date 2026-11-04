using System.Globalization;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Output belongs to the session that produced it. The shell shows the active workspace only, so a query that
    // finishes while another tab is on screen writes into its own workspace and appears when that tab comes back.
    private bool IsOnScreen(SessionWorkspace owner) => ReferenceEquals(owner, activeSession);

    private void SetStatus(SessionWorkspace owner, string text)
    {
        owner.Status = text;
        if (IsOnScreen(owner)) StatusText.Text = text;
    }

    private void AddMessage(SessionWorkspace owner, int number, string source, string text, string? rawText = null)
    {
        owner.MessageBuffer.Add(LogRowOf(new QueryMessage(number, source, text, rawText)));
        RefreshMessages(owner);
    }

    private static LogMessage LogRowOf(QueryMessage message) => new(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), message.Number, message.Source, message.Text, message.RawText);

    private void AddOutputMessages(SessionWorkspace owner, QueryOutput output, string? source = null)
    {
        owner.MessageBuffer.Append(output.Messages.Select(message => LogRowOf(source == null ? message : message with { Source = source })), output.OmittedMessages);
        RefreshMessages(owner);
    }

    // Only called on the dispatcher. Retain existing rows so the head and selection stay stable on each tick.
    private void RefreshMessages(SessionWorkspace owner)
    {
        var rows = owner.MessageBuffer.Snapshot(count => new LogMessage("", 0, "CLIENT", HeadTailBuffer<LogMessage>.OmissionText(count)));
        var messages = owner.Messages;
        bool changed = messages.Count != rows.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i == messages.Count) messages.Add(rows[i]);
            else if (messages[i] != rows[i]) { messages[i] = rows[i]; changed = true; }
        }
        while (messages.Count > rows.Count) messages.RemoveAt(messages.Count - 1);
        if (!IsOnScreen(owner) || !changed) return;
        LogCount.Text = messages.Count + (messages.Count == 1 ? " message" : " messages");
        if (Messages.IsVisible && messages.Count > 0) Messages.ScrollIntoView(messages[^1]);
    }

    // Report runs on the engine thread. The bounded buffer is thread safe; no dispatcher work is posted per row.
    private sealed class SessionMessageProgress(SessionWorkspace owner) : IProgress<QueryMessage>
    {
        public bool Reported { get; private set; }
        public void Report(QueryMessage message) { owner.MessageBuffer.Add(LogRowOf(message)); Reported = true; }
    }

    private void ClearMessages(SessionWorkspace owner)
    {
        owner.MessageBuffer.Clear();
        owner.Messages.Clear();
        if (IsOnScreen(owner)) LogCount.Text = "0 messages";
    }

    // Messages open for the session the user is looking at; a background run never steals the panel.
    private void ShowMessages(SessionWorkspace owner) { if (IsOnScreen(owner)) SetLogVisible(true); }

    // The toolbar reflects the active session; any other session only updates its own tab in the strip.
    private void RefreshFor(SessionWorkspace owner, bool preserveCompletion = false)
    {
        if (IsOnScreen(owner)) RefreshConnection(preserveCompletion); else RefreshSessionHeader(owner);
    }

    private static string SourceLabelOf(SessionWorkspace owner) =>
        owner.Engine is AseIsql.Odbc.OdbcSession session ? session.Platform.Tag() : "SQL";
}
