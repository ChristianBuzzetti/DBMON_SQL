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
        grid.SetValue(ResultCopyTabProperty, tab);
        var includeQuery = new MenuItem {
            Header = "Include executed query", IsCheckable = true, StaysOpenOnClick = true,
            ToolTip = "Include this result's submitted SQL above the output in every copy format, including Ctrl+C"
        };
        includeQuery.Checked += (_, _) => grid.SetValue(IncludeResultQueryProperty, true);
        includeQuery.Unchecked += (_, _) => grid.SetValue(IncludeResultQueryProperty, false);
        menu.Items.Add(includeQuery); menu.Items.Add(new Separator());
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
        var formattedTable = new MenuItem {
            Header = "Copy as formatted table",
            ToolTip = "Copy selected cells with headers as a styled table for Outlook, Teams and other rich-text editors"
        };
        formattedTable.Click += (_, _) => CopyResultHtml(grid);
        selectionActions.Add(formattedTable); menu.Items.Add(formattedTable);
        menu.Items.Add(new Separator());
        menu.Items.Add(CopyItem("Copy all", ResultCopy.All));
        menu.Items.Add(CopyItem("Copy all with headers", ResultCopy.AllWithHeaders));
        var everyResult = new MenuItem { Header = "Copy every result set", ToolTip = "Copy every open result of this session, with headers and a label per set" };
        everyResult.Click += (_, _) => CopyEveryResult();
        menu.Items.Add(everyResult);
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
        var selectAll = new MenuItem { Header = "Select all", InputGestureText = "Ctrl+A" };
        selectAll.Click += (_, _) => grid.SelectAllCells(); menu.Items.Add(selectAll);
        var selectRows = new MenuItem { Header = "Select entire rows" };
        selectRows.Click += (_, _) => SelectEntireResultRows(grid);
        selectionActions.Add(selectRows); menu.Items.Add(selectRows);
        menu.Items.Add(new Separator());
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
            includeQuery.IsEnabled = ResultQuery(grid) != null;
            bool selected = grid.SelectedCells.Count > 0;
            foreach (var item in selectionActions) item.IsEnabled = selected;
            selectAll.IsEnabled = grid.Items.Count > 0 && grid.Columns.Any(column => column.Visibility == Visibility.Visible);
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
        grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, e) => { grid.SelectAllCells(); e.Handled = true; },
            (_, e) => { e.CanExecute = grid.Items.Count > 0; e.Handled = true; }));
        grid.InputBindings.Add(new KeyBinding(ApplicationCommands.SelectAll, Key.A, ModifierKeys.Control));
    }

    private void SelectEntireResultRows(DataGrid grid)
    {
        var rows = grid.SelectedCells.Where(cell => cell.IsValid).Select(cell => cell.Item).ToHashSet();
        if (rows.Count == 0) return;
        if ((long)grid.SelectedCells.Count == (long)rows.Count * grid.Columns.Count) return;
        grid.SetValue(BulkResultSelectionProperty, true);
        try
        {
            // Extend the cell selection directly. Selecting row items separately repeatedly reconciles
            // the whole row selection in WPF; contiguous cell ranges remain compact in SelectedCells.
            foreach (object row in grid.Items)
                if (rows.Contains(row))
                    foreach (var column in grid.Columns)
                    {
                        var cell = new DataGridCellInfo(row, column);
                        if (!grid.SelectedCells.Contains(cell)) grid.SelectedCells.Add(cell);
                    }
        }
        finally
        {
            grid.SetValue(BulkResultSelectionProperty, false);
            UpdateResultSelectionSummary(grid);
        }
    }

    private static readonly DependencyProperty BulkResultSelectionProperty =
        DependencyProperty.RegisterAttached("BulkResultSelection", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private static readonly DependencyProperty IncludeResultQueryProperty =
        DependencyProperty.RegisterAttached("IncludeResultQuery", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    private static readonly DependencyProperty ResultCopyTabProperty =
        DependencyProperty.RegisterAttached("ResultCopyTab", typeof(TabItem), typeof(MainWindow));

    private string? ResultQuery(DataGrid grid)
    {
        if (grid.GetValue(ResultCopyTabProperty) is not TabItem { Tag: DataTable table } tab ||
            (grid.ItemsSource as DataView)?.Table != table || OwnerOf(tab) is not { } owner ||
            !owner.Origins.TryGetValue(table, out var origin) || string.IsNullOrWhiteSpace(origin.Sql)) return null;
        return origin.Sql;
    }

    private string? ResultQueryToCopy(DataGrid grid) => !(bool)grid.GetValue(IncludeResultQueryProperty) ? null :
        ResultQuery(grid) ?? throw new ArgumentException("The executed query is not available for this result.");

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

    private sealed record ResultCopyData(string[] Headers, IEnumerable<IReadOnlyList<object?>> Rows);

    private static ResultCopyData ResultCopyValues(DataGrid grid, ResultCopy mode)
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
        return new ResultCopyData(headers, rows);
    }

    private string BuildResultCopy(DataGrid grid, ResultCopy mode, char delimiter, bool delimited = false)
    {
        var values = ResultCopyValues(grid, mode);
        string text = DelimitedText.Serialize(values.Headers, values.Rows, delimiter, includeHeaders: mode is ResultCopy.WithHeaders or ResultCopy.AllWithHeaders, headersOnly: mode == ResultCopy.HeadersOnly, quoteFields: delimited);
        return ResultQueryToCopy(grid) is { } query ? ResultHtmlClipboard.QueryAndText(query, text) : text;
    }

    private void CopyResultHtml(DataGrid grid)
    {
        try
        {
            var values = ResultCopyValues(grid, ResultCopy.WithHeaders);
            string html = ResultQueryToCopy(grid) is { } query
                ? ResultHtmlClipboard.QueryAndTable(query, values.Headers, values.Rows)
                : ResultHtmlClipboard.Table(values.Headers, values.Rows);
            var data = new DataObject();
            data.SetData(DataFormats.Html, ResultHtmlClipboard.Payload(html));
            data.SetData(DataFormats.UnicodeText, BuildResultCopy(grid, ResultCopy.WithHeaders, '\t'));
            Clipboard.SetDataObject(data, true);
            StatusText.Text = "Copied formatted table · paste into a rich-text editor";
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        { ShowError("Unable to copy: " + ex.Message); }
    }

    // A script that returns sixteen results is sixteen copies by hand; this takes the lot in one action.
    private const int MaxEveryResultChars = 4 * 1024 * 1024;
    private void CopyEveryResult()
    {
        try
        {
            var sets = Workspace.Items.OfType<TabItem>()
                .Where(tab => tab.Tag is DataTable && tab.Content is DockPanel)
                .Select(tab => (Tab: tab, Grid: ((DockPanel)tab.Content).Children.OfType<DataGrid>().FirstOrDefault()))
                .Where(entry => entry.Grid != null)
                .Select(entry => (Label: ResultName(entry.Tab) + " · " + Counts.Of(((DataTable)entry.Tab.Tag).Rows.Count, "row"),
                                  Text: BuildResultCopy(entry.Grid!, ResultCopy.AllWithHeaders, '	')));
            var copy = ResultFormats.Sets(sets, MaxEveryResultChars);
            if (copy.Sets == 0) { ShowError("No result sets are open."); return; }
            Clipboard.SetText(copy.Text);
            StatusText.Text = "Copied " + Counts.Of(copy.Sets, "result set") + " to clipboard" + (copy.Complete ? "" : " · stopped at 4 MiB; later results were not copied");
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        { ShowError("Unable to copy: " + ex.Message); }
    }

    private void CopyResult(DataGrid grid, ResultCopy mode, char delimiter, bool delimited = false)
    {
        try { string text = BuildResultCopy(grid, mode, delimiter, delimited); Clipboard.SetText(text); StatusText.Text = "Copied to clipboard · " + mode; }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or InvalidOperationException)
        { ShowError("Unable to copy: " + ex.Message); }
    }
}
