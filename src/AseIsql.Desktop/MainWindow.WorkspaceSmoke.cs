using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private async Task VerifyWorkspacePolish(Action<bool, string> require)
    {
        var tabs = Workspace.Items.OfType<TabItem>().Where(tab => tab == QueryTab || tab.Tag is DataTable).ToArray();
        foreach (var selected in tabs)
        {
            Workspace.SelectedItem = selected;
            Workspace.UpdateLayout();
            for (int i = 1; i < tabs.Length; i++)
            {
                var previous = (Border)tabs[i - 1].Template.FindName("Frame", tabs[i - 1]);
                var next = (Border)tabs[i].Template.FindName("Frame", tabs[i]);
                double gap = next.TranslatePoint(new Point(), Workspace).X - previous.TranslatePoint(new Point(previous.ActualWidth, 0), Workspace).X;
                require(gap >= 3.9, $"Tab plates keep a clear four-DIP gap with {selected.Header} selected (gap {gap:F1})");
            }
            CaptureSmokeVisual(this, "workspace-" + selected.Header.ToString()!.Replace(' ', '-') + ".png");
        }
        ClearResults(true);
        Editor.Text = "select 1; select 2";
        await ExecuteAsync();
        require(ResultTabs.Select(ResultName).SequenceEqual(new[] { "Result 1", "Result 2" }), "Closing every result restarts numbering at Result 1");
        CloseResult(ResultTabs.First());
        Workspace.SelectedItem = QueryTab;
        Editor.Text = "select 3";
        await ExecuteAsync();
        require(ResultName(ResultTabs.Last()) == "Result 3", "Keeping a result does not reset the numbering");
        // Reruns temporarily remove their own results but must not rewind the next fresh run's counter.
        ClearResults(true);
        Editor.Text = "select 1; select 2";
        await ExecuteAsync();
        await RunPressedAsync();
        Workspace.SelectedItem = QueryTab;
        await ExecuteAsync();
        require(ResultTabs.Select(ResultName).SequenceEqual(new[] { "Result 1", "Result 2", "Result 3", "Result 4" }), "Replacing the only run preserves its names and the next fresh result numbers");

        var savedNames = databaseNames;
        databaseNames = Enumerable.Range(0, 100).Select(i => i == 0 ? "mon_db" : "db_" + i).ToList();
        var menu = CreateDatabaseMenu();
        try
        {
            menu.MaxHeight = 180;
            menu.IsOpen = true;
            menu.UpdateLayout();
            var first = (MenuItem)menu.Items[0];
            require(first.Header is TextBlock { Text: "mon_db" }, "Database menu renders literal underscores without changing identifiers");
            ScrollViewer? FindScroll(DependencyObject parent)
            {
                if (parent is ScrollViewer viewer) return viewer;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    if (FindScroll(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
                return null;
            }
            var scroll = FindScroll(menu);
            require(scroll is { ScrollableHeight: > 0, ComputedVerticalScrollBarVisibility: Visibility.Visible }, "A constrained database menu exposes a visible scrollbar");
            scroll!.ScrollToBottom();
            menu.UpdateLayout();
            require(scroll.VerticalOffset > 0 && scroll.VerticalOffset >= scroll.ScrollableHeight - 1, "The database menu can scroll to its final Reload list entry");
        }
        finally { menu.IsOpen = false; databaseNames = savedNames; }
        ClearResults(true);
        Editor.Text = DemoSql;
        await ExecuteAsync();
        var persisted = new QueryHistoryStore(HistoryPath).Load();
        require(persisted.SequenceEqual(history), "Every execution saves exact timestamped SQL outside the real user history in smoke");
        history.Clear(); LoadHistory();
        require(history.SequenceEqual(persisted), "A new history load restores chronological entries from disk");
        string original = File.ReadAllText(HistoryPath);
        File.WriteAllText(HistoryPath, "{malformed fixture");
        history.Clear(); LoadHistory(); RecordHistory("select 'must remain in memory only'");
        require(!historyWritable && historyWarning != null && File.ReadAllText(HistoryPath) == "{malformed fixture", "Unreadable history stays untouched and later executions expose a warning");
        File.WriteAllText(HistoryPath, original); historyWritable = true; historyWarning = null;
        history.Clear(); LoadHistory();
        savedText = Editor.Text;
        Exception? dialogFailure = null;
        _ = Dispatcher.BeginInvoke(new Action(() => {
            var dialog = OwnedWindows.Cast<Window>().Single(window => window.Title.StartsWith("SQL History", StringComparison.Ordinal));
            try
            {
                var list = ((DockPanel)dialog.Content).Children.OfType<ListBox>().Single();
                require(list.Items.Count == history.Count && list.Items[0] is ListBoxItem { Tag: QueryHistoryEntry }, "History dialog displays dated saved entries newest first");
                CaptureSmokeVisual(dialog, "query-history.png");
                list.SelectedIndex = 0;
                list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
                require(Editor.Text == history[0].Sql, "Double-clicking history restores the complete original SQL");
            }
            catch (Exception ex) { dialogFailure = ex; }
            finally { dialog.Close(); }
        }));
        HistoryClick(this, new RoutedEventArgs());
        if (dialogFailure != null) throw dialogFailure;
        require(UseTarget("mon_db") == "mon_db", "Database switching retains the literal identifier in its USE target");
        var script = new DataTable(); script.Columns.Add("SQL");
        const string firstSql = "sp_addthreshold tempdb,'logsegment',2560512,sp_thresholdaction_25\r\nGO";
        const string secondSql = "select \"literal quotes\"\nGO";
        script.Rows.Add(firstSql); script.Rows.Add(secondSql);
        var scriptTab = AddResult(script, 1);
        Workspace.SelectedItem = scriptTab;
        await Dispatcher.InvokeAsync(() => UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);
        var scriptGrid = ((DockPanel)scriptTab.Content).Children.OfType<DataGrid>().Single();
        scriptGrid.SelectedCells.Add(new DataGridCellInfo(scriptGrid.Items[0], scriptGrid.Columns[0]));
        require(BuildResultCopy(scriptGrid, ResultCopy.Selection, '\t') == firstSql + "\r\n", "The Ctrl+C text producer copies multiline SQL verbatim without surrounding CSV quotes");
        require(BuildResultCopy(scriptGrid, ResultCopy.All, '\t') == firstSql + "\r\n" + secondSql + "\r\n", "Copy all preserves SQL line breaks and literal double quotes");
        require(BuildResultCopy(scriptGrid, ResultCopy.Selection, '\t', delimited: true) == "\"" + firstSql + "\"\r\n", "Explicit delimited Tab copy retains escaped multiline fields");
        CloseResult(scriptTab);
        // Let the parent remove this isolated fixture after verification.
        require(true, "HISTORY_FIXTURE " + Path.GetDirectoryName(HistoryPath));
    }
}
