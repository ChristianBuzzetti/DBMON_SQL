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
        MenuItem CopyItem(string header, ResultCopy mode, char delimiter = '\t', bool delimited = false)
        {
            var item = new MenuItem { Header = header };
            if (mode is ResultCopy.Selection or ResultCopy.WithHeaders) selectionActions.Add(item);
            item.Click += (_, _) => CopyResult(grid, mode, delimiter, delimited); return item;
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
            option.Items.Add(CopyItem("Selection", ResultCopy.Selection, delimiter, delimited: true));
            option.Items.Add(CopyItem("Selection with headers", ResultCopy.WithHeaders, delimiter, delimited: true));
            option.Items.Add(CopyItem("All with headers", ResultCopy.AllWithHeaders, delimiter, delimited: true));
            delimited.Items.Add(option);
        }
        menu.Items.Add(delimited); menu.Items.Add(new Separator());
        var viewCell = new MenuItem { Header = "View cell text…" };
        viewCell.Click += (_, _) => OpenSelectedCellText(grid);
        selectionActions.Add(viewCell); menu.Items.Add(viewCell);
        var resetRowHeight = new MenuItem { Header = "Auto-fit row height" };
        resetRowHeight.Click += (_, _) => { foreach (var row in VisualChildren<DataGridRow>(grid)) row.Height = double.NaN; };
        menu.Items.Add(resetRowHeight);
        menu.Items.Add(new Separator());
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
        AttachRowResize(grid);
        grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { CopyResult(grid, ResultCopy.Selection, '\t'); e.Handled = true; },
            (_, e) => { e.CanExecute = grid.SelectedCells.Count > 0; e.Handled = true; }));
    }

    private void OpenSelectedCellText(DataGrid grid)
    {
        var cell = grid.SelectedCells.FirstOrDefault(c => c.IsValid && c.Item is DataRowView);
        if (cell.Item is not DataRowView row) return;
        int ordinal = grid.Columns.IndexOf(cell.Column); if (ordinal < 0 || ordinal >= row.Row.Table.Columns.Count) return;
        var column = row.Row.Table.Columns[ordinal];
        string text = row[ordinal] is DBNull ? "NULL" : row[ordinal] is byte[] bytes ? "0x" + Convert.ToHexString(bytes) : Convert.ToString(row[ordinal], System.Globalization.CultureInfo.InvariantCulture) ?? "";
        TextWindow((string.IsNullOrWhiteSpace(column.Caption) ? UnnamedColumn : column.Caption) + " [" + column.DataType.Name + "]", text);
    }

    // Because RowHeader is hidden for density, the bottom 5 px of each DataGridRow acts as its vertical resize handle:
    // dragging stretches or shrinks the row, double-clicking the edge restores auto-height, and double-clicking inside
    // a long or multiline cell opens its full text in a scrollable window.
    private void AttachRowResize(DataGrid grid)
    {
        DataGridRow? resizingRow = null; double startY = 0, startHeight = 0;
        static DataGridRow? BottomEdgeRow(DataGrid owner, MouseEventArgs e)
        {
            var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null || !owner.IsAncestorOf(row)) return null;
            double y = e.GetPosition(row).Y;
            return y >= Math.Max(0, row.ActualHeight - 5) && y <= row.ActualHeight + 2 ? row : null;
        }
        grid.PreviewMouseMove += (_, e) => {
            if (resizingRow != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed) { resizingRow = null; grid.ReleaseMouseCapture(); grid.Cursor = null; return; }
                double min = double.IsNaN(grid.MinRowHeight) || grid.MinRowHeight <= 0 ? 24 : grid.MinRowHeight;
                resizingRow.Height = Math.Max(min, startHeight + (e.GetPosition(grid).Y - startY));
                e.Handled = true; return;
            }
            grid.Cursor = BottomEdgeRow(grid, e) != null ? Cursors.SizeNS : null;
        };
        grid.PreviewMouseLeftButtonDown += (_, e) => {
            if (BottomEdgeRow(grid, e) is { } edgeRow)
            {
                if (e.ClickCount == 2) { edgeRow.Height = double.NaN; e.Handled = true; return; }
                resizingRow = edgeRow; startY = e.GetPosition(grid).Y; startHeight = edgeRow.ActualHeight;
                grid.CaptureMouse(); e.Handled = true; return;
            }
            if (e.ClickCount == 2 && FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject) is { DataContext: DataRowView rowView, Column: { } col })
            {
                int ordinal = grid.Columns.IndexOf(col);
                if (ordinal >= 0 && ordinal < rowView.Row.Table.Columns.Count)
                {
                    string text = Convert.ToString(rowView[ordinal], System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    if (text.Length > 120 || text.IndexOfAny(['\r', '\n']) >= 0)
                    { SelectContextCell(grid, rowView, col); OpenSelectedCellText(grid); e.Handled = true; }
                }
            }
        };
        grid.PreviewMouseLeftButtonUp += (_, e) => {
            if (resizingRow == null) return;
            resizingRow = null; grid.ReleaseMouseCapture(); grid.Cursor = null; e.Handled = true;
        };
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
        if (tab.Tag is not DataTable table || OwnerOf(tab) is not { } owner) return;
        bool mounted = IsOnScreen(owner), selected = mounted && tab.IsSelected;
        int index = mounted ? Workspace.Items.IndexOf(tab) : 0;
        owner.Pins.Remove(table); owner.Origins.Remove(table); owner.Diffs.Remove(table); owner.Runs.Remove(table);
        UnmountResultTab(owner, tab);
        if (owner.Tabs.Count == 0) owner.ResultCounter = 0;
        if (ReferenceEquals(owner.SelectedTab, tab)) owner.SelectedTab = null;
        if (!mounted) return;
        if (selected) Workspace.SelectedIndex = Math.Max(0, Math.Min(index, Workspace.Items.Count - 1));
        if (Workspace.SelectedItem == QueryTab) { Editor.Focus(); StatusText.Text = "Result closed · Query · " + SourceLabel; }
    }

    private static string BuildResultCopy(DataGrid grid, ResultCopy mode, char delimiter, bool delimited = false)
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
        return DelimitedText.Serialize(headers, rows, delimiter, includeHeaders: mode is ResultCopy.WithHeaders or ResultCopy.AllWithHeaders, headersOnly: headersOnly, quoteFields: delimited);
    }

    private void CopyResult(DataGrid grid, ResultCopy mode, char delimiter, bool delimited = false)
    {
        try { string text = BuildResultCopy(grid, mode, delimiter, delimited); Clipboard.SetText(text); StatusText.Text = "Copied to clipboard · " + mode; }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        { ShowError("Unable to copy: " + ex.Message); }
    }
}
