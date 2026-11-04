using System.ComponentModel;
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void ActivityClick(object sender, RoutedEventArgs e)
    {
        if (CurrentPlatform != ServerPlatform.Ase)
        { ShowMessage("Activity monitor", "Activity monitor is not supported for " + CurrentPlatform.Title() + " yet."); return; }
        // The regular login owns credentials and transfers a newly opened connection. Never borrow an editor engine.
        var sourceProfile = lastProfile;
        var login = new ConnectionWindow(sourceProfile) { Owner = this, Title = "Activity monitor · dedicated connection" };
        if (login.ShowDialog() != true || login.TakeConnection() is not { } connected) return;
        if (connected.Selection.Profile.Platform != ServerPlatform.Ase)
        {
            connected.Session.Dispose();
            ShowMessage("Activity monitor", "Activity monitor is not supported for " + connected.Selection.Profile.Platform.Title() + " yet."); return;
        }
        var profile = connected.Selection.Profile;
        bool SameServer(ConnectionProfile? other) => other != null && other.Platform == profile.Platform &&
            other.Server.Equals(profile.Server, StringComparison.OrdinalIgnoreCase) && other.Port == profile.Port;
        int[] ProtectedSpids() => sessions.Where(s => SameServer(s.Profile)).Select(s => (s.Engine as ISessionFacts)?.Facts.Spid)
            .OfType<int>().ToArray();
        bool production = profile.Production || SameServer(sourceProfile) && sourceProfile?.Production == true;
        var monitor = new ActivityWindow(this, connected.Session, production, ProtectedSpids);
        try { monitor.Show(); }
        catch { connected.Session.Dispose(); throw; }
    }

    // One engine per monitor. UI-thread busy gate serializes timer, selection, and kill work; closing cancels before
    // disposing, after the last worker has returned. No calls reach the editor's session or its run settings.
    private sealed class ActivityWindow : Window
    {
        private readonly MainWindow main;
        private readonly OdbcSession session;
        private readonly bool production;
        private readonly Func<int[]> protectedSpids;
        private readonly CancellationTokenSource cancellation = new();
        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly DataGrid grid = new() { AutoGenerateColumns = false, SelectionUnit = DataGridSelectionUnit.FullRow, SelectionMode = DataGridSelectionMode.Extended };
        private readonly DataGrid locks = new() { AutoGenerateColumns = true, IsReadOnly = true };
        private readonly TextBox sqlText = new() { IsReadOnly = true, AcceptsReturn = true, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBox filter = new() { Width = 260, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Filter SPID, login, host, program, database, status or command; ancestors stay visible" };
        private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock detailHeading = new() { Text = "Select a SPID to inspect its current SQL and locks.", Margin = new Thickness(0, 4, 0, 6), TextWrapping = TextWrapping.Wrap };
        private readonly TabItem sqlTab = new() { Header = "Current SQL" };
        private readonly TabItem locksTab = new() { Header = "Locks" };
        private readonly MenuItem kill = new() { Header = "Kill spid…" };
        private Task cancellationFinished = Task.CompletedTask;
        private IReadOnlyList<BlockingNode> roots = [];
        private bool busy, closed, disposed, updatingRows, detailsPending;
        private bool? statementAvailable, sqlAvailable, locksAvailable;
        private int? ownSpid;
        private string sortProperty = nameof(ActivityProcess.Spid);
        private bool descending;

        private sealed record ActivityRow(BlockingNode Node, int Depth)
        {
            public ActivityProcess Process => Node.Process;
            public bool Head => Node.IsHeadBlocker;
            public bool Cycle => Node.InCycle;
            public string Tree => new string(' ', Math.Min(Depth, 80) * 3) + (Depth > 0 ? "↳ " : "") +
                Process.Spid.ToString(CultureInfo.InvariantCulture) + (Cycle ? " · cycle" : Head ? " · HEAD" : Process.BlockedBy > 0 && Node.Parent == null ? " · blocker absent" : "");
        }

        public ActivityWindow(MainWindow main, OdbcSession session, bool production, Func<int[]> protectedSpids)
        {
            this.main = main; this.session = session; this.production = production; this.protectedSpids = protectedSpids;
            Owner = main; Title = "Activity monitor · " + session.Name + (production ? " · PROD" : "");
            Width = 1260; Height = 780; MinWidth = 800; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(StyleProperty, typeof(Window));
            SourceInitialized += (_, _) => Themes.ApplyChrome(this, Themes.Current);
            var layout = new DockPanel { Margin = new Thickness(14) };
            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(new TextBlock { Text = "Refresh", VerticalAlignment = VerticalAlignment.Center });
            var interval = new ComboBox { ItemsSource = new[] { "2 s", "5 s", "10 s", "30 s" }, SelectedIndex = 1, Width = 80, Margin = new Thickness(8, 0, 8, 0) };
            interval.SelectionChanged += (_, _) => timer.Interval = TimeSpan.FromSeconds(new[] { 2, 5, 10, 30 }[interval.SelectedIndex]);
            toolbar.Children.Add(interval);
            var pause = new CheckBox { Content = "Pause", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            pause.Checked += (_, _) => timer.Stop(); pause.Unchecked += (_, _) => { if (!closed) timer.Start(); };
            toolbar.Children.Add(pause);
            var refresh = new Button { Content = "Refresh now" }; refresh.Click += async (_, _) => await RefreshAsync(); toolbar.Children.Add(refresh);
            var retry = new Button { Content = "Retry MDA", ToolTip = "Check SQL text and lock access again after monitoring permissions or configuration change" };
            retry.Click += async (_, _) => { if (busy || closed) return; statementAvailable = sqlAvailable = locksAvailable = null; await DetailsAsync(); }; toolbar.Children.Add(retry);
            toolbar.Children.Add(new TextBlock { Text = "Filter", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
            filter.TextChanged += (_, _) => ShowRows(); toolbar.Children.Add(filter);
            var copy = new Button { Content = "Copy rows", ToolTip = "Copy selected rows with headers; Ctrl+C does the same" }; copy.Click += (_, _) => CopyRows(); toolbar.Children.Add(copy);
            DockPanel.SetDock(toolbar, Dock.Top); layout.Children.Add(toolbar);
            var hint = new TextBlock { Text = "Blocking tree · HEAD = head blocker · cycle = circular blocking · click column headers to sort within each branch", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); DockPanel.SetDock(hint, Dock.Top); layout.Children.Add(hint);
            if (production)
            {
                var warning = new TextBlock { Text = "⚠ PROD · Killing a process interrupts its work and may trigger a long transaction rollback.", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
                warning.SetResourceReference(TextBlock.ForegroundProperty, "ErrorInk"); DockPanel.SetDock(warning, Dock.Top); layout.Children.Add(warning);
            }
            DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
            var split = new Grid(); split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            AddColumn("SPID / blocking tree", "Tree", nameof(ActivityProcess.Spid), 240);
            foreach (var (header, property) in new[] { ("Status", "Status"), ("Login", "Login"), ("Host", "Host"), ("Program", "Program"), ("Database", "Database"), ("Command", "Command"),
                ("CPU", "Cpu"), ("Physical IO", "PhysicalIo"), ("Memory", "Memory"), ("Blocked by", "BlockedBy"), ("Blocked seconds", "TimeBlocked"), ("Transaction", "Transaction") })
                AddColumn(header, "Process." + property, property, 110);
            var rowStyle = new Style(typeof(DataGridRow));
            var head = new DataTrigger { Binding = new Binding("Head"), Value = true };
            head.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("Notice")));
            head.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("NoticeInk")));
            head.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold)); rowStyle.Triggers.Add(head);
            var cycle = new DataTrigger { Binding = new Binding("Cycle"), Value = true };
            cycle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ErrorInk"))); rowStyle.Triggers.Add(cycle); grid.RowStyle = rowStyle;
            grid.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            grid.Sorting += (_, args) => {
                args.Handled = true;
                descending = sortProperty == args.Column.SortMemberPath && !descending; sortProperty = args.Column.SortMemberPath;
                foreach (var column in grid.Columns) column.SortDirection = null;
                args.Column.SortDirection = descending ? ListSortDirection.Descending : ListSortDirection.Ascending; ShowRows();
            };
            grid.SelectionChanged += async (_, _) => { if (!updatingRows) { ClearDetails(); await DetailsAsync(); } };
            grid.PreviewKeyDown += (_, args) => { if (args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopyRows(); args.Handled = true; } };
            grid.PreviewMouseRightButtonDown += (_, args) => {
                if (args.OriginalSource is DependencyObject source && FindAncestor<DataGridRow>(source) is { Item: ActivityRow row }) grid.SelectedItem = row;
            };
            var menu = new ContextMenu(); var copyItem = new MenuItem { Header = "Copy rows" }; copyItem.Click += (_, _) => CopyRows(); menu.Items.Add(copyItem); menu.Items.Add(kill); grid.ContextMenu = menu;
            menu.Opened += (_, _) => UpdateKill();
            kill.Click += async (_, _) => await KillAsync(); split.Children.Add(grid);
            var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(splitter, 1); split.Children.Add(splitter);
            var detail = new DockPanel(); DockPanel.SetDock(detailHeading, Dock.Top); detail.Children.Add(detailHeading);
            sqlText.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
            sqlTab.Content = sqlText; locksTab.Content = locks;
            var tabs = new TabControl(); tabs.Items.Add(sqlTab); tabs.Items.Add(locksTab); detail.Children.Add(tabs);
            Grid.SetRow(detail, 2); split.Children.Add(detail); layout.Children.Add(split); Content = layout;
            timer.Tick += async (_, _) => await RefreshAsync();
            Loaded += async (_, _) => { timer.Start(); await RefreshAsync(); };
            Closed += async (_, _) => {
                closed = true; timer.Stop();
                // ODBC cancellation callbacks can block; CancelAsync runs them away from the editor's UI thread.
                cancellationFinished = cancellation.CancelAsync();
                if (!busy) await DisposeSessionAsync();
            };
        }

        private ActivityProcess? Selected => (grid.SelectedItem as ActivityRow)?.Process;
        private void UpdateKill()
        {
            string? denied = Selected is { } p ? ActivityMonitor.KillDenied(p, ownSpid, protectedSpids()) : "Select a SPID.";
            kill.IsEnabled = !busy && !closed && denied == null;
            kill.ToolTip = denied ?? (busy ? "Waiting for the monitor request to finish." : "Two confirmations are required.");
        }
        private void AddColumn(string header, string path, string sort, double width) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), SortMemberPath = sort, Width = width });
        private async Task<QueryOutput> QueryAsync(string sql) => await Task.Run(() => session.Execute(sql, cancellation.Token, userRun: false, timeoutSeconds: 10), cancellation.Token);
        private static DataTable Table(QueryOutput output, bool processes = true)
        {
            if (output.Error != null) throw new InvalidOperationException(output.Error);
            return (processes ? ActivityMonitor.PickProcessTable(output.Tables) :
                output.Tables.FirstOrDefault(table => ResultColumns.Find(table, "monitor_spid") != null)) ??
                output.Tables.FirstOrDefault() ?? throw new InvalidOperationException("The server returned no activity result.");
        }
        private async Task DisposeSessionAsync()
        {
            if (disposed) return; disposed = true;
            try { await cancellationFinished; await Task.Run(session.Dispose); }
            catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { /* Window is already closed. */ }
            finally { cancellation.Dispose(); }
        }
        private async Task RunAsync(Func<Task> action)
        {
            if (busy || closed) return; busy = true; UpdateKill();
            try { await action(); }
            catch (OperationCanceledException) { if (!closed) status.Text = "Monitor request cancelled."; }
            catch (Exception ex) when (ex is OdbcException or InvalidOperationException or ArgumentException)
            { if (!closed) { ClearDetails(); status.Text = "Activity monitor: " + ex.Message; } }
            finally
            {
                busy = false;
                if (closed) await DisposeSessionAsync();
                else { UpdateKill(); if (detailsPending) { detailsPending = false; await DetailsAsync(); } }
            }
        }
        private async Task RefreshAsync() => await RunAsync(async () => {
            status.Text = "Refreshing…";
            if (ownSpid == null)
            {
                var identity = Table(await QueryAsync(ActivityMonitor.IdentitySql), processes: false);
                ownSpid = ActivityMonitor.ParseIdentity(identity);
            }
            var processes = ActivityMonitor.Parse(Table(await QueryAsync(ActivityMonitor.ProcessesSql)));
            if (closed) return;
            roots = ActivityMonitor.BuildTree(processes); ShowRows();
            status.Text = $"{DateTime.Now:T} · {Counts.Of(processes.Count, "process")} · monitor SPID {ownSpid?.ToString(CultureInfo.InvariantCulture) ?? "unknown (kill disabled)"} · dedicated connection";
            detailsPending = false; await ReadDetailsAsync();
        });

        private void ShowRows()
        {
            int[] selected = grid.SelectedItems.Cast<ActivityRow>().Select(r => r.Process.Spid).ToArray();
            int? current = Selected?.Spid;
            var property = typeof(ActivityProcess).GetProperty(sortProperty)!;
            int Compare(ActivityProcess a, ActivityProcess b)
            {
                int result = Comparer<object>.Default.Compare(property.GetValue(a), property.GetValue(b));
                if (result == 0) result = a.Spid.CompareTo(b.Spid);
                return descending ? -result : result;
            }
            var rows = ActivityMonitor.Flatten(roots, Compare, filter.Text).Select(r => new ActivityRow(r.Node, r.Depth)).ToList();
            updatingRows = true;
            try
            {
                grid.ItemsSource = rows;
                grid.SelectedItem = rows.FirstOrDefault(r => r.Process.Spid == current);
                foreach (var row in rows.Where(r => selected.Contains(r.Process.Spid))) if (!grid.SelectedItems.Contains(row)) grid.SelectedItems.Add(row);
            }
            finally { updatingRows = false; }
            if (Selected == null) ClearDetails();
        }
        private void ClearDetails()
        {
            sqlText.Text = ""; locks.ItemsSource = null;
            detailHeading.Text = Selected is { } p ? $"SPID {p.Spid} · {p.Login} · {p.Host} · {p.Program}" : "Select a SPID to inspect its current SQL and locks.";
        }
        private async Task DetailsAsync()
        {
            if (busy) { detailsPending = true; return; }
            await RunAsync(ReadDetailsAsync);
        }
        private async Task ReadDetailsAsync()
        {
            ClearDetails();
            if (Selected is not { } process || closed) return;
            int spid = process.Spid;
            bool StillSelected() => !closed && Selected?.Spid == spid;
            if (sqlAvailable != false)
            {
                var output = await QueryAsync(ActivityMonitor.SqlTextSql(spid, statementAvailable != false));
                // monSysStatement is history: a running batch may not have a matching record yet.
                // Never lose available live text just because that optional statement match is empty.
                if (statementAvailable != false && (output.Error != null || output.Tables.Count == 0 || output.Tables[0].Rows.Count == 0))
                { statementAvailable = false; output = await QueryAsync(ActivityMonitor.SqlTextSql(spid, false)); }
                else if (output.Error == null) statementAvailable ??= true;
                sqlAvailable = output.Error == null && output.Tables.Count > 0;
                sqlTab.Visibility = sqlAvailable == true ? Visibility.Visible : Visibility.Collapsed;
                if (StillSelected() && sqlAvailable == true)
                {
                    string text = ActivityMonitor.ParseSqlText(output.Tables[0]);
                    sqlText.Text = text.Length > 0 ? text : "No current SQL text reported for this SPID.";
                }
            }
            if (locksAvailable != false)
            {
                var output = await QueryAsync(ActivityMonitor.LocksSql(spid));
                locksAvailable = output.Error == null && output.Tables.Count > 0;
                locksTab.Visibility = locksAvailable == true ? Visibility.Visible : Visibility.Collapsed;
                if (StillSelected() && locksAvailable == true) locks.ItemsSource = output.Tables[0].DefaultView;
            }
            if (StillSelected()) detailHeading.Text = $"SPID {spid} · {process.Login} · {process.Host} · {process.Program}" +
                (sqlAvailable == false ? " · SQL text unavailable (MDA disabled or not permitted)" : statementAvailable == false ? " · batch SQL (statement MDA unavailable or no matching statement)" : "") +
                (locksAvailable == false ? " · locks unavailable (MDA disabled or not permitted)" : "");
        }

        private void CopyRows()
        {
            if (grid.SelectedItems.Count == 0) { status.Text = "Select one or more process rows to copy."; return; }
            try
            {
                var properties = typeof(ActivityProcess).GetProperties().Where(p => p.Name != nameof(ActivityProcess.SearchText)).ToArray();
                string Cell(object? value) => (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                var rows = grid.Items.Cast<ActivityRow>().Where(r => grid.SelectedItems.Contains(r));
                string text = string.Join('\t', properties.Select(p => p.Name)) + Environment.NewLine +
                    string.Join(Environment.NewLine, rows.Select(r => string.Join('\t', properties.Select(p => Cell(p.GetValue(r.Process))))));
                Clipboard.SetText(text); status.Text = "Copied selected process rows with headers.";
            }
            catch (ExternalException ex) { status.Text = "Clipboard unavailable: " + ex.Message; }
        }
        private async Task KillAsync()
        {
            await RunAsync(async () => {
                if (Selected is not { } process) return;
                if (ActivityMonitor.KillDenied(process, ownSpid, protectedSpids()) is { } reason) { status.Text = reason; return; }
                string warning = production ? "⚠ PROD · PRODUCTION SERVER\nKilling this process can interrupt production work and cause a long rollback.\n\n" : "Killing this process interrupts its work and can cause a transaction rollback.\n\n";
                string identity = $"Server: {session.Name}\nSPID: {process.Spid}\nLogin: {process.Login}\nHost: {process.Host}\nProgram: {process.Program}\nCommand: {process.Command}";
                if (production)
                {
                    var review = SqlRisk.Review("kill " + process.Spid.ToString(CultureInfo.InvariantCulture));
                    var first = new ProductionConfirmWindow(new ProductionPrompt(session.Name, "Kill SPID " + process.Spid, review, warning + identity))
                        { Owner = this, Title = "Kill SPID on PROD · confirmation 1 of 2" };
                    first.RunButton.Content = "Continue…";
                    if (first.ShowDialog() != true) return;
                }
                else if (!Confirm(this, "Kill SPID · confirmation 1 of 2", warning + identity, "Continue…")) return;
                string? typed = main.AskText("Kill SPID · confirmation 2 of 2", (production ? "PROD · " : "") +
                    $"Type {process.Spid} to kill this process. Cancel keeps it running.", owner: this);
                if (!ActivityMonitor.ConfirmsSpid(process.Spid, typed)) { status.Text = "Kill cancelled: the SPID was not confirmed."; return; }
                var fresh = ActivityMonitor.Parse(Table(await QueryAsync(ActivityMonitor.ProcessSql(process.Spid)))).FirstOrDefault();
                if (fresh == null || !ActivityMonitor.SameIdentity(process, fresh)) { status.Text = "Process changed or ended. Refresh and review it again."; return; }
                var output = await QueryAsync(ActivityMonitor.KillSql(fresh, ownSpid, protectedSpids()));
                if (output.Error != null) throw new InvalidOperationException(output.Error);
                status.Text = "Kill submitted for SPID " + process.Spid + ". Refresh to check rollback and process status.";
            });
        }
    }
}
