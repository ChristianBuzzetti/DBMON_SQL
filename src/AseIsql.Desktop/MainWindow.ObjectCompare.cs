using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void ObjectCompareClick(object sender, RoutedEventArgs e)
    {
        if (!CanOpenReadTool()) return;
        var choose = new MultiRunWindow(this, "Compare ASE tables, procedures, views or triggers between two connections", "object comparison", "Object comparison");
        choose.ShowDialog();
        if (choose.Chosen is not { Count: >= 2 } targets) { StatusText.Text = "Object comparison needs at least two selected connections."; RefreshConnection(); return; }
        var ase = targets.Where(t => (ProfileOf(t)?.Platform ?? (t.Engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase) == ServerPlatform.Ase).ToArray();
        if (ase.Length < 2) { ShowError("Object comparison requires two SAP ASE connections. IQ and RS remain available in Server health check."); RefreshConnection(); return; }
        ShowObjectComparison(ase);
    }

    private void ShowObjectComparison(IReadOnlyList<RunTarget> targets)
    {
        var owner = activeSession;
        var window = new Window { Owner = this, Title = "Object comparison · read only · SAP ASE", Width = 1120, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SourceInitialized += (_, _) => Themes.ApplyChrome(window, Themes.Current);
        var layout = new DockPanel { Margin = new Thickness(16) };
        var controls = new StackPanel(); DockPanel.SetDock(controls, Dock.Top); layout.Children.Add(controls);
        var connections = new Grid(); connections.ColumnDefinitions.Add(new ColumnDefinition()); connections.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Label", SelectedIndex = 0, MinWidth = 220 };
        var right = new ComboBox { ItemsSource = targets, DisplayMemberPath = "Label", SelectedIndex = 1, MinWidth = 220 };
        var leftDb = new TextBox(); var rightDb = new TextBox();
        string Database(RunTarget target) => (target.Engine as ISessionFacts)?.Facts.Database ?? ProfileOf(target)?.Database ?? "";
        leftDb.Text = Database(targets[0]); rightDb.Text = Database(targets[1]);
        foreach (var (selector, database, title, position) in new[] { (left, leftDb, "Left connection / database", 0), (right, rightDb, "Right connection / database", 1) })
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 8) }; panel.Children.Add(new TextBlock { Text = title }); panel.Children.Add(selector); panel.Children.Add(database);
            Grid.SetColumn(panel, position); connections.Children.Add(panel);
        }
        left.SelectionChanged += (_, _) => { if (left.SelectedItem is RunTarget target) leftDb.Text = Database(target); };
        right.SelectionChanged += (_, _) => { if (right.SelectedItem is RunTarget target) rightDb.Text = Database(target); };
        controls.Children.Add(connections);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var types = new ComboBox { ItemsSource = Enum.GetValues<CompareObjectType>(), SelectedItem = CompareObjectType.Tables, MinWidth = 150 };
        var normalize = new CheckBox { Content = "Normalize SQL whitespace", IsEnabled = false, Margin = new Thickness(12, 5, 12, 5), ToolTip = "Ignore formatting outside quoted literals, identifiers and comments. The line diff can still show formatting changes." };
        var run = new Button { Content = "Compare objects", IsDefault = true }; var cancel = new Button { Content = "Cancel run", IsEnabled = false };
        foreach (var element in new FrameworkElement[] { types, normalize, run, cancel }) actions.Children.Add(element);
        controls.Children.Add(actions);
        var status = new TextBlock { Text = "Compare matching owner and object names. No schema changes are executed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        controls.Children.Add(status);
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        var list = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single };
        list.Columns.Add(new DataGridTextColumn { Header = "Owner", Binding = new Binding("Owner") });
        list.Columns.Add(new DataGridTextColumn { Header = "Object", Binding = new Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        list.Columns.Add(new DataGridTextColumn { Header = "Comparison", Binding = new Binding("Status"), Width = 180 });
        var rowStyle = new Style(typeof(DataGridRow), Application.Current.TryFindResource(typeof(DataGridRow)) as Style);
        foreach (var (difference, color) in new[] { (ObjectDifference.Same, "Ok"), (ObjectDifference.Different, "NoticeInk"), (ObjectDifference.OnlyLeft, "NoticeInk"), (ObjectDifference.OnlyRight, "NoticeInk"), (ObjectDifference.Unavailable, "ErrorInk") })
        {
            var trigger = new DataTrigger { Binding = new Binding("Difference"), Value = difference }; trigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(color))); rowStyle.Triggers.Add(trigger);
        }
        list.RowStyle = rowStyle; body.Children.Add(list);
        var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(splitter, 1); body.Children.Add(splitter);
        var tabs = new TabControl(); Grid.SetRow(tabs, 2); body.Children.Add(tabs);
        var unified = ToolTextBox(); var leftText = ToolTextBox(); var rightText = ToolTextBox();
        tabs.Items.Add(new TabItem { Header = "Unified line diff  (− left / + right)", Content = unified });
        var sideBySide = new Grid(); sideBySide.ColumnDefinitions.Add(new ColumnDefinition()); sideBySide.ColumnDefinitions.Add(new ColumnDefinition());
        sideBySide.Children.Add(leftText); Grid.SetColumn(rightText, 1); sideBySide.Children.Add(rightText); tabs.Items.Add(new TabItem { Header = "Left / right definitions", Content = sideBySide });
        layout.Children.Add(body); window.Content = layout;
        void Details()
        {
            if (list.SelectedItem is not ComparedObject item) return;
            leftText.Text = item.Left?.Error ?? item.Left?.Definition ?? "Object is absent from the left inventory.";
            rightText.Text = item.Right?.Error ?? item.Right?.Definition ?? "Object is absent from the right inventory.";
            try { unified.Text = item.Difference == ObjectDifference.Unavailable ? "Metadata unavailable. Inspect the left / right details.\n\nLEFT: " + (item.Left?.Error ?? "available") + "\nRIGHT: " + (item.Right?.Error ?? "available") : ObjectCompare.UnifiedDiff(item.Left?.Definition ?? "", item.Right?.Definition ?? ""); }
            catch (ArgumentException ex) { unified.Text = ex.Message; tabs.SelectedIndex = 1; }
        }
        list.SelectionChanged += (_, _) => Details();
        IReadOnlyList<ObjectSnapshot>? cachedLeft = null, cachedRight = null;
        CompareObjectType? cachedType = null;
        void Publish()
        {
            if (cachedLeft == null || cachedRight == null) return;
            var compared = ObjectCompare.Compare(cachedLeft, cachedRight, cachedType != CompareObjectType.Tables && normalize.IsChecked == true);
            list.ItemsSource = compared;
            status.Text = string.Join(" · ", Enum.GetValues<ObjectDifference>().Select(d => (d switch { ObjectDifference.OnlyLeft => "Only in left", ObjectDifference.OnlyRight => "Only in right", _ => d.ToString() }) + ": " + compared.Count(o => o.Difference == d).ToString(CultureInfo.InvariantCulture))) + " · select an object for its definitions and line diff";
            if (compared.Count > 0) list.SelectedIndex = 0;
        }
        normalize.Checked += (_, _) => Publish(); normalize.Unchecked += (_, _) => Publish();
        types.SelectionChanged += (_, _) => normalize.IsEnabled = types.SelectedItem is CompareObjectType type && type != CompareObjectType.Tables;
        CancellationTokenSource? pending = null;
        run.Click += async (_, _) => {
            if (pending != null || left.SelectedItem is not RunTarget l || right.SelectedItem is not RunTarget r || types.SelectedItem is not CompareObjectType type) return;
            if (ReferenceEquals(l.Engine, r.Engine)) { status.Text = "Choose two distinct connections."; return; }
            string ldb = leftDb.Text.Trim(), rdb = rightDb.Text.Trim();
            if (ldb.Length == 0 || rdb.Length == 0) { status.Text = "Enter the database for both sides."; return; }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5)); pending = cancellation; owner.Running = cancellation; RefreshFor(owner);
            run.IsEnabled = left.IsEnabled = right.IsEnabled = leftDb.IsEnabled = rightDb.IsEnabled = types.IsEnabled = normalize.IsEnabled = false; cancel.IsEnabled = true;
            cachedLeft = cachedRight = null; list.ItemsSource = null; unified.Clear(); leftText.Clear(); rightText.Clear();
            status.Text = "Reading object inventories and definitions on both connections…";
            try
            {
                var reads = await Task.WhenAll(Task.Run(() => CatalogSql.ReadObjects(new ToolQueryEngine(l.Engine, 60), ldb, type, cancellation.Token)), Task.Run(() => CatalogSql.ReadObjects(new ToolQueryEngine(r.Engine, 60), rdb, type, cancellation.Token)));
                cancellation.Token.ThrowIfCancellationRequested();
                cachedLeft = reads[0]; cachedRight = reads[1]; cachedType = type; Publish();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { cachedLeft = cachedRight = null; status.Text = cancellation.IsCancellationRequested ? "Comparison cancelled or exceeded its five-minute deadline. No inventory comparison was published." : "Comparison failed: " + ex.Message; }
            finally { pending = null; owner.Running = null; RefreshFor(owner); run.IsEnabled = left.IsEnabled = right.IsEnabled = leftDb.IsEnabled = rightDb.IsEnabled = types.IsEnabled = true; normalize.IsEnabled = types.SelectedItem is CompareObjectType current && current != CompareObjectType.Tables; cancel.IsEnabled = false; }
        };
        cancel.Click += (_, _) => { if (pending != null) _ = pending.CancelAsync(); };
        window.Closing += (_, args) => { if (pending == null) return; args.Cancel = true; _ = pending.CancelAsync(); status.Text = "Cancelling · waiting for both drivers to release their sessions…"; };
        window.ShowDialog(); RefreshConnection();
    }
}
