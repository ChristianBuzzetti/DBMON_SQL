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
    private ConnectionProfile? lastProfile;
    private bool changingSession, closeApproved;
    private string SourceLabel => engine is OdbcSession session ? session.Platform.Tag() : "SQL";
    // Fixtures without a connection count as ASE, the platform the catalog, explorer and object actions speak.
    private ServerPlatform CurrentPlatform => engine is OdbcSession session ? session.Platform : (engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase;
    private const string DatabasesSql = "select name from master..sysdatabases order by name";
    // Database names of the current session's server, read once on demand; a new session starts without them.
    private List<string>? databaseNames;

    private void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (running != null || changingSession) return;
        if (engine is OdbcSession) { StatusText.Text = "Disconnect the current session before switching servers."; return; }
        var dialog = new ConnectionWindow(lastProfile) { Owner = this };
        bool? accepted = dialog.ShowDialog(); lastProfile = dialog.Profile ?? lastProfile;
        if (accepted == true && dialog.TakeConnection() is { } connection)
        {
            ActivateConnection(connection.Session,connection.Selection,connection.Session.LoginMessages,connection.TimingText);
        }
    }

    private void SessionChipClick(object sender, RoutedEventArgs e)
    {
        if (engine is not OdbcSession) { ConnectClick(sender, e); return; }
        var menu = new ContextMenu { PlacementTarget = ConnectButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Item(string header, string? gesture, RoutedEventHandler click, bool enabled = true) { var item = new MenuItem { Header = header, InputGestureText = gesture ?? "", IsEnabled = enabled }; item.Click += click; menu.Items.Add(item); }
        Item("Copy server · database", null, (_, _) => CopyText(engine.Name));
        if (engine is ISessionFacts { Facts.Spid: int spid }) Item("Copy SPID · " + spid.ToString(CultureInfo.InvariantCulture), null, (_, _) => CopyText(spid.ToString(CultureInfo.InvariantCulture)));
        if (CurrentPlatform == ServerPlatform.Ase) Item("Switch database…", null, (_, _) => DatabaseClick(this, e), running == null && !changingSession);
        if (CurrentPlatform != ServerPlatform.Rs) Item("Object explorer", "F4", (_, _) => BrowserClick(this, e));
        menu.Items.Add(new Separator());
        Item("Disconnect", "F12", (_, _) => DisconnectClick(this, e), running == null && !changingSession);
        menu.IsOpen = true;
    }
    private void ActivateConnection(IQueryEngine session,LoginSelection selection,IReadOnlyList<QueryMessage> loginMessages,string? timing=null)
    {
        // Adopt the resource before UI work, and consume the validated snapshot
        // captured before opening ODBC rather than re-reading a closed dialog.
        StopRepeat("Stopped: a new session replaced the one it ran on.");
        // SET options belonged to the session that is being replaced; the new one starts at the server's defaults.
        engine=session;lastProfile=selection.Profile;aseOptions=AseSessionOptions.Default;
        BrowserTree.Items.Clear();databaseNames=null;ClearResults(true);resultCounter=0;ClearMessages();
        AddMessage(0,"CLIENT","Connected · "+engine.Name+(timing==null?"":" · "+timing));
        foreach(var message in loginMessages)AddMessage(message.Number,message.Source,message.Text,message.RawText);
        RefreshConnection();Workspace.SelectedItem=QueryTab;Editor.Focus();
    }

    private async void DisconnectClick(object sender, RoutedEventArgs e)
    {
        if (running != null || changingSession || engine is not OdbcSession session) return;
        StopRepeat("Stopped: disconnecting.");
        changingSession = true; ExecuteButton.IsEnabled = ConnectButton.IsEnabled = false;
        try
        {
            await Task.Run(session.Dispose);
            engine = new DisconnectedQueryEngine();  BrowserTree.Items.Clear(); databaseNames = null; aseOptions = AseSessionOptions.Default;
            ClearResults(true);
            ClearMessages();
            AddMessage(0, "CLIENT", session.Platform.Title() + " session closed.");
        }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { AddMessage(1, "CLIENT", "Session closure was not confirmed: " + ex.Message); }
        finally { changingSession = false; ExecuteButton.IsEnabled = true; RefreshConnection(); }
    }

    private void RefreshConnection()
    {
        bool ownsSession = engine is OdbcSession;
        bool live = engine is OdbcSession { IsOpen: true };
        ConnectMenu.IsEnabled = !ownsSession && running == null && !changingSession;
        bool offline = engine is DisconnectedQueryEngine;
        ExecuteButton.IsEnabled = running == null && !changingSession && !offline;
        ExecuteButton.ToolTip = offline ? "Connect to SAP ASE, IQ or Replication Server to execute SQL" : "Execute selection or script · F9";
        OfflineBanner.Visibility = offline ? Visibility.Visible : Visibility.Collapsed;
        DisconnectMenu.IsEnabled = ownsSession && running == null && !changingSession;
        // The chip stays usable while a session exists: it is the session's menu, not only a connect button.
        ConnectButton.IsEnabled = running == null && !changingSession;
        ConnectionBadge.Text = live ? engine.Name : ownsSession ? "Session interrupted · " + engine.Name : "Connect…";
        ConnectionDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Accent" : ownsSession ? "ErrorInk" : "Muted");
        ConnectionChevron.Visibility = ownsSession ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.ToolTip = ownsSession ? "Session · " + engine.Name + " · click for session actions" : "Connect to SAP ASE, IQ or Replication Server · F11";
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
        BrowserConnection.Text = live ? engine.Name : "No connection";
        HeaderSubtitle.Text = "SQL WORKSPACE · " + (live ? CurrentPlatform.Title().ToUpperInvariant() : "SAP ASE · IQ · RS");
        UpdateTitle();
    }

    private void CopyText(string text)
    {
        try { Clipboard.SetText(text); StatusText.Text = "Copied · " + text; }
        catch (System.Runtime.InteropServices.ExternalException ex) { ShowError("Unable to copy: " + ex.Message); }
    }

    private void SpidClick(object sender, RoutedEventArgs e) { if (engine is ISessionFacts { Facts.Spid: int spid }) CopyText(spid.ToString(CultureInfo.InvariantCulture)); }

    private void RememberDatabases(QueryOutput output)
    {
        if (output.Tables.Count == 0 || output.Tables[0].Columns.Count < 1) return;
        databaseNames = output.Tables[0].Rows.Cast<DataRow>().Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture)?.Trim() ?? "").Where(name => name.Length != 0).ToList();
    }

    private void DatabaseClick(object sender, RoutedEventArgs e) => RunCatalogAction(async () => {
        if (engine is not OdbcSession { IsOpen: true, Platform: ServerPlatform.Ase }) { StatusText.Text = "Switching databases applies to a connected SAP ASE session."; return; }
        if (databaseNames == null && await ReadCatalog(DatabasesSql, "Databases") is { } output) RememberDatabases(output);
        if (databaseNames == null) return;
        CreateDatabaseMenu().IsOpen = true;
    }, "Databases");

    private ContextMenu CreateDatabaseMenu()
    {
        string current = (engine as ISessionFacts)?.Facts.Database ?? "";
        var menu = new ContextMenu { PlacementTarget = DatabaseButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        foreach (string name in databaseNames ?? [])
        {
            var item = new MenuItem { Header = new TextBlock { Text = name }, IsChecked = name.Equals(current, StringComparison.Ordinal) };
            item.Click += (_, _) => RunCatalogAction(() => UseDatabase(name), "Switch database");
            menu.Items.Add(item);
        }
        if (databaseNames is not { Count: > 0 }) menu.Items.Add(new MenuItem { Header = "No databases returned", IsEnabled = false });
        menu.Items.Add(new Separator());
        var reload = new MenuItem { Header = "Reload list" };
        reload.Click += (_, _) => { databaseNames = null; DatabaseClick(this, new RoutedEventArgs()); };
        menu.Items.Add(reload);
        return menu;
    }

    private async Task UseDatabase(string name)
    {
        var output = await ReadCatalog("use " + UseTarget(name), "Switch database");
        if (output == null || output.Error != null) return;
        string reached = (engine as ISessionFacts)?.Facts.Database ?? name;
        AddMessage(0, "CLIENT", "Current database · " + reached);
        StatusText.Text = "Database · " + reached;
        Editor.Focus();
    }

    // A regular identifier is sent as typed; anything else is delimited so USE never splices raw names into SQL.
    public static string UseTarget(string name) =>
        Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_@#$]{0,254}\z") ? name : ResultFormats.Identifier(name);

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeApproved) return;
        StopRepeat("Stopped: closing DBMON SQL.");
        if (running != null) { _ = running.CancelAsync(); e.Cancel = true; return; }
        if (changingSession || !ConfirmDiscard()) { e.Cancel = true; return; }
        // The current session and every multi-server session are owned here and closed before the window goes.
        var owned = extraTargets.Select(t => t.Engine).Prepend(engine).OfType<IDisposable>().ToArray();
        if (owned.Length == 0) return;
        e.Cancel = true; changingSession = true; IsEnabled = false;
        try { await Task.Run(() => { foreach (var session in owned) session.Dispose(); }); extraTargets.Clear(); closeApproved = true; Close(); }
        catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { AddMessage(1, "CLIENT", "Closure was not confirmed: " + ex.Message); }
        finally { changingSession = false; IsEnabled = true; }
    }
}
