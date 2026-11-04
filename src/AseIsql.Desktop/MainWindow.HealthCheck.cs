using System.ComponentModel;
using System.Data;
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
        try { settings = HealthSettings.Load(isSmoke ? Path.Combine(AppContext.BaseDirectory, "UiSmoke", "health-check.json") : DataFolder.File("health-check.json")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { ShowError("Unable to load health settings: " + ex.Message); return; }
        var choose = new MultiRunWindow(this, "Fixed platform probes · version, capacity and availability", "health check", "Server health check");
        choose.ShowDialog();
        if (choose.Chosen is not { Count: > 0 } targets) { RefreshConnection(); return; }
        var mapped = targets.Select(t => new HealthTarget(new RunTarget(t.Label, new ToolQueryEngine(t.Engine, settings.ServerTimeoutSeconds)),
            ProfileOf(t)?.Platform ?? (t.Engine as ISessionFacts)?.Facts.Platform ?? ServerPlatform.Ase)).ToArray();
        ShowHealthMatrix(mapped, settings);
    }

    private void ShowHealthMatrix(IReadOnlyList<HealthTarget> targets, HealthSettings settings)
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
        foreach (var button in new[] { run, cancel, csv, html }) actions.Children.Add(button);
        DockPanel.SetDock(actions, Dock.Top); layout.Children.Add(actions);
        var status = new TextBlock { Text = $"{targets.Count} servers · {settings.ServerTimeoutSeconds}s per server · usage >{settings.UsageWarnPercent}% WARN / >{settings.UsageFailPercent}% FAIL · select a cell for details", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
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
            run.IsEnabled = csv.IsEnabled = html.IsEnabled = false; cancel.IsEnabled = true;
            status.Text = "Reading health probes in parallel…"; details.Clear();
            try
            {
                results = await HealthCheck.ExecuteAsync(targets, settings, cancellation.Token);
                matrix = HealthCheck.Matrix(results); grid.ItemsSource = matrix.DefaultView;
                status.Text = $"{results.Count} servers · {results.Count(s => s.State == HealthState.Fail)} FAIL · {results.Count(s => s.State == HealthState.Warn)} WARN · select a probe cell for details";
                csv.IsEnabled = html.IsEnabled = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = "Health check failed: " + ex.Message; }
            finally { pending = null; owner.Running = null; RefreshFor(owner); run.IsEnabled = true; cancel.IsEnabled = false; }
        }
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
