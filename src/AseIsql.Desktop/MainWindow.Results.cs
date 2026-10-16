using System.Data;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private enum ResultCopy { Selection, WithHeaders, HeadersOnly, All, AllWithHeaders }

    private void AttachResultActions(TabItem tab, DataGrid grid)
    {
        var tabMenu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => RenameResult(tab); tabMenu.Items.Add(rename);
        var close = new MenuItem { Header = "Close resultset" };
        close.Click += (_, _) => CloseResult(tab); tabMenu.Items.Add(close);
        tab.ContextMenu = tabMenu;
        tab.PreviewMouseDown += (_, e) => {
            // Only a click on the tab header closes it, never on its result grid.
            if (e.ChangedButton == MouseButton.Middle && tab.Template.FindName("Frame", tab) is Border frame &&
                e.OriginalSource is DependencyObject source && (source == frame || frame.IsAncestorOf(source)))
            { CloseResult(tab); e.Handled = true; }
            // Double-click on the header renames the result, as in a spreadsheet tab.
            else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && tab.Template.FindName("Frame", tab) is Border header &&
                e.OriginalSource is DependencyObject clicked && (clicked == header || header.IsAncestorOf(clicked)))
            { e.Handled = true; Dispatcher.BeginInvoke(() => RenameResult(tab)); }
        };
        var menu = new ContextMenu();
        var selectionActions = new List<MenuItem>();
        MenuItem CopyItem(string header, ResultCopy mode, char delimiter = '\t')
        {
            var item = new MenuItem { Header = header };
            if (mode is ResultCopy.Selection or ResultCopy.WithHeaders) selectionActions.Add(item);
            item.Click += (_, _) => CopyResult(grid, mode, delimiter); return item;
        }
        menu.Items.Add(CopyItem("Copy", ResultCopy.Selection));
        menu.Items.Add(CopyItem("Copy with headers", ResultCopy.WithHeaders));
        menu.Items.Add(CopyItem("Copy headers only", ResultCopy.HeadersOnly));
        menu.Items.Add(new Separator());
        menu.Items.Add(CopyItem("Copy all", ResultCopy.All));
        menu.Items.Add(CopyItem("Copy all with headers", ResultCopy.AllWithHeaders));
        var delimited = new MenuItem { Header = "Copy delimited" };
        foreach (var (name, delimiter) in new[] { ("Tab", '\t'), ("Comma (CSV)", ','), ("Semicolon", ';'), ("Pipe", '|') })
        {
            var option = new MenuItem { Header = name };
            option.Items.Add(CopyItem("Selection", ResultCopy.Selection, delimiter));
            option.Items.Add(CopyItem("Selection with headers", ResultCopy.WithHeaders, delimiter));
            option.Items.Add(CopyItem("All with headers", ResultCopy.AllWithHeaders, delimiter));
            delimited.Items.Add(option);
        }
        menu.Items.Add(delimited); menu.Items.Add(new Separator());
        var closeFromGrid = new MenuItem { Header = "Close resultset" };
        closeFromGrid.Click += (_, _) => CloseResult(tab); menu.Items.Add(closeFromGrid);
        menu.Opened += (_, _) => {
            bool selected = grid.SelectedCells.Count > 0;
            foreach (var item in selectionActions) item.IsEnabled = selected;
        };
        grid.ContextMenu = menu;
        grid.PreviewMouseRightButtonDown += (_, e) => {
            var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell == null) return;
            SelectContextCell(grid, cell.DataContext, cell.Column); cell.Focus();
        };
        grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopyResult(grid, ResultCopy.Selection, '\t'); e.Handled = true; },
            (_, e) => { e.CanExecute = grid.SelectedCells.Count > 0; e.Handled = true; }));
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node != null)
        {
            if (node is T found) return found;
            node = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private static void SelectContextCell(DataGrid grid, object row, DataGridColumn column)
    {
        var info = new DataGridCellInfo(row, column);
        if (!grid.SelectedCells.Contains(info)) { grid.UnselectAllCells(); grid.SelectedCells.Add(info); }
        grid.CurrentCell = info;
    }

    private void CloseResult(TabItem tab)
    {
        if (tab.Tag is not DataTable || !Workspace.Items.Contains(tab)) return;
        bool active = tab.IsSelected; int index = Workspace.Items.IndexOf(tab);
        pinnedResults.Remove((DataTable)tab.Tag); resultOrigins.Remove((DataTable)tab.Tag); diffSources.Remove((DataTable)tab.Tag); resultRuns.Remove((DataTable)tab.Tag);
        Workspace.Items.Remove(tab);
        if (active) Workspace.SelectedIndex = Math.Max(0, Math.Min(index, Workspace.Items.Count - 1));
        if (Workspace.SelectedItem == QueryTab) { Editor.Focus(); StatusText.Text = "Result closed · Query · " + SourceLabel; }
    }

    private static string BuildResultCopy(DataGrid grid, ResultCopy mode, char delimiter)
    {
        bool all = mode is ResultCopy.All or ResultCopy.AllWithHeaders, headersOnly = mode == ResultCopy.HeadersOnly;
        var selected = all || headersOnly ? [] : grid.SelectedCells.Where(c => c.IsValid && c.Item is DataRowView).ToArray();
        if (!all && !headersOnly && selected.Length == 0) throw new ArgumentException("Select cells to copy.");
        var selectedColumns = selected.Select(cell => cell.Column).ToHashSet();
        var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible && (all || headersOnly || selectedColumns.Contains(c))).OrderBy(c => c.DisplayIndex).ToArray();
        var selectedPairs = selected.Select(c => (((DataRowView)c.Item).Row, c.Column)).ToHashSet();
        var selectedRows = selectedPairs.Select(p => p.Row).ToHashSet();
        var items = grid.Items.Cast<object>().OfType<DataRowView>().Where(row => all || selectedRows.Contains(row.Row));
        var ordinals = columns.Select(column => grid.Columns.IndexOf(column)).ToArray();
        // Headers come from the source captions: a display placeholder such as "(no column name)" is not data.
        var source = (grid.ItemsSource as DataView)?.Table;
        var headers = columns.Select((column, index) => source != null ? source.Columns[ordinals[index]].Caption : column.Header?.ToString() ?? "").ToArray();
        var rows = headersOnly ? Enumerable.Empty<IReadOnlyList<object?>>() : items.Select(row => (IReadOnlyList<object?>)columns.Select((column, index) =>
            all || selectedPairs.Contains((row.Row, column)) ? row.Row[ordinals[index]] : null).ToArray());
        return DelimitedText.Serialize(headers, rows, delimiter, includeHeaders: mode is ResultCopy.WithHeaders or ResultCopy.AllWithHeaders, headersOnly: headersOnly);
    }

    private void CopyResult(DataGrid grid, ResultCopy mode, char delimiter)
    {
        try { string text = BuildResultCopy(grid, mode, delimiter); Clipboard.SetText(text); StatusText.Text = "Copied to clipboard · " + mode; }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        { ShowError("Unable to copy: " + ex.Message); }
    }
}
