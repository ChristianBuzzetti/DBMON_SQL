using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void VerifyResultSelectionAndClipboard(DataGrid grid, Action<bool, string> require)
    {
        MenuItem? Item(string name) => grid.ContextMenu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Header.Equals(name));
        string Html() => Clipboard.GetData(DataFormats.Html) as string ?? "";
        require(Item("Select all") != null, "Result menu offers Select all");
        require(Item("Select entire rows") != null, "Result menu offers Select entire rows");
        Item("Select entire rows")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(grid.SelectedCells.Count == 2 && grid.SelectedCells.All(cell => cell.Item == grid.Items[0]), "Select entire rows expands the selected row across visible columns");
        Item("Select all")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(grid.SelectedCells.Count == 4, "Select all marks every result cell");
        grid.UnselectAllCells();
        grid.SelectedCells.Add(new DataGridCellInfo(grid.Items[1], grid.Columns[0]));
        ApplicationCommands.SelectAll.Execute(null, grid);
        require(grid.SelectedCells.Count == 4, "Result SelectAll command marks every result cell");
        require(Item("Copy as formatted table") != null, "Result menu offers Copy as formatted table");
        Item("Copy as formatted table")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(Clipboard.ContainsData(DataFormats.Html) && Clipboard.GetData(DataFormats.Html) is string html && html.Contains("StartFragment:") && html.Contains("<table"), "Formatted table publishes native HTML clipboard data");
        require(Html().Contains("line,one<br>line two"), "Formatted table preserves multiline HTML cell values");
        require(Clipboard.GetText() == BuildResultCopy(grid, ResultCopy.WithHeaders, '\t'), "Formatted table supplies the same selection and headers as plain text fallback");
        grid.UnselectAllCells();
        grid.SelectedCells.Add(new DataGridCellInfo(grid.Items[0], grid.Columns[0]));
        grid.SelectedCells.Add(new DataGridCellInfo(grid.Items[1], grid.Columns[1]));
        Item("Copy as formatted table")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(!Html().Contains("line,one") && Html().Contains("not-selected"), "HTML copy preserves sparse selection holes instead of leaking unselected values");
        Item("Select entire rows")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(grid.SelectedCells.Count == 4, "Select entire rows expands multiple selected rows");
        grid.Columns[1].Visibility = Visibility.Collapsed;
        Item("Copy as formatted table")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(!Html().Contains("second") && !Html().Contains("not-selected"), "HTML copy excludes hidden columns");
        grid.Columns[1].Visibility = Visibility.Visible;
        grid.UnselectAllCells();
        grid.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        require(!Item("Select entire rows")!.IsEnabled && !Item("Copy as formatted table")!.IsEnabled, "Row expansion and formatted-table copy are disabled with no selected cells");
        Item("Select all")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        grid.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        VerifyResultQueryClipboard(grid, require);
        CaptureSmokeVisual(this, "results-select-all.png");
    }

    private void VerifyResultQueryClipboard(DataGrid grid, Action<bool, string> require)
    {
        var toggle = grid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Include executed query"));
        require(toggle.IsCheckable && !toggle.IsChecked, "Including the executed query is optional for each result");
        var tab = (TabItem)grid.GetValue(ResultCopyTabProperty);
        var table = (DataTable)tab.Tag;
        var owner = OwnerOf(tab)!;
        var originalOrigin = owner.Origins[table];
        var originalDocument = Editor.Document;
        int originalCaret = Editor.CaretOffset;
        const string submittedSql = "select '<executed>José & 🐱</executed>'\r\nfrom fixture;";
        var modes = Enum.GetValues<ResultCopy>();
        var delimiters = new[] { '\t', ',', ';', '|' };
        var baseline = modes.SelectMany(mode => delimiters.Select(delimiter =>
            (Mode: mode, Delimiter: delimiter, Text: BuildResultCopy(grid, mode, delimiter, delimited: true)))).ToArray();
        string plainBaseline = BuildResultCopy(grid, ResultCopy.Selection, '\t');
        try
        {
            owner.Origins[table] = (submittedSql, originalOrigin.Source);
            Editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument("select 'edited, never executed';");
            toggle.IsChecked = true;
            foreach (var sample in baseline)
                require(BuildResultCopy(grid, sample.Mode, sample.Delimiter, delimited: true) == ResultHtmlClipboard.QueryAndText(submittedSql, sample.Text),
                    "Query inclusion preserves the result serialization: " + sample.Mode + " / " + (int)sample.Delimiter);
            ApplicationCommands.Copy.Execute(null, grid);
            require(Clipboard.GetText() == ResultHtmlClipboard.QueryAndText(submittedSql, plainBaseline),
                "Ctrl+C includes the recorded SQL rather than the edited buffer");
            grid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Copy as formatted table"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            string html = Clipboard.GetData(DataFormats.Html) as string ?? "";
            require(html.Contains("&lt;executed&gt;José &amp; 🐱&lt;/executed&gt;") &&
                html.IndexOf("Query", StringComparison.Ordinal) < html.IndexOf("<table", StringComparison.Ordinal) &&
                !html.Contains("edited, never executed"), "Formatted copy places escaped submitted SQL above its table");
            require(Clipboard.GetText() == BuildResultCopy(grid, ResultCopy.WithHeaders, '\t'),
                "Rich query-and-table clipboard keeps a matching readable text fallback");
            var previousSession = activeSession;
            try
            {
                activeSession = new SessionWorkspace();
                require(BuildResultCopy(grid, ResultCopy.Selection, '\t') == ResultHtmlClipboard.QueryAndText(submittedSql, plainBaseline),
                    "Query copying resolves the grid's owning session instead of the active session");
            }
            finally { activeSession = previousSession; }
            owner.Origins.Remove(table);
            bool rejected = false;
            try { BuildResultCopy(grid, ResultCopy.All, '\t'); } catch (ArgumentException) { rejected = true; }
            require(rejected, "Missing query provenance cannot fall back to another SQL buffer");
            grid.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            require(!toggle.IsEnabled, "Query inclusion is unavailable when result provenance is missing");
        }
        finally
        {
            owner.Origins[table] = originalOrigin; toggle.IsChecked = false;
            Editor.Document = originalDocument; Editor.CaretOffset = originalCaret;
            grid.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        }
        foreach (var sample in baseline)
            require(BuildResultCopy(grid, sample.Mode, sample.Delimiter, delimited: true) == sample.Text,
                "Turning query inclusion off restores exact previous copy output: " + sample.Mode + " / " + (int)sample.Delimiter);
    }
}
