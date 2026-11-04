using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private sealed class RunText(Func<string> format)
    {
        private string? text;
        public string Text => text ??= format();
        private IReadOnlyList<SysmonSection>? sections;
        public IReadOnlyList<SysmonSection> Sections => sections ??= ResultFormats.SysmonSections(Text);
    }
    private sealed class SessionRunText
    {
        public RunText? Latest, Displayed;
    }
    private readonly ConditionalWeakTable<SessionWorkspace, SessionRunText> sessionRunText = new();
    private readonly ConditionalWeakTable<ResultRun, RunText> resultRunText = new();
    private RunText? visibleRunText;

    private void CaptureRunText(SessionWorkspace owner, ResultRun run, QueryOutput output) =>
        CaptureRunText(owner, run, () => ResultFormats.Isql(output));

    private void CaptureRunText(SessionWorkspace owner, ResultRun run, Func<string> format)
    {
        var snapshot = new RunText(format);
        resultRunText.Add(run, snapshot);
        var state = sessionRunText.GetValue(owner, _ => new SessionRunText());
        state.Latest = state.Displayed = snapshot;
        if (IsOnScreen(owner)) RefreshRunText();
    }

    private void SelectRunText()
    {
        var state = sessionRunText.GetValue(activeSession, _ => new SessionRunText());
        state.Displayed = RunOf(Workspace.SelectedItem as TabItem) is { } run && resultRunText.TryGetValue(run, out var snapshot) ? snapshot : state.Latest;
        RefreshRunText();
    }

    private void RefreshRunText()
    {
        // Formatting is deferred until this view is opened. Grid-only users do not pay to format the run.
        if (TextOutputTab == null || !TextOutputTab.IsSelected) return;
        var snapshot = sessionRunText.GetValue(activeSession, _ => new SessionRunText()).Displayed;
        if (ReferenceEquals(snapshot, visibleRunText)) return;
        visibleRunText = snapshot;
        RunTextBox.Text = snapshot?.Text ?? "";
        SysmonSections.ItemsSource = snapshot?.Sections;
        SysmonIndex.Visibility = snapshot?.Sections.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RunTextBox.ScrollToHome();
        CopyRunTextButton.IsEnabled = SaveRunTextButton.IsEnabled = snapshot != null;
    }

    private void OutputViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == OutputViews) RefreshRunText();
    }

    private void SysmonSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SysmonSections.SelectedItem is not SysmonSection section) return;
        // Keep focus in the list so arrow keys can visit successive sections.
        RunTextBox.Select(RunTextBox.GetCharacterIndexFromLineIndex(section.Line), RunTextBox.GetLineLength(section.Line));
        RunTextBox.ScrollToLine(section.Line);
        RunTextBox.ScrollToHorizontalOffset(0);
    }

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
    private void ShowMessages(SessionWorkspace owner)
    {
        if (!IsOnScreen(owner)) return;
        SetLogVisible(true); MessagesOutputTab.IsSelected = true;
    }

    // The toolbar reflects the active session; any other session only updates its own tab in the strip.
    private void RefreshFor(SessionWorkspace owner, bool preserveCompletion = false)
    {
        if (IsOnScreen(owner)) RefreshConnection(preserveCompletion); else RefreshSessionHeader(owner);
    }

    private static string SourceLabelOf(SessionWorkspace owner) =>
        owner.Engine is AseIsql.Odbc.OdbcSession session ? session.Platform.Tag() : "SQL";
}
