using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AseIsql.Odbc;
using Microsoft.Win32;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // These adapters suppress user execution options, IO/plan capture and trailing flush SQL on tool reads.
    private sealed class ToolQueryEngine(IQueryEngine inner, int timeout) : IQueryEngine
    {
        public string Name => inner.Name;
        public QueryOutput Execute(string sql, CancellationToken cancellation = default) => inner is OdbcSession session
            ? session.Execute(sql, cancellation, userRun: false, timeoutSeconds: timeout) : inner.Execute(sql, cancellation);
    }

    private bool CanOpenReadTool()
    {
        if (running != null || changingSession || catalogActions != 0 || repeat != null)
        { StatusText.Text = "Wait for the current operation and stop repeating before opening this tool."; return false; }
        return true;
    }

    private void HealthCheckClick(object sender, RoutedEventArgs e)
    {
        if (!CanOpenReadTool()) return;
        HealthSettings settings;
        string settingsPath =
#if DBMON_SMOKE
            isSmoke ? Path.Combine(AppContext.BaseDirectory, "UiSmoke", "health-check.json") :
#endif
            DataFolder.File("health-check.json");
        try { settings = HealthSettings.Load(settingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { ShowError("Unable to load health settings: " + ex.Message); return; }
        string rulesPath = Path.Combine(Path.GetDirectoryName(settingsPath)!, "health-rules.json");
        var rules = HealthRuleSet.Load(rulesPath, out string? rulesWarning, settings);
        var choose = new MultiRunWindow(this, "Fixed platform probes · version, capacity and availability", "health check", "Server health check");
        choose.ShowDialog();
        if (choose.Chosen is not { Count: > 0 } targets) { RefreshConnection(); return; }
        var mapped = targets.Select(t => new HealthTarget(new RunTarget(t.Label, new ToolQueryEngine(t.Engine, settings.ServerTimeoutSeconds)),
            ProfileOf(t)?.Platform ?? (t.Engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase, ProfileOf(t)?.Production ?? false)).ToArray();
        ShowHealthMatrix(mapped, settings, rules, rulesPath, rulesWarning);
    }

    private void ShowHealthMatrix(IReadOnlyList<HealthTarget> targets, HealthSettings settings, HealthRuleSet rules, string rulesPath, string? rulesWarning)
    {
        var owner = activeSession;
        var window = new Window { Owner = this, Title = "Server health check · read only", Width = 1120, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SourceInitialized += (_, _) => Themes.ApplyChrome(window, Themes.Current);
        var layout = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var run = new Button { Content = "Run health check", IsDefault = true };
        var cancel = new Button { Content = "Cancel run", IsEnabled = false };
        var csv = new Button { Content = "Export CSV…", IsEnabled = false };
        var html = new Button { Content = "Export HTML…", IsEnabled = false };
        var editRules = new Button { Content = "Rules…" };
        foreach (var button in new[] { run, cancel, editRules, csv, html }) actions.Children.Add(button);
        DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        var warning = new TextBlock { Text = rulesWarning ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Visibility = rulesWarning == null ? Visibility.Collapsed : Visibility.Visible };
        warning.SetResourceReference(TextBlock.ForegroundProperty, "NoticeInk");
        DockPanel.SetDock(warning, Dock.Top); layout.Children.Add(warning);
        var status = new TextBlock { Text = $"{targets.Count} servers · {settings.ServerTimeoutSeconds}s per server · PROD / non-PROD rules apply · select a cell for details", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var details = ToolTextBox(); details.Height = 220;
        DockPanel.SetDock(details, Dock.Bottom); layout.Children.Add(details);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionUnit = DataGridSelectionUnit.Cell, FrozenColumnCount = 1, MaxColumnWidth = 240 };
        IReadOnlyList<HealthServer> results = []; DataTable? matrix = null;
        grid.AutoGeneratingColumn += (_, args) => {
            if (args.PropertyDescriptor is PropertyDescriptor descriptor) args.Column.Header = matrix?.Columns[args.PropertyName]?.Caption ?? descriptor.DisplayName;
            var style = new Style(typeof(DataGridCell), Application.Current.TryFindResource(typeof(DataGridCell)) as Style);
            foreach (var (label, resource) in new[] { ("OK", "Ok"), ("WARN", "NoticeInk"), ("FAIL", "ErrorInk") })
            {
                var trigger = new DataTrigger { Binding = new Binding("[" + args.PropertyName + "]") { Converter = HealthStateConverter.Instance }, Value = label };
                trigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(resource)));
                trigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Triggers.Add(trigger);
            }
            args.Column.CellStyle = style;
        };
        grid.CurrentCellChanged += (_, _) => {
            if (grid.CurrentCell.Item is not DataRowView row || grid.CurrentColumn is not DataGridBoundColumn { Binding: Binding binding }) return;
            string column = binding.Path.Path.Trim('[', ']');
            if (!column.StartsWith("probe_", StringComparison.Ordinal)) { details.Text = "Select a probe cell to see SQL, returned values and messages."; return; }
            string label = Convert.ToString(row["Server"]) ?? "";
            var cell = results.FirstOrDefault(s => s.Label == label)?.Cells.FirstOrDefault(c => c.Probe.Id == column[6..]);
            details.Text = cell == null ? "This probe does not apply to this platform." : label + " · " + cell.Probe.Title + " · " + cell.Caption + "\n\n" + cell.Details;
        };
        layout.Children.Add(grid); window.Content = layout;
        CancellationTokenSource? pending = null;
        async Task Run()
        {
            if (pending != null) return;
            using var cancellation = new CancellationTokenSource(); pending = cancellation; owner.Running = cancellation; RefreshFor(owner);
            run.IsEnabled = editRules.IsEnabled = csv.IsEnabled = html.IsEnabled = false; cancel.IsEnabled = true;
            status.Text = "Reading health probes in parallel…"; details.Clear();
            try
            {
                results = await HealthCheck.ExecuteAsync(targets, settings, rules, cancellation.Token);
                matrix = HealthCheck.Matrix(results); grid.ItemsSource = matrix.DefaultView;
                status.Text = $"{results.Count} servers · {results.Count(s => s.State == HealthState.Fail)} FAIL · {results.Count(s => s.State == HealthState.Warn)} WARN · select a probe cell for details";
                csv.IsEnabled = html.IsEnabled = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = "Health check failed: " + ex.Message; }
            finally { pending = null; owner.Running = null; RefreshFor(owner); run.IsEnabled = editRules.IsEnabled = true; cancel.IsEnabled = false; }
        }
        editRules.Click += (_, _) => {
            var saved = EditHealthRules(window, rules, settings, rulesPath);
            if (saved == null) return;
            rules = saved; warning.Visibility = Visibility.Collapsed;
            status.Text = results.Count == 0 ? "Rules saved. Run health check to apply them." : "Rules saved. Existing results use the previous rules; rerun the health check to apply changes.";
        };
        run.Click += async (_, _) => await Run();
        cancel.Click += (_, _) => { if (pending != null) _ = pending.CancelAsync(); };
        window.Closing += (_, args) => { if (pending == null) return; args.Cancel = true; _ = pending.CancelAsync(); status.Text = "Cancelling · waiting for the driver to release its sessions…"; };
        void Export(bool asHtml)
        {
            if (matrix == null) return;
            var picker = new SaveFileDialog { Filter = asHtml ? "HTML report|*.html" : "CSV matrix|*.csv", DefaultExt = asHtml ? ".html" : ".csv", FileName = "server-health" };
            if (picker.ShowDialog(window) != true) return;
            try { AtomicFiles.SaveUtf8(picker.FileName, asHtml ? ResultFormats.Html(matrix, "Fixed read-only health probes", "Health check · " + DateTimeOffset.Now.ToString("O")) : CsvExport.Serialize(matrix)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = "Export failed: " + ex.Message; }
        }
        csv.Click += (_, _) => Export(false); html.Click += (_, _) => Export(true);
        window.Loaded += async (_, _) => await Run();
        window.ShowDialog(); RefreshConnection();
    }

    private static HealthRuleSet? EditHealthRules(Window owner, HealthRuleSet rules, HealthSettings settings, string path)
    {
        var window = new Window { Owner = owner, Title = "Health check rules", Width = 900, Height = 610, MinWidth = 760, MinHeight = 460, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Control.BackgroundProperty, "Canvas"); window.SetResourceReference(Control.ForegroundProperty, "Ink");
        window.SourceInitialized += (_, _) => Themes.ApplyChrome(window, Themes.Current);
        var layout = new DockPanel { Margin = new Thickness(16) };
        var hint = new TextBlock { Text = $"Severity caps every result, including errors. Ignore never raises Overall. Disabled probes do not run.\nBlank thresholds inherit health-check.json: usage >{settings.UsageWarnPercent}% / >{settings.UsageFailPercent}%, dump >{settings.DumpWarnHours} / >{settings.DumpFailHours} hours, blocking ≥{settings.BlockedWarnCount} / ≥{settings.BlockedFailCount} (WARN / FAIL).", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); DockPanel.SetDock(hint, Dock.Top); layout.Children.Add(hint);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        message.SetResourceReference(TextBlock.ForegroundProperty, "NoticeInk"); footer.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var reset = new Button { Content = "Reset to defaults" };
        var save = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        foreach (var button in new[] { reset, save, cancel }) buttons.Children.Add(button);
        footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var tabs = new TabControl();
        var grids = new List<DataGrid>();
        var rows = new List<HealthRuleRow[]>();
        Style InputStyle(Type type, bool display = false)
        {
            var style = new Style(type, Application.Current.TryFindResource(type) as Style);
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            if (display)
            {
                style.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
                style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            }
            return style;
        }
        foreach (bool production in new[] { true, false })
        {
            var grid = new DataGrid { IsReadOnly = false, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, CanUserReorderColumns = false, RowHeight = double.NaN, MinRowHeight = 38, Margin = new Thickness(0, 10, 0, 0) };
            grid.Columns.Add(new DataGridTextColumn { Header = "Probe", IsReadOnly = true, Binding = new Binding(nameof(HealthRuleRow.Title)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Enabled", Binding = new Binding(nameof(HealthRuleRow.Enabled)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ElementStyle = InputStyle(typeof(CheckBox), true), EditingElementStyle = InputStyle(typeof(CheckBox)), Width = 90 });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "Severity", ItemsSource = Enum.GetValues<HealthSeverity>(), SelectedItemBinding = new Binding(nameof(HealthRuleRow.Severity)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ElementStyle = InputStyle(typeof(ComboBox), true), EditingElementStyle = InputStyle(typeof(ComboBox)), Width = 100 });
            foreach (var (header, property) in new[] { ("Warn threshold", nameof(HealthRuleRow.Warn)), ("Fail threshold", nameof(HealthRuleRow.Fail)) })
            {
                var style = new Style(typeof(TextBlock));
                var trigger = new DataTrigger { Binding = new Binding(nameof(HealthRuleRow.HasThresholds)), Value = false };
                trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden)); style.Triggers.Add(trigger);
                grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, ElementStyle = style, EditingElementStyle = InputStyle(typeof(TextBox)), Width = 130 });
            }
            grid.BeginningEdit += (_, args) => {
                if (args.Column.DisplayIndex >= 3 && args.Row.Item is HealthRuleRow { HasThresholds: false }) args.Cancel = true;
            };
            var items = HealthRuleSet.AllProbes.Select(p => new HealthRuleRow(p, rules.RuleFor(p, production))).ToArray();
            rows.Add(items); grid.ItemsSource = items; grids.Add(grid);
            tabs.Items.Add(new TabItem { Header = production ? "PROD servers" : "Non-PROD servers", Content = grid });
        }
        layout.Children.Add(tabs); window.Content = layout;
        HealthRuleSet? saved = null;
        reset.Click += (_, _) => {
            var defaults = new HealthRuleSet();
            for (int i = 0; i < grids.Count; i++)
            {
                grids[i].CancelEdit(DataGridEditingUnit.Cell); grids[i].CancelEdit(DataGridEditingUnit.Row);
                rows[i] = HealthRuleSet.AllProbes.Select(p => new HealthRuleRow(p, defaults.RuleFor(p, i == 0))).ToArray();
                grids[i].ItemsSource = rows[i];
            }
            message.Text = "Both rule sets have been reset. Save to keep these defaults.";
        };
        save.Click += (_, _) => {
            try
            {
                foreach (var grid in grids)
                    if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true))
                        throw new ArgumentException("Finish editing the current rule before saving.");
                List<HealthRule> Merge(int index, List<HealthRule> original) => rows[index].Select(r => r.ToRule())
                    .Concat(original.Where(r => !HealthRuleSet.AllProbes.Any(p => p.Id == r.ProbeId))).ToList();
                var candidate = rules with { Production = Merge(0, rules.Production), NonProduction = Merge(1, rules.NonProduction) };
                candidate.Save(path, settings); saved = candidate; window.DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { message.Text = "Rules were not saved: " + ex.Message; }
        };
        window.ShowDialog(); return saved;
    }

    private sealed class HealthRuleRow(HealthProbe probe, HealthRule rule)
    {
        public string Title => probe.Title + (probe.Metric is HealthMetric.Percent or HealthMetric.IqSpace ? " (%)" : probe.Metric == HealthMetric.DumpAge ? " (hours)" : probe.Metric == HealthMetric.Blocked ? " (count)" : "");
        public bool HasThresholds => HealthRule.HasThresholds(probe);
        public bool Enabled { get; set; } = rule.Enabled;
        public HealthSeverity Severity { get; set; } = rule.Severity;
        public string Warn { get; set; } = rule.WarnThreshold?.ToString(CultureInfo.CurrentCulture) ?? "";
        public string Fail { get; set; } = rule.FailThreshold?.ToString(CultureInfo.CurrentCulture) ?? "";

        public HealthRule ToRule()
        {
            double? Parse(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || !double.IsFinite(value))
                    throw new ArgumentException(probe.Title + ": enter a finite number or leave the threshold blank.");
                return value;
            }
            return rule with { Enabled = Enabled, Severity = Severity, WarnThreshold = Parse(Warn), FailThreshold = Parse(Fail) };
        }
    }

    private static TextBox ToolTextBox()
    {
        var box = new TextBox { IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 0) };
        box.SetResourceReference(Control.FontFamilyProperty, "MonoFont"); return box;
    }

    private sealed class HealthStateConverter : IValueConverter
    {
        public static readonly HealthStateConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (value?.ToString() ?? "").Split(' ', '·')[0];
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
