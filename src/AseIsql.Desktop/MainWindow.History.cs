using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private readonly List<QueryHistoryEntry> history = [];
    private string HistoryPath => isSmoke ? smokeHistoryPath : DataFolder.File("query-history.json");
    private readonly string smokeHistoryPath = Path.Combine(Path.GetTempPath(), "dbmon-history-smoke-" + Guid.NewGuid().ToString("N"), "query-history.json");
    private bool historyWritable = true;
    private string? historyWarning;

    private void LoadHistory()
    {
        try { history.AddRange(new QueryHistoryStore(HistoryPath).Load()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            historyWritable = false;
            historyWarning = "Query history could not be loaded. The existing file will not be overwritten: " + HistoryPath + " · " + ex.Message;
        }
    }

    private void RecordHistory(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        history.Insert(0, new QueryHistoryEntry(DateTimeOffset.Now, sql));
        history.Sort((left, right) => right.ExecutedAt.CompareTo(left.ExecutedAt));
        if (history.Count > QueryHistoryStore.Capacity) history.RemoveRange(QueryHistoryStore.Capacity, history.Count - QueryHistoryStore.Capacity);
        if (!historyWritable) { AddMessage(0, "CLIENT", historyWarning!); return; }
        try { new QueryHistoryStore(HistoryPath).Save(history); historyWarning = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            historyWarning = "Query history is available in this session but could not be saved: " + ex.Message;
            AddMessage(0, "CLIENT", historyWarning);
        }
    }

    private void HistoryClick(object sender, RoutedEventArgs e)
    {
        var items = new ListBox { Margin = new Thickness(14), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (var entry in history)
        {
            var row = new StackPanel { Margin = new Thickness(4) };
            var date = new TextBlock { Text = entry.ExecutedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"), Margin = new Thickness(0, 0, 0, 4) };
            date.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var sql = new TextBlock { Text = entry.Sql, TextWrapping = TextWrapping.Wrap, MaxHeight = 80 };
            sql.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            row.Children.Add(date); row.Children.Add(sql);
            items.Items.Add(new ListBoxItem { Content = row, Tag = entry });
        }
        var window = new Window { Owner = this, Title = "SQL History · newest first", Width = 820, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        WindowSizing.FitToWorkArea(window);
        var panel = new DockPanel();
        var hint = new TextBlock { Text = historyWarning ?? (history.Count == 0 ? "No saved queries yet." : "Latest 100 queries · double-click to load the complete SQL into the editor."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 14, 14, 0) };
        var location = new TextBlock { Text = HistoryPath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 4, 14, 0) };
        location.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(hint, Dock.Top); panel.Children.Add(hint);
        DockPanel.SetDock(location, Dock.Top); panel.Children.Add(location);
        panel.Children.Add(items); window.Content = panel;
        items.MouseDoubleClick += (_, _) => { if (items.SelectedItem is ListBoxItem { Tag: QueryHistoryEntry entry } && ConfirmDiscard()) { SetDocument(entry.Sql, null); window.Close(); } };
        window.ShowDialog();
    }
}
