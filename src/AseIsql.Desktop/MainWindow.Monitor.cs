using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // A modeless panel owns no connection. All commands use its original workspace's serialized execution lane.
    private static bool MonitorConnectionCurrent(SessionWorkspace owner, IQueryEngine source, ServerPlatform platform) =>
        ReferenceEquals(owner.Engine, source) && source is OdbcSession { IsOpen: true } && PlatformOf(owner) == platform;

    private Window? OpenMonitor(ServerPlatform platform, string title, IReadOnlyList<(string Label, string Sql)> commands,
        Func<int, DataTable, IReadOnlyList<(string Label, DataTable Table)>> parse,
        Action<Window, StackPanel, SessionWorkspace, IQueryEngine>? addActions = null)
    {
        var owner = activeSession; var source = owner.Engine;
        if (!MonitorConnectionCurrent(owner, source, platform)) {
            SetStatus(owner, "Connect to " + platform.Tag() + " to open " + title + "."); return null;
        }
        var window = new Window { Owner = this, Title = title + " · " + source.Name, Width = 1080, Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(StyleProperty, typeof(Window));
        var body = new DockPanel { Margin = new Thickness(12) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var refresh = new Button { Content = "Refresh selected tab" }; toolbar.Children.Add(refresh);
        DockPanel.SetDock(toolbar, Dock.Top); body.Children.Add(toolbar);
        var notice = new TextBlock { Text = "Select a tab to load its read-only snapshot.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 0) };
        notice.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(notice, Dock.Bottom); body.Children.Add(notice);
        var tabs = new TabControl(); body.Children.Add(tabs); window.Content = body;
        bool loading = false, closed = false;
        var loaded = new HashSet<int>();
        foreach (var (label, sql) in commands) tabs.Items.Add(new TabItem { Header = label, ToolTip = sql, Content = new TextBlock { Text = "Select this tab or Refresh to load a snapshot.", Margin = new Thickness(12) } });
        addActions?.Invoke(window, toolbar, owner, source);
        async Task Refresh(bool force)
        {
            int index = tabs.SelectedIndex;
            if (closed || loading || index < 0 || !force && loaded.Contains(index)) return;
            var tab = (TabItem)tabs.Items[index];
            if (!sessions.Contains(owner) || !MonitorConnectionCurrent(owner, source, platform)) {
                notice.Text = "The original connection is closed or changed. Reopen the panel from the intended session.";
                return;
            }
            if (owner.Busy) { notice.Text = "Wait: this session is busy (" + owner.BusyReason + "). Refresh to retry."; return; }
            loaded.Remove(index);
            loading = true; refresh.IsEnabled = false; owner.CatalogActions++; RefreshFor(owner);
            tab.Content = new TextBlock { Text = "Loading…", Margin = new Thickness(12) };
            notice.Text = commands[index].Sql + " · " + source.Name;
            try {
                var output = await ReadCatalog(owner, commands[index].Sql, title);
                if (closed || !MonitorConnectionCurrent(owner, source, platform)) return;
                if (output == null) { tab.Content = new TextBlock { Text = "Unavailable · see this session's Messages; refresh to retry.", Margin = new Thickness(12) }; return; }
                var snapshots = new List<(string Label, DataTable Table)>(); string? parsingError = null;
                try {
                    for (int result = 0; result < output.Tables.Count; result++) {
                        foreach (var parsed in parse(index, output.Tables[result]))
                            snapshots.Add((output.Tables.Count > 1 ? (result + 1).ToString(CultureInfo.InvariantCulture) + " · " + parsed.Label : parsed.Label, parsed.Table));
                    }
                } catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or InvalidOperationException) {
                    parsingError = ex.Message; snapshots.Clear();
                    for (int result = 0; result < output.Tables.Count; result++) snapshots.Add(("Raw result " + (result + 1).ToString(CultureInfo.InvariantCulture), output.Tables[result]));
                }
                var results = new TabControl();
                foreach (var snapshot in snapshots) results.Items.Add(new TabItem { Header = snapshot.Label, Content = MonitorGrid(snapshot.Table) });
                string messages = string.Join(Environment.NewLine, output.Messages.Select(m => m.Source + ": " + m.Text));
                if (messages.Length > 0) results.Items.Add(new TabItem { Header = "Server messages", Content = new TextBox {
                    Text = messages, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
                tab.Content = results.Items.Count == 0 ? new TextBlock { Text = "The command returned no visible rows or messages.", Margin = new Thickness(12) } : results;
                bool partial = output.Error != null || output.OmittedMessages != 0 || parsingError != null;
                notice.Text = (partial ? "Partial / metrics unavailable · " + (output.Error ?? parsingError ?? "server messages omitted") + " · " : "Snapshot · ") +
                    DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " · " + commands[index].Sql + " · " + source.Name + " · NULL metrics are unknown";
                if (!partial) loaded.Add(index); else loaded.Remove(index);
                // Switching tabs while the read runs never publishes into the new selection.
            } catch (Exception ex) when (ex is OdbcException or InvalidOperationException or ArgumentException or FormatException or OverflowException or InvalidCastException) {
                notice.Text = "Refresh failed · " + ex.Message;
                tab.Content = new TextBlock { Text = notice.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) }; loaded.Remove(index);
            } finally { loading = false; owner.CatalogActions--; RefreshFor(owner); refresh.IsEnabled = true; }
            if (!closed && tabs.SelectedIndex != index) await Refresh(force:false);
        }
        refresh.Click += async (_, _) => await Refresh(force:true);
        tabs.SelectionChanged += async (_, args) => { if (args.Source == tabs) await Refresh(force:false); };
        window.Closed += (_, _) => closed = true;
        window.Loaded += async (_, _) => { tabs.SelectedIndex = 0; await Refresh(force:false); };
        window.Show(); return window;
    }
    private DataGrid MonitorGrid(DataTable table)
    {
        var grid = new DataGrid { ItemsSource = table.DefaultView, IsReadOnly = true, AutoGenerateColumns = false,
            CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true, RowHeight = double.NaN };
        grid.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
        foreach (DataColumn column in table.Columns)
            grid.Columns.Add(new DataGridTextColumn { Header = string.IsNullOrWhiteSpace(column.Caption) ? UnnamedColumn : column.Caption,
                SortMemberPath = column.ColumnName, Binding = new Binding($"[{column.Ordinal}]") { Converter = NullDisplayConverter.Instance },
                MinWidth = 60, Width = new DataGridLength(FitColumnWidth(table, column.Ordinal, column.Caption)) });
        if (table.Columns.Contains("Needs attention")) {
            var style = new Style(typeof(DataGridRow), TryFindResource(typeof(DataGridRow)) as Style);
            var warning = new DataTrigger { Binding = new Binding("[Needs attention]"), Value = true };
            warning.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("NoticeInk")));
            warning.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Notice")));
            style.Triggers.Add(warning); grid.RowStyle = style;
        }
        return grid;
    }
}
