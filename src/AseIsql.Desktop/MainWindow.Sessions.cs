using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AseIsql.Odbc;
using ICSharpCode.AvalonEdit.Document;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Resource ownership is per workspace. Only the active workspace is mounted in the existing shell.
    // Keep real documents, tabs and tree nodes: rebuilding them would lose undo, grid state and catalog paging.
    private sealed class SessionWorkspace
    {
        public IQueryEngine Engine = new DisconnectedQueryEngine();
        public ConnectionProfile? Profile;
        public TextDocument Document = new();
        public string? Path;
        public string? EditorLabel;
        public string SavedText = "";
        public int Caret, SelectionStart, SelectionLength;
        public double VerticalOffset, HorizontalOffset;
        public readonly List<TextAnchor> Bookmarks = [];
        public readonly ObservableCollection<LogMessage> Messages = [];
        public readonly HeadTailBuffer<LogMessage> MessageBuffer = new();
        public readonly List<TabItem> Tabs = [];
        public TabItem? SelectedTab;
        public readonly HashSet<DataTable> Pins = [];
        public readonly Dictionary<DataTable, (string Sql, string Source)> Origins = [];
        public readonly Dictionary<DataTable, ResultRun> Runs = [];
        public readonly Dictionary<DataTable, (IReadOnlyList<(string Server, DataTable Table)> Parts, IReadOnlyList<int> Keys, int Number)> Diffs = [];
        public int RunCounter, ResultCounter, LastRunId;
        public string ExecutingSql = "";
        public readonly List<TreeViewItem> Catalog = [];
        public string Filter = "", Search = "", Status = "Disconnected · connect with F11 to execute SQL";
        public List<string>? Databases;
        public AseSessionOptions Options = AseSessionOptions.Default;
        public readonly Dictionary<string, string> Parameters = new(StringComparer.OrdinalIgnoreCase);
        public RepeatPlan? Repeat;
        public bool LastRunFailed;
        public DataTable? RepeatMdaPrevious;
        // Execution belongs to the session, so one tab's query never holds the others. Only the active workspace is
        // mounted in the shell: a background run publishes into these fields and is mounted when its tab comes back.
        public CancellationTokenSource? Running;
        public bool Changing, RepeatBusy;
        public int CatalogActions;
        public bool Busy => Running != null || Changing || CatalogActions != 0 || Repeat != null || RepeatBusy;
        public string BusyReason => Running != null ? "a query is running" : Repeat != null || RepeatBusy ? "a repeat is active" :
            CatalogActions != 0 ? "a catalog request is publishing" : "a connection change is in progress";
        public TabItem Header = null!;
        public TextBlock Label = null!;
        public System.Windows.Shapes.Ellipse Dot = null!;
        public Border ProdBadge = null!;
    }

    private SessionWorkspace activeSession = new();
    private readonly List<SessionWorkspace> sessions = [];
    private bool mountingSession;
    // ReadCatalog's caller can still be publishing after its running CTS has been released.
    private int catalogActions { get => activeSession.CatalogActions; set => activeSession.CatalogActions = value; }

    // Switching and adding session tabs stay available while other sessions work. Only a connection change already in
    // flight on this session holds the strip, because the login modal owns the window for as long as it is open.
    private bool SessionNavigationAllowed()
    {
        if (changingSession)
        { StatusText.Text = "Wait for this session's connection change to finish."; return false; }
        return true;
    }

    private void InitializeSessions()
    {
        sessions.Add(activeSession);
        AddSessionHeader(activeSession);
        SaveSessionView();
        SessionTabs.SelectedItem = activeSession.Header;
    }

    private void AddSessionHeader(SessionWorkspace workspace)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        workspace.Dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(workspace.Dot);
        // PROD tag: ErrorInk on Surface reads on light and dark palettes alike (the contrast check covers the pair).
        var prodText = new TextBlock { Text = "PROD", FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        prodText.SetResourceReference(TextBlock.ForegroundProperty, "Surface"); prodText.SetResourceReference(TextBlock.FontSizeProperty, "SmallFontSize");
        workspace.ProdBadge = new Border { Child = prodText, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        workspace.ProdBadge.SetResourceReference(Border.BackgroundProperty, "ErrorInk"); workspace.ProdBadge.SetResourceReference(Border.CornerRadiusProperty, "ControlRadius");
        panel.Children.Add(workspace.ProdBadge);
        workspace.Label =new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        panel.Children.Add(workspace.Label);
        var close = new Button { Content = "×", ToolTip = "Close session", Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(8, 0, 0, 0), MinHeight = 20, MinWidth = 20 };
        close.Click += async (_, e) => { e.Handled = true; await CloseSessionAsync(workspace); };
        panel.Children.Add(close);
        workspace.Header = new TabItem { Header = panel, Tag = workspace };
        workspace.Header.SetResourceReference(StyleProperty, "SessionWorkspaceTab");
        workspace.Header.ContextMenu = CreateSessionMenu(workspace);
        SessionTabs.Items.Add(workspace.Header);
        RefreshSessionHeader(workspace);
    }

    // The connection actions live on each session tab and act on that session, not on whichever tab is on screen.
    // Right-clicking a tab does not select it; the state is read again every time the menu opens.
    private ContextMenu CreateSessionMenu(SessionWorkspace workspace)
    {
        var menu = new ContextMenu();
        var connect = new MenuItem { Header = "Connect…", InputGestureText = "F11" };
        connect.Click += (_, _) => ConnectSession(workspace);
        var disconnect = new MenuItem { Header = "Disconnect", InputGestureText = "F12" };
        disconnect.Click += async (_, _) => { if (CanDisconnect(workspace)) await DisconnectSessionAsync(workspace); };
        var duplicate = new MenuItem { Header = "Duplicate session" };
        duplicate.Click += async (_, _) => await DuplicateSessionAsync(workspace);
        var close = new MenuItem { Header = "Close session" };
        close.Click += async (_, _) => await CloseSessionAsync(workspace);
        menu.Items.Add(connect); menu.Items.Add(disconnect); menu.Items.Add(duplicate); menu.Items.Add(new Separator()); menu.Items.Add(close);
        menu.Opened += (_, _) =>
        {
            connect.IsEnabled = CanConnect(workspace);
            disconnect.IsEnabled = CanDisconnect(workspace);
            // A new connection does not need the source to be idle, only open and not mid connection change.
            duplicate.IsEnabled = workspace.Engine is OdbcSession { IsOpen: true } && !workspace.Changing && !activeSession.Changing;
            close.IsEnabled = !workspace.Busy && !activeSession.Changing;
        };
        return menu;
    }

    private static void RefreshSessionHeader(SessionWorkspace workspace)
    {
        if (workspace.Label == null) return;
        string title = workspace.Engine is DisconnectedQueryEngine ? workspace.EditorLabel ?? "Not connected" :
            (workspace.Profile is { } profile ? ProfileLabel(profile) : workspace.Engine.Name) + " · " +
            (workspace.Engine is AseIsql.Odbc.OdbcSession odbc ? odbc.Platform :
                (workspace.Engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase).Tag();
        // A tab the user is not looking at still says it is working, so a background run is never invisible.
        bool working = workspace.Running != null || workspace.Repeat != null;
        workspace.Label.Text = working ? "▶ " + title : title;
        workspace.Dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, workspace.Engine is DisconnectedQueryEngine ? "Muted" :
            workspace.Engine is AseIsql.Odbc.OdbcSession { IsOpen: false } ? "ErrorInk" : "Accent");
        bool prod = IsProduction(workspace);
        workspace.ProdBadge.Visibility = prod ? Visibility.Visible : Visibility.Collapsed;
        SetIsProduction(workspace.Header, prod);
        workspace.Header.ToolTip = (prod ? "PRODUCTION · SQL that changes state asks for confirmation\n" : "") + title + (workspace.Engine is DisconnectedQueryEngine ? "" : "\n" + workspace.Engine.Name);
        if (workspace.Busy) workspace.Header.ToolTip = (string)workspace.Header.ToolTip! + Environment.NewLine + workspace.BusyReason;
    }

    private void SaveSessionView()
    {
        var state = activeSession;
        state.Document = Editor.Document;
        state.Caret = Editor.CaretOffset;
        state.SelectionStart = Editor.SelectionStart; state.SelectionLength = Editor.SelectionLength;
        state.VerticalOffset = Editor.VerticalOffset; state.HorizontalOffset = Editor.HorizontalOffset;
        state.SelectedTab = Workspace.SelectedItem as TabItem;
        state.Catalog.Clear(); state.Catalog.AddRange(BrowserTree.Items.OfType<TreeViewItem>());
        state.Search = BrowserSearch.Text; state.Status = StatusText.Text;
        RefreshSessionHeader(state);
    }

    private bool SelectSession(SessionWorkspace workspace, bool closing = false)
    {
        if (ReferenceEquals(activeSession, workspace)) return true;
        if (!closing && !SessionNavigationAllowed()) return false;
        SaveSessionView();
        mountingSession = true;
        try
        {
            foreach (var tab in ResultTabs.ToArray()) Workspace.Items.Remove(tab);
            BrowserTree.Items.Clear();
            activeSession = workspace;
            Editor.Document = workspace.Document;
            Editor.Select(workspace.SelectionStart, workspace.SelectionLength);
            Editor.CaretOffset = workspace.Caret;
            Editor.ScrollToVerticalOffset(workspace.VerticalOffset); Editor.ScrollToHorizontalOffset(workspace.HorizontalOffset);
            foreach (var tab in workspace.Tabs) Workspace.Items.Add(tab);
            foreach (var node in workspace.Catalog) BrowserTree.Items.Add(node);
            BrowserSearch.Text = workspace.Search;
            Messages.ItemsSource = log; LogCount.Text = Counts.Of(log.Count, "message");
            SetLogVisible(logVisible);
            Workspace.SelectedItem = workspace.SelectedTab != null && Workspace.Items.Contains(workspace.SelectedTab) ? workspace.SelectedTab : QueryTab;
            RefreshRunText();
            SessionTabs.SelectedItem = workspace.Header;
            SyncRepeatBanner();
            RefreshConnection(); StatusText.Text = workspace.Status;
        }
        finally { mountingSession = false; }
        if (Workspace.SelectedItem == QueryTab) Editor.TextArea.Focus();
        return true;
    }

    private void SessionTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (mountingSession || e.Source != SessionTabs || SessionTabs.SelectedItem is not TabItem { Tag: SessionWorkspace next }) return;
        if (!SelectSession(next))
        {
            mountingSession = true; SessionTabs.SelectedItem = activeSession.Header; mountingSession = false;
        }
    }

    private SessionWorkspace? AddSession()
    {
        if (!SessionNavigationAllowed()) return null;
        var workspace = new SessionWorkspace();
        sessions.Add(workspace); AddSessionHeader(workspace); SelectSession(workspace);
        return workspace;
    }

    private Task DuplicateSessionAsync(SessionWorkspace source) =>
        source.Engine is OdbcSession { IsOpen: true } odbc ? OpenSiblingTabAsync(odbc, source, false) : Task.CompletedTask;

    // Opens a second connection like `source` (same profile and credentials, held in memory) off the UI thread and adds it
    // as a new session tab right after `after` (or at the end), selected. PROD follows the profile; the monitor adds its own.
    private async Task OpenSiblingTabAsync(OdbcSession source, SessionWorkspace? after, bool production)
    {
        if (!SessionNavigationAllowed()) return;
        OdbcSession sibling;
        try { sibling = await Task.Run(source.OpenSibling); }
        catch (Exception ex) when (ex is System.Data.Odbc.OdbcException or ArgumentException or InvalidOperationException or System.IO.IOException)
        { ShowMessage("Duplicate session", "The session could not be duplicated.\n" + source.DescribeFailure(ex)); return; }
        var profile = production && !sibling.Profile.Production ? sibling.Profile with { Production = true } : sibling.Profile;
        if (!SessionNavigationAllowed()) { sibling.Dispose(); return; }
        var workspace = new SessionWorkspace();
        int at = after == null ? sessions.Count : sessions.IndexOf(after) + 1;
        if (at <= 0 && after != null) at = sessions.Count;
        sessions.Insert(at, workspace); AddSessionHeader(workspace);
        SessionTabs.Items.Remove(workspace.Header); SessionTabs.Items.Insert(at, workspace.Header);
        if (!SelectSession(workspace)) { sessions.Remove(workspace); SessionTabs.Items.Remove(workspace.Header); sibling.Dispose(); return; }
        ActivateConnection(sibling, new LoginSelection(profile), sibling.LoginMessages, null, sibling.OmittedLoginMessages);
    }

    // Activity monitor entry: the monitor's own dedicated session is the template, the new tab goes after the active one.
    private void DuplicateFromMonitor(OdbcSession monitorSession, bool production)
    {
        _ = OpenSiblingTabAsync(monitorSession, activeSession, production);
        Activate();
    }

    private void AddSessionClick(object sender, RoutedEventArgs e)
    {
        if (AddSession() != null) ConnectClick(sender, e);
    }

    private async Task CloseSessionAsync(SessionWorkspace workspace)
    {
        if (workspace.Busy)
        { StatusText.Text = "This session is busy: " + workspace.BusyReason + ". Stop it before closing the tab."; return; }
        if (!SessionNavigationAllowed() || !SelectSession(workspace) || !ConfirmDiscard()) return;
        workspace.Changing = true;
        RefreshConnection();
        try
        {
            if (workspace.Engine is IDisposable disposable) await Task.Run(disposable.Dispose);
            // The last close still leaves one usable offline workspace.
            var next = sessions.FirstOrDefault(s => s != workspace);
            if (next == null) { next = new SessionWorkspace(); sessions.Add(next); AddSessionHeader(next); }
            SelectSession(next, closing: true);
            sessions.Remove(workspace); SessionTabs.Items.Remove(workspace.Header);
        }
        catch (Exception ex) when (ex is System.Data.Odbc.OdbcException or InvalidOperationException)
        { AddMessage(1, "CLIENT", "Session closure was not confirmed: " + ex.Message); }
        finally { workspace.Changing = false; RefreshConnection(); }
    }
}
