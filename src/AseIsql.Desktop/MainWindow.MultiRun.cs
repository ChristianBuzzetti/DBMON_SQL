using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AseIsql.Odbc;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Extra sessions owned for multi-server runs. The current session is offered too but stays owned by the
    // connection flow; extras are opened from the run dialog and closed on removal or when the window closes.
    private readonly List<RunTarget> extraTargets = [];
    // Servers the user unchecked stay unchecked for the next run of this window's lifetime.
    private readonly HashSet<string> excludedTargets = new(StringComparer.OrdinalIgnoreCase);
    // The login profile of each extra session, so the current list can be saved as a group (never a password).
    private readonly Dictionary<RunTarget, ConnectionProfile> targetProfiles = [];
    // Each Diff tab remembers the per-server tables and key it compared, so the key can be changed later.
    private readonly Dictionary<DataTable, (IReadOnlyList<(string Server, DataTable Table)> Parts, IReadOnlyList<int> Keys, int Number)> diffSources = [];
    private string ServerGroupsPath => isSmoke ? Path.Combine(AppContext.BaseDirectory, "UiSmoke", "server-groups.json") :
        DataFolder.File("server-groups.json");

    private static string ProfileLabel(ConnectionProfile profile) => profile.ServerAlias.Length != 0 ? profile.ServerAlias : profile.Server + ":" + profile.Port;
    private static bool IsLive(IQueryEngine target) => target is not OdbcSession session || session.IsOpen;

    private List<RunTarget> AvailableTargets()
    {
        var all = new List<RunTarget>();
        if (engine is not DisconnectedQueryEngine)
        {
            string label = lastProfile != null ? ProfileLabel(lastProfile) : "current";
            if (extraTargets.Any(t => t.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) label += " (current)";
            all.Add(new RunTarget(label, engine));
        }
        all.AddRange(extraTargets);
        return all;
    }

    private static string DescribeTarget(RunTarget target) => target.Engine.Name +
        (target.Engine is ISessionFacts { Facts.Spid: int spid } ? " · SPID " + spid.ToString(CultureInfo.InvariantCulture) : "") +
        (IsLive(target.Engine) ? "" : " · session interrupted");

    private void MultiRunClick(object sender, RoutedEventArgs e)
    {
        if (running != null || changingSession) return;
        string scope = Editor.SelectionLength > 0 ? "selection" : "script";
        string sql = Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Text;
        if (string.IsNullOrWhiteSpace(sql)) { StatusText.Text = "Write SQL before running it on several servers."; Editor.Focus(); return; }
        var dialog = new MultiRunWindow(this, sql, scope);
        dialog.ShowDialog();
        if (dialog.Chosen is { Count: > 0 } chosen) _ = ExecuteOnTargetsAsync(chosen, sql, scope);
        else RefreshConnection();
    }

    private async Task ExecuteOnTargetsAsync(IReadOnlyList<RunTarget> chosen, string sql, string scope)
    {
        if (running != null || changingSession) return;
        ClearMessages();
        using var cancellation = new CancellationTokenSource();
        executingSql = sql; running = cancellation;
        ExecuteButton.IsEnabled = false; CancelButton.IsEnabled = true; RefreshConnection();
        AddMessage(0, "CLIENT", $"Execute · {scope} · {Counts.Of(chosen.Count, "server")} in parallel · {string.Join(", ", chosen.Select(t => t.Label))}");
        var timer = Stopwatch.StartNew();
        var ticker = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        ticker.Tick += (_, _) => StatusText.Text = $"Executing on {Counts.Of(chosen.Count, "server")} · {timer.Elapsed.TotalSeconds:F1} s · Esc cancels all";
        StatusText.Text = $"Executing on {Counts.Of(chosen.Count, "server")} · Esc cancels all"; ticker.Start();
        try
        {
            var outcomes = await MultiRun.ExecuteAsync(chosen, sql, cancellation.Token);
            double elapsedMs = timer.Elapsed.TotalMilliseconds; ticker.Stop();
            // Multi-server results accumulate like any run; they are registered without a session, so F9 points to Run on….
            int firstNew = Workspace.Items.Count; var multiRun = NewRun(sql, null);
            foreach (var combined in MultiRun.Combine(outcomes))
            {
                if (combined.Table != null)
                {
                    AddResult(combined.Table, combined.Index + 1, "All servers · " + string.Join(", ", outcomes.Where(o => o.Output.Tables.Count > combined.Index).Select(o => o.Target.Label)), sql, $"All · {combined.Index + 1}");
                    var parts = outcomes.Where(o => o.Output.Tables.Count > combined.Index).Select(o => (o.Target.Label, o.Output.Tables[combined.Index])).ToList();
                    var diff = AddDiffResult(parts, [0], combined.Index + 1, sql);
                    AddMessage(0, "CLIENT", diff.DifferingKeys == 0 ? $"Result {combined.Index + 1} is identical on every server · {Counts.Of(diff.Keys, "key")} by {diff.KeyDescription}" :
                        $"Result {combined.Index + 1} · {diff.DifferingKeys} of {Counts.Of(diff.Keys, "key")} differ between servers · key {diff.KeyDescription} · see Diff · {combined.Index + 1}");
                }
                else if (outcomes.Count(o => o.Output.Tables.Count > combined.Index) > 1) AddMessage(0, "CLIENT", combined.Reason!);
            }
            foreach (var outcome in outcomes)
            {
                string label = outcome.Target.Label; int number = 1;
                string origin = OriginOf(outcome.Target.Engine);
                foreach (DataTable table in outcome.Output.Tables) { AddResult(table, number, origin, sql, $"{label} · {number}", label); number++; }
                foreach (QueryMessage message in outcome.Output.Messages) AddMessage(message.Number, label, message.Text, message.RawText);
                if (outcome.Output.OmittedMessages > 0) AddMessage(0, label, $"{outcome.Output.OmittedMessages} earlier messages were omitted by the retention limit.");
                if (outcome.Output.Error != null) AddMessage(1, label, outcome.Output.Error, outcome.Output.ErrorRawText);
                else AddMessage(0, label, $"Done · {Counts.Of(outcome.Output.Tables.Count, "result")} · {Counts.Of(outcome.Output.Tables.Sum(t => t.Rows.Count), "row")} · {Elapsed(outcome.ElapsedMs)}");
            }
            history.Insert(0, sql); if (history.Count > 100) history.RemoveAt(100);
            foreach (var added in Workspace.Items.OfType<TabItem>().Skip(firstNew).Where(t => t.Tag is DataTable table && !resultRuns.ContainsKey(table))) resultRuns[(DataTable)added.Tag] = multiRun;
            TrimResults(multiRun.Id);
            int failed = outcomes.Count(o => o.Output.Error != null);
            if (failed > 0) SetLogVisible(true);
            if (Workspace.Items.OfType<TabItem>().FirstOrDefault(tab => tab.Tag is DataTable table && resultRuns.TryGetValue(table, out var owner) && owner.Id == multiRun.Id) is { } firstTab) Workspace.SelectedItem = firstTab;
            StatusText.Text = $"{(failed == 0 ? "Ready" : "Done with errors")} · {Counts.Of(chosen.Count, "server")} · {chosen.Count - failed} ok · {failed} failed · {Elapsed(elapsedMs)}";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { AddMessage(1, "CLIENT", "Multi-server run failed: " + ex.Message); StatusText.Text = "Error · see Messages"; SetLogVisible(true); }
        finally { ticker.Stop(); running = null; ExecuteButton.IsEnabled = true; CancelButton.IsEnabled = false; RefreshConnection(); }
    }

    private DiffResult AddDiffResult(IReadOnlyList<(string Server, DataTable Table)> parts, IReadOnlyList<int> keys, int number, string sql)
    {
        var diff = ResultDiff.Compare(parts, keys);
        AddResult(diff.Table, number, $"Differences by {diff.KeyDescription} · {diff.DifferingKeys} of {Counts.Of(diff.Keys, "key")} differ · {string.Join(", ", parts.Select(p => p.Server))}", sql, $"Diff · {number}");
        var tab = (TabItem)Workspace.Items[^1];
        diffSources[diff.Table] = (parts, keys, number);
        var keyButton = new Button { Content = "Key: " + diff.KeyDescription + "  ▾", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0), MaxWidth = 320,
            ToolTip = "Rows are matched across servers by these columns · click to choose others" };
        keyButton.Click += (_, _) => ChooseDiffKeys(tab);
        ((DockPanel)tab.Content).Children.OfType<DockPanel>().First().Children.Add(keyButton);
        return diff;
    }

    private void ChooseDiffKeys(TabItem tab)
    {
        if (tab.Tag is not DataTable table || !diffSources.TryGetValue(table, out var source)) return;
        var shape = source.Parts[0].Table;
        var window = new Window { Owner = this, Title = "Diff key columns", Width = 420, SizeToContent = SizeToContent.Height, MaxHeight = 640, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Rows are matched across servers by the checked columns. The other columns are compared value by value.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var list = new StackPanel();
        var boxes = shape.Columns.Cast<DataColumn>().Select((column, index) => new CheckBox { Content = column.Caption.Length == 0 ? UnnamedColumn : column.Caption, IsChecked = source.Keys.Contains(index), Margin = new Thickness(0, 3, 0, 3) }).ToList();
        foreach (var box in boxes) list.Children.Add(box);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "Compare", IsDefault = true }; ok.Click += (_, _) => window.DialogResult = true;
        void Update() => ok.IsEnabled = boxes.Any(box => box.IsChecked == true);
        foreach (var box in boxes) { box.Checked += (_, _) => Update(); box.Unchecked += (_, _) => Update(); }
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true }); buttons.Children.Add(ok); panel.Children.Add(buttons);
        window.Content = panel; window.SourceInitialized += (_, _) => Themes.ApplyChrome(window, Themes.Current); Update();
        if (window.ShowDialog() != true) return;
        RekeyDiff(tab, boxes.Select((box, index) => (box, index)).Where(pair => pair.box.IsChecked == true).Select(pair => pair.index).ToList());
    }

    // Replaces a Diff tab in place with the comparison by other key columns; the per-server tables stay as received.
    private bool RekeyDiff(TabItem tab, IReadOnlyList<int> keys)
    {
        if (tab.Tag is not DataTable table || !diffSources.TryGetValue(table, out var source)) return false;
        try
        {
            int position = Workspace.Items.IndexOf(tab); bool pinned = pinnedResults.Contains(table);
            var diff = AddDiffResult(source.Parts, keys, source.Number, resultOrigins.GetValueOrDefault(table).Sql);
            var replacement = (TabItem)Workspace.Items[^1];
            CloseResult(tab); Workspace.Items.Remove(replacement); Workspace.Items.Insert(Math.Min(position, Workspace.Items.Count), replacement);
            if (pinned) TogglePin(replacement);
            Workspace.SelectedItem = replacement;
            StatusText.Text = $"Diff · {diff.DifferingKeys} of {Counts.Of(diff.Keys, "key")} differ by {diff.KeyDescription}";
            return true;
        }
        catch (ArgumentException ex) { ShowError("Unable to compare: " + ex.Message); return false; }
    }

    private IReadOnlyList<ServerGroup> LoadServerGroups()
    {
        try { return ServerGroupStore.Load(ServerGroupsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { ShowError("Unable to read server groups: " + ex.Message); return []; }
    }

    private bool SaveServerGroups(IReadOnlyList<ServerGroup> groups)
    {
        try { ServerGroupStore.Save(ServerGroupsPath, groups); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ShowError("Unable to save server groups: " + ex.Message); return false; }
    }

    private ConnectionProfile? ProfileOf(RunTarget target) => ReferenceEquals(target.Engine, engine) ? lastProfile : targetProfiles.GetValueOrDefault(target);

    // Adopts a session opened for multi-server runs under a label that is unique among the listed servers.
    private RunTarget AdoptTarget(IQueryEngine session, ConnectionProfile profile)
    {
        var taken = AvailableTargets().Select(t => t.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string label = ProfileLabel(profile), unique = label;
        for (int n = 2; taken.Contains(unique); n++) unique = label + " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
        var target = new RunTarget(unique, session);
        extraTargets.Add(target); targetProfiles[target] = profile;
        RefreshConnection();
        return target;
    }

    private async Task RemoveTarget(RunTarget target)
    {
        extraTargets.Remove(target); excludedTargets.Remove(target.Label); targetProfiles.Remove(target);
        if (target.Engine is IDisposable owned)
        {
            try { await Task.Run(owned.Dispose); }
            catch (Exception ex) when (ex is OdbcException or InvalidOperationException) { AddMessage(1, "CLIENT", "Session closure was not confirmed for " + target.Label + ": " + ex.Message); }
        }
        RefreshConnection();
    }

    // Chooses the servers of one run. Adding a server opens the usual login in its own owned session; nothing about
    // the credentials is kept here. Running is the confirmation: the window lists every server that will receive the SQL.
    private sealed class MultiRunWindow : Window
    {
        private readonly MainWindow main;
        private readonly StackPanel rows = new();
        private readonly Button run = new() { IsDefault = true };
        private readonly Button add = new();
        private readonly Button groups = new();
        private readonly Dictionary<RunTarget, CheckBox> toggles = [];
        public IReadOnlyList<RunTarget>? Chosen { get; private set; }

        public MultiRunWindow(MainWindow main, string sql, string scope)
        {
            this.main = main; Owner = main; Title = "Run on multiple servers"; Width = 700; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SourceInitialized += (_, _) => Themes.ApplyChrome(this, Themes.Current);
            var layout = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            var heading = new TextBlock { Text = "◤  Run on multiple servers", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            heading.SetResourceReference(TextBlock.FontSizeProperty, "SectionFontSize");
            layout.Children.Add(heading);
            layout.Children.Add(Muted("The same SQL runs in parallel, one session per server. Each server keeps its own row limit, timeout, results and messages; results with the same columns are also combined under a server column and compared in a Diff tab.", wrap: true));
            layout.Children.Add(Section("SERVERS"));
            layout.Children.Add(rows);
            add.Content = "+  Add server…"; add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 8, 0, 0);
            add.ToolTip = "Open another session (ASE, IQ or Replication Server) for multi-server runs · the password is not stored";
            add.Click += (_, _) => AddServer();
            groups.Content = "Groups  ▾"; groups.Margin = new Thickness(8, 8, 0, 0);
            groups.ToolTip = "Open a saved group of servers, or save the listed servers as a group · passwords are never saved";
            groups.Click += (_, _) => ShowGroups();
            var serverActions = new StackPanel { Orientation = Orientation.Horizontal }; serverActions.Children.Add(add); serverActions.Children.Add(groups);
            layout.Children.Add(serverActions);
            layout.Children.Add(Section("SQL"));
            int lines = sql.Split('\n').Length;
            layout.Children.Add(Muted($"{char.ToUpperInvariant(scope[0])}{scope[1..]} · {Counts.Of(lines, "line")} · {Counts.Of(sql.Length, "character")}"));
            var preview = new TextBlock { Text = sql.Trim().Split('\n')[0].TrimEnd('\r'), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };
            preview.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            layout.Children.Add(preview);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            run.SetResourceReference(BackgroundProperty, "Accent"); run.SetResourceReference(ForegroundProperty, "OnAccent"); run.SetResourceReference(BorderBrushProperty, "Accent");
            run.Click += (_, _) => { Chosen = toggles.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToList(); Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(run);
            layout.Children.Add(buttons);
            Content = layout;
            Refresh();
        }

        private static TextBlock Section(string text)
        {
            var label = new TextBlock { Text = "▚  " + text, Margin = new Thickness(0, 18, 0, 8) };
            label.SetResourceReference(StyleProperty, "SectionLabel");
            return label;
        }

        private static TextBlock Muted(string text, bool wrap = false)
        {
            var block = new TextBlock { Text = text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return block;
        }

        internal int RowCount => toggles.Count;
        internal string RunCaption => run.Content?.ToString() ?? "";

        private void Refresh()
        {
            rows.Children.Clear(); toggles.Clear();
            var targets = main.AvailableTargets();
            if (targets.Count == 0) rows.Children.Add(Muted("No sessions yet. Add the servers that should run this SQL."));
            foreach (var target in targets)
            {
                bool live = IsLive(target.Engine);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var toggle = new CheckBox { Content = target.Label, FontWeight = FontWeights.SemiBold, IsEnabled = live, IsChecked = live && !main.excludedTargets.Contains(target.Label), Margin = new Thickness(0, 4, 14, 4), MinWidth = 170 };
                toggle.Checked += (_, _) => { main.excludedTargets.Remove(target.Label); UpdateRun(); };
                toggle.Unchecked += (_, _) => { main.excludedTargets.Add(target.Label); UpdateRun(); };
                DockPanel.SetDock(toggle, Dock.Left); row.Children.Add(toggle);
                if (main.extraTargets.Contains(target))
                {
                    var remove = new Button { Content = "", ToolTip = "Close this session and remove it from multi-server runs" };
                    remove.SetResourceReference(StyleProperty, "IconButton");
                    remove.Click += async (_, _) => { IsEnabled = false; try { await main.RemoveTarget(target); } finally { IsEnabled = true; Refresh(); } };
                    DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                }
                else { var current = Muted("current"); current.Margin = new Thickness(8, 0, 6, 0); current.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(current, Dock.Right); row.Children.Add(current); }
                var detail = Muted(DescribeTarget(target)); detail.VerticalAlignment = VerticalAlignment.Center;
                detail.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont"); detail.SetResourceReference(TextBlock.FontSizeProperty, "SmallFontSize");
                row.Children.Add(detail);
                rows.Children.Add(row); toggles[target] = toggle;
            }
            add.IsEnabled = targets.Count < MultiRun.MaxTargets;
            UpdateRun();
        }

        private void UpdateRun()
        {
            int count = toggles.Values.Count(toggle => toggle.IsChecked == true);
            run.Content = count == 0 ? "Run" : $"Run on {Counts.Of(count, "server")}";
            run.IsEnabled = count > 0;
        }

        private void AddServer() => Login(main.lastProfile, null);

        // One login per server, prefilled from the profile when there is one; the user types only the password.
        private bool Login(ConnectionProfile? profile, string? caption)
        {
            if (main.AvailableTargets().Count >= MultiRun.MaxTargets) return false;
            var login = new ConnectionWindow(profile) { Owner = this };
            if (caption != null) login.Title += " · " + caption;
            if (login.ShowDialog() != true || login.TakeConnection() is not { } connection) return false;
            main.AdoptTarget(connection.Session, connection.Selection.Profile);
            Refresh();
            return true;
        }

        private void ShowGroups()
        {
            var menu = new ContextMenu { PlacementTarget = groups, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var saved = main.LoadServerGroups();
            foreach (var group in saved)
            {
                var item = new MenuItem { Header = $"{group.Name}  ·  {Counts.Of(group.Profiles.Count, "server")}" };
                var open = new MenuItem { Header = "Open · log in to each server" }; open.Click += (_, _) => OpenGroup(group);
                var delete = new MenuItem { Header = "Delete group" }; delete.Click += (_, _) => main.SaveServerGroups(ServerGroupStore.Remove(main.LoadServerGroups(), group.Name));
                item.Items.Add(open); item.Items.Add(delete); menu.Items.Add(item);
            }
            if (saved.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved groups yet", IsEnabled = false });
            menu.Items.Add(new Separator());
            var profiles = ListedProfiles();
            var save = new MenuItem { Header = "Save listed servers as group…", IsEnabled = profiles.Count > 0 };
            save.Click += (_, _) => SaveGroup(profiles);
            menu.Items.Add(save);
            menu.IsOpen = true;
        }

        internal List<ConnectionProfile> ListedProfiles() => main.AvailableTargets().Select(main.ProfileOf).OfType<ConnectionProfile>().ToList();

        private void SaveGroup(IReadOnlyList<ConnectionProfile> profiles)
        {
            string? name = main.AskText("Save server group", $"Name for this group of {Counts.Of(profiles.Count, "server")}. Passwords are not saved; opening the group asks for each one.", "", this)?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            try { if (main.SaveServerGroups(ServerGroupStore.Upsert(main.LoadServerGroups(), new ServerGroup(name, profiles)))) main.StatusText.Text = "Server group saved · " + name; }
            catch (ArgumentException ex) { main.ShowError(ex.Message); }
        }

        internal int OpenGroup(ServerGroup group, Func<ConnectionProfile, string, bool>? login = null)
        {
            int opened = 0, index = 0;
            foreach (var profile in group.Profiles)
            {
                index++;
                // A server already in the list (same endpoint and login) is reused rather than opened twice.
                if (ListedProfiles().Any(p => p.Server.Equals(profile.Server, StringComparison.OrdinalIgnoreCase) && p.Port == profile.Port && p.User.Equals(profile.User, StringComparison.Ordinal))) continue;
                if (!(login ?? Login)(profile, $"group {group.Name} · {index}/{group.Profiles.Count}")) break;
                opened++;
            }
            main.StatusText.Text = $"Server group {group.Name} · {Counts.Of(opened, "session")} opened";
            Refresh();
            return opened;
        }
    }
}
