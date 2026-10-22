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
        var messages = owner.Messages;
        messages.Add(new LogMessage(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), number, source, text, rawText));
        if (messages.Count > 500) messages.RemoveAt(0);
        if (!IsOnScreen(owner)) return;
        LogCount.Text = messages.Count + (messages.Count == 1 ? " message" : " messages");
        if (Messages.IsVisible) Messages.ScrollIntoView(messages[^1]);
    }

    private void ClearMessages(SessionWorkspace owner)
    {
        owner.Messages.Clear();
        if (IsOnScreen(owner)) LogCount.Text = "0 messages";
    }

    // Messages open for the session the user is looking at; a background run never steals the panel.
    private void ShowMessages(SessionWorkspace owner) { if (IsOnScreen(owner)) SetLogVisible(true); }

    // The toolbar reflects the active session; any other session only updates its own tab in the strip.
    private void RefreshFor(SessionWorkspace owner)
    {
        if (IsOnScreen(owner)) RefreshConnection(); else RefreshSessionHeader(owner);
    }

    private static string SourceLabelOf(SessionWorkspace owner) =>
        owner.Engine is AseIsql.Odbc.OdbcSession session ? session.Platform.Tag() : "SQL";
}
