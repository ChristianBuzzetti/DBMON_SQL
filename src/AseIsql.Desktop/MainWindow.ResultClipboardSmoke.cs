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
        CaptureSmokeVisual(this, "results-select-all.png");
    }
}
