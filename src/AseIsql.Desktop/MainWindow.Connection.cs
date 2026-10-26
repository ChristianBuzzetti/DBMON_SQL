using System.ComponentModel;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private ConnectionProfile? lastProfile { get => activeSession.Profile; set => activeSession.Profile = value; }
    private bool closeApproved;
    // A connection change belongs to the session making it, so another tab's login never blocks this one.
    private bool changingSession { get => activeSession.Changing; set => activeSession.Changing = value; }
    private string SourceLabel => engine is OdbcSession session ? session.Platform.Tag() : "SQL";
    // Fixtures without a connection count as ASE, the platform the catalog, explorer and object actions speak.
    private static ServerPlatform PlatformOf(SessionWorkspace owner) => owner.Engine is OdbcSession session ? session.Platform : (owner.Engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase;
    private ServerPlatform CurrentPlatform => PlatformOf(activeSession);
    private const string DatabasesSql = "select name from master..sysdatabases order by name";
    // Database names of the current session's server, read once on demand; a new session starts without them.
    private List<string>? databaseNames { get => activeSession.Databases; set => activeSession.Databases = value; }

    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (!SessionNavigationAllowed()) return;
        var loginProfile = lastProfile;
        if (engine is not DisconnectedQueryEngine && AddSession() == null) return;
        var dialog = new ConnectionWindow(lastProfile ?? loginProfile) { Owner = this };
        changingSession = true; RefreshConnection();
        bool? accepted;
        try { accepted = dialog.ShowDialog(); lastProfile = dialog.Profile ?? lastProfile; }
        finally { changingSession = false; RefreshConnection(); }
        if (accepted == true && dialog.TakeConnection() is { } connection)
        {
            ActivateConnection(connection.Session,connection.Selection,connection.Session.LoginMessages,connection.TimingText);
        }
    }

    private void ActivateConnection(IQueryEngine session,LoginSelection selection,IReadOnlyList<QueryMessage> loginMessages,string? timing=null)
    {
        // Adopt the resource before UI work, and consume the validated snapshot
        // captured before opening ODBC rather than re-reading a closed dialog.
        // Connection adoption is allowed only in the offline workspace selected by ConnectClick.
        // SET options belonged to the session that is being replaced; the new one starts at the server's defaults.
        engine=session;lastProfile=selection.Profile;aseOptions=AseSessionOptions.Default;
        BrowserTree.Items.Clear();databaseNames=null;ClearResults(true);resultCounter=0;ClearMessages();
        AddMessage(0,"CLIENT","Connected · "+engine.Name+(timing==null?"":" · "+timing));
        foreach(var message in loginMessages)AddMessage(message.Number,message.Source,message.Text,message.RawText);
        RefreshConnection();Workspace.SelectedItem=QueryTab;Editor.Focus();
    }

    private async void DisconnectClick(object sender, RoutedEventArgs e) => await DisconnectSessionAsync();

    private async Task DisconnectSessionAsync()
    {
        var owner = activeSession;
        if (owner.Running != null || owner.Changing || owner.RepeatBusy || owner.CatalogActions != 0 || owner.Engine is DisconnectedQueryEngine) return;
        var session = owner.Engine;
        StopRepeat(owner, "Stopped: disconnecting.");
        owner.Changing = true; RefreshFor(owner);
        try
        {
            if (session is IDisposable disposable) await Task.Run(disposable.Dispose);
            owner.Engine = new DisconnectedQueryEngine(); owner.Databases = null; owner.Options = AseSessionOptions.Default;
            if (IsOnScreen(owner)) BrowserTree.Items.Clear(); else owner.Catalog.Clear();
            ClearResults(owner, true);
            ClearMessages(owner);
            AddMessage(owner, 0, "CLIENT", "Session closed · " + session.Name);
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { AddMessage(owner, 1, "CLIENT", "Session closure was not confirmed: " + ex.Message); }
        finally { owner.Changing = false; RefreshFor(owner); }
    }

    private void RefreshConnection()
    {
        CloseEditorCompletion();
        bool ownsSession = engine is OdbcSession;
        bool live = engine is OdbcSession { IsOpen: true };
        CancelButton.IsEnabled = running != null;
        ConnectMenu.IsEnabled = !changingSession;
        bool offline = engine is DisconnectedQueryEngine;
        ExecuteButton.IsEnabled = running == null && !changingSession && repeat == null && !offline;
        ExecuteButton.ToolTip = offline ? "Connect to SAP ASE, IQ or Replication Server to execute SQL" : "Execute selection or script · F9";
        OfflineBanner.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        DisconnectMenu.IsEnabled = ownsSession && running == null && !changingSession;
        // An interrupted ODBC session no longer vouches for its facts; fixtures without a connection report their own.
        SessionFacts? facts = engine is ISessionFacts source && (live || engine is not OdbcSession) ? source.Facts : null;
        SessionFactsPanel.Visibility = facts != null ? Visibility.Visible : Visibility.Collapsed;
        if (facts != null)
        {
            DatabaseText.Text = facts.Database;
            // Only ASE switches databases with USE; IQ shows its database, Replication Server has none.
            DatabaseButton.Visibility = facts.Database.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            DatabaseButton.IsEnabled = running == null && !changingSession && facts.Platform == ServerPlatform.Ase;
            DatabaseButton.ToolTip = "Current database · " + facts.Database + (facts.Platform == ServerPlatform.Ase ? " · click to switch" : "");
            SpidButton.Visibility = facts.Spid != null ? Visibility.Visible : Visibility.Collapsed;
            SpidText.Text = facts.Spid?.ToString(CultureInfo.InvariantCulture) ?? "";
            LoginText.Text = facts.Login ?? "";
            LoginText.Visibility = string.IsNullOrEmpty(facts.Login) ? Visibility.Collapsed : Visibility.Visible;
            CharsetText.Text = facts.Charset ?? lastProfile?.EffectiveCharset ?? "";
            CharsetText.ToolTip = facts.Charset != null ? "Client character set reported by the server" : "Requested client character set (the server did not report one)";
        }
        ConnectionStatus.Text = (live ? SourceLabel : ownsSession ? SourceLabel + " · session interrupted" : facts != null ? SourceLabel : "Disconnected") +
            (extraTargets.Count > 0 ? " · +" + Counts.Of(extraTargets.Count, "server") : "");
        ConnectionStatus.ToolTip = extraTargets.Count > 0 ? "Sessions for multi-server runs: " + string.Join(", ", extraTargets.Select(t => t.Label)) : null;
        RunMenuButton.IsEnabled = !changingSession;
        RefreshAseOptions();
        foreach (var workspace in sessions) RefreshSessionHeader(workspace);
        BrowserConnection.Text = live ? engine.Name : "No connection";
        UpdateTitle();
    }

    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); StatusText.Text = "Copied · " + text; }
        catch (System.Runtime.InteropServices.ExternalException ex) { ShowError("Unable to copy: " + ex.Message); }
    }

    private void SpidClick(object sender, RoutedEventArgs e) { if (engine is ISessionFacts { Facts.Spid: int spid }) CopyText(spid.ToString(CultureInfo.InvariantCulture)); }

    private static void RememberDatabases(SessionWorkspace owner, QueryOutput output)
    {
        if (output.Tables.Count == 0 || output.Tables[0].Columns.Count < 1) return;
        owner.Databases = output.Tables[0].Rows.Cast<DataRow>().Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture)?.Trim() ?? "").Where(name => name.Length != 0).ToList();
    }

    private void DatabaseClick(object sender, RoutedEventArgs e) => RunCatalogAction(async owner => {
        if (owner.Engine is not OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase }) { SetStatus(owner, "Switching databases applies to a connected SAP ASE session."); return; }
        if (owner.Databases == null && await ReadCatalog(owner, DatabasesSql, "Databases") is { } output) RememberDatabases(owner, output);
        if (owner.Databases == null) return;
        // The menu hangs off the status bar chip, so it opens only while this session is still the one on screen.
        if (!IsOnScreen(owner)) { SetStatus(owner, "Database list read · open this session's tab to switch database"); return; }
        CreateDatabaseMenu(owner).IsOpen = true;
    }, "Databases");

    private ContextMenu CreateDatabaseMenu(SessionWorkspace owner)
    {
        string current = (owner.Engine as ISessionFacts)?.Facts.Database ?? "";
        var menu = new ContextMenu { PlacementTarget = DatabaseButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (string name in owner.Databases ?? [])
        {
            var item = new MenuItem { Header = new TextBlock { Text = name }, IsChecked = name.Equals(current, StringComparison.Ordinal) };
            // USE is sent to the session that listed these databases, never to whichever tab is on screen by then.
            item.Click += (_, _) => RunCatalogAction(async target => { if (ReferenceEquals(target, owner)) await UseDatabase(owner, name); }, "Switch database");
            menu.Items.Add(item);
        }
        if (owner.Databases is not { Count: > 0 }) menu.Items.Add(new MenuItem { Header = "No databases returned", IsEnabled = false });
        menu.Items.Add(new Separator());
        var reload = new MenuItem { Header = "Reload list" };
        reload.Click += (_, _) => { owner.Databases = null; if (IsOnScreen(owner)) DatabaseClick(this, new RoutedEventArgs()); };
        menu.Items.Add(reload);
        return menu;
    }

    private async Task UseDatabase(SessionWorkspace owner, string name)
    {
        var output = await ReadCatalog(owner, "use " + UseTarget(name), "Switch database");
        if (output == null || output.Error != null) return;
        string reached = (owner.Engine as ISessionFacts)?.Facts.Database ?? name;
        AddMessage(owner, 0, "CLIENT", "Current database · " + reached);
        SetStatus(owner, "Database · " + reached);
        if (IsOnScreen(owner)) Editor.Focus();
    }
    // A regular identifier is sent as typed; anything else is delimited so USE never splices raw names into SQL.
    public static string UseTarget(string name) =>
        Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_@#$]{0,254}\z") ? name : ResultFormats.Identifier(name);

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeApproved) return;
        foreach (var workspace in sessions.ToArray()) StopRepeat(workspace, "Stopped: closing DBMON SQL.");
        if (sessions.Where(session => session.Running != null).ToArray() is { Length: > 0 } pending)
        { foreach (var session in pending) _ = session.Running!.CancelAsync(); e.Cancel = true; return; }
        if (sessions.Any(session => session.Changing || session.CatalogActions != 0)) { e.Cancel = true; return; }
        var original = activeSession;
        foreach (var workspace in sessions.ToArray())
        {
            SelectSession(workspace);
            if (!ConfirmDiscard()) { e.Cancel = true; SelectSession(original); return; }
        }
        SelectSession(original);
        // The current session and every multi-server session are owned here and closed before the window goes.
        var owned = extraTargets.Select(t => t.Engine).Concat(sessions.Select(s => s.Engine)).OfType<IDisposable>().Distinct(ReferenceEqualityComparer.Instance).Cast<IDisposable>().ToArray();
        if (owned.Length == 0) return;
        e.Cancel = true; changingSession = true; IsEnabled = false;
        try { await Task.Run(() => { foreach (var session in owned) session.Dispose(); }); extraTargets.Clear(); closeApproved = true; Close(); }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { AddMessage(1, "CLIENT", "Closure was not confirmed: " + ex.Message); }
        finally { changingSession = false; IsEnabled = true; }
    }
}
