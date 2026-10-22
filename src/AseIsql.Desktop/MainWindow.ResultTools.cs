using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    private void ClearResults(bool includePinned = false) => ClearResults(activeSession, includePinned);
    private void ClearResults(SessionWorkspace owner, bool includePinned)
    {
        foreach (var tab in owner.Tabs.Where(t => t.Tag is DataTable table && (includePinned || !owner.Pins.Contains(table))).ToArray()) CloseResult(tab);
    }
    private void TogglePin(TabItem tab) => TogglePin(OwnerOf(tab) ?? activeSession, tab);
    private void TogglePin(SessionWorkspace owner, TabItem tab)
    {
        if (tab.Tag is not DataTable table) return;
        if (owner.Pins.Remove(table)) tab.Header = tab.Header.ToString()!.Replace("📌 ", "");
        else {
            if (owner.Pins.Count >= 8) { ShowError("You can pin up to 8 results."); return; }
            long bytes = 0;
            foreach (var candidate in owner.Pins.Append(table))
                foreach (DataRow row in candidate.Rows)
                    foreach (object? value in row.ItemArray) bytes += value is string s ? s.Length * 2L : value is byte[] binary ? binary.Length : 16;
            if (bytes > 64 * 1024 * 1024) { ShowError("Pinned results exceed 64 MiB of estimated values."); return; }
            owner.Pins.Add(table); tab.Header = "📌 " + tab.Header;
        }
    }
    private static DataTable VisibleResult(DataGrid grid)
    {
        var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex).ToArray();
        var rows = grid.Items.OfType<DataRowView>().ToArray();
        var source = ((DataView)grid.ItemsSource).Table!;
        var copy = new DataTable();
        foreach (var column in columns) {
            var original = source.Columns[grid.Columns.IndexOf(column)];
            var added = copy.Columns.Add(original.ColumnName, original.DataType); added.Caption = original.Caption; added.ExtendedProperties["SourceName"] = original.ExtendedProperties["SourceName"] ?? original.Caption;
        }
        foreach (var row in rows) copy.Rows.Add(columns.Select(c => row.Row[grid.Columns.IndexOf(c)]).ToArray());
        return copy;
    }
    private void AddResultTools(TabItem tab, DataGrid grid, DockPanel body, TextBlock footer)
    {
        var panel = new DockPanel { Margin = new Thickness(10,6,10,6), LastChildFill = false };
        var filter = new TextBox { Width = 220, ToolTip = "Filter values locally without querying the server", Padding = new Thickness(6,4,6,4), Margin = new Thickness(0,0,12,0) };
        panel.Children.Add(new TextBlock { Text = "Filter:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) }); panel.Children.Add(filter);
        filter.TextChanged += (_,_) => {
            if (tab.Tag is not DataTable table) return;
            string pattern = string.Concat(filter.Text.Select(c => c switch { '\'' => "''", '[' => "[[]", ']' => "[]]", '*' => "[*]", '%' => "[%]", _ => c.ToString() }));
            try {
                table.DefaultView.RowFilter = filter.Text.Length == 0 ? "" : string.Join(" OR ", table.Columns.Cast<DataColumn>().Where(c => c.DataType != typeof(byte[])).Select(c =>
                    "Convert([" + c.ColumnName.Replace("\\","\\\\").Replace("]","\\]") + "], 'System.String') LIKE '%" + pattern + "%'"));
                footer.Text = resultOrigins[table].Source + " · " + table.DefaultView.Count.ToString("N0", CultureInfo.InvariantCulture) + " / " + Counts.Of(table.Rows.Count, "row") + " · local filter";
            } catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or FormatException) { StatusText.Text = ex.Message; }
        };
        var pin = new Button { Content = "📌 Pin", Padding = new Thickness(8,4,8,4) }; pin.Click += (_,_) => TogglePin(tab); panel.Children.Add(pin);
        var detail = new Button { Content = "Row details", Padding = new Thickness(8,4,8,4) };
        detail.Click += (_,_) => { if (grid.CurrentCell.Item is DataRowView row) TextWindow("Row details", string.Join(Environment.NewLine + Environment.NewLine, row.Row.Table.Columns.Cast<DataColumn>().Select(c => c.Caption + " [" + c.DataType.Name + "]\n" + (row[c.Ordinal] is DBNull ? "NULL" : row[c.Ordinal] is byte[] bytes ? "0x" + Convert.ToHexString(bytes) : Convert.ToString(row[c.Ordinal],CultureInfo.InvariantCulture))))); };
        panel.Children.Add(detail);
        DockPanel.SetDock(panel, Dock.Top); body.Children.Insert(0,panel);
        grid.SelectedCellsChanged += (_,_) => {
            decimal sum=0, min=decimal.MaxValue, max=decimal.MinValue; int count=0;
            try { foreach(var cell in grid.SelectedCells) if(cell.Item is DataRowView row) {
                object value=row[grid.Columns.IndexOf(cell.Column)];
                if(value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double) { decimal n=Convert.ToDecimal(value,CultureInfo.InvariantCulture); sum+=n; min=Math.Min(min,n); max=Math.Max(max,n); count++; }
            }
            StatusText.Text=count==0 ? grid.SelectedCells.Count+" selected cells" : $"Numeric values: {count} · SUM {sum} · AVG {sum/count} · MIN {min} · MAX {max}";
            } catch(OverflowException) { StatusText.Text="Aggregates exceed the decimal range; original values are preserved."; }
        };
        var menu=grid.ContextMenu;
        menu.Items.Add(new Separator());
        void Action(string name, System.Action execute) { var item=new MenuItem { Header=name }; item.Click+=(_,_)=> { try { execute(); } catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException) { ShowError(ex.Message); } }; menu.Items.Add(item); }
        Action("Rename resultset…", ()=>RenameResult(tab));
        Action("Pin / unpin result", ()=>TogglePin(tab));
        Action("View source SQL", ()=>TextWindow("SQL · "+resultOrigins[(DataTable)tab.Tag].Source,resultOrigins[(DataTable)tab.Tag].Sql));
        Action("Freeze first column", ()=>grid.FrozenColumnCount=grid.FrozenColumnCount==0?1:0);
        Action("Bar chart…", ()=>ChartResult(grid,tab));
        Action("Transpose result", ()=> { var origin=resultOrigins[(DataTable)tab.Tag]; AddResult(ResultFormats.Transpose(VisibleResult(grid)),Workspace.Items.Count,origin.Source,origin.Sql,ResultName(tab)+" · transposed"); Workspace.SelectedIndex=Workspace.Items.Count-1; });
        foreach(string format in new[]{"JSON","Markdown","HTML report","SQL IN list","SQL INSERT"})
            Action("Export "+format+"…", ()=>ExportResult(grid,tab,format));
    }
    // The tab name is the user's label for the result; data, source SQL and provenance never change with it.
    private bool RenameResult(TabItem tab, string? name = null)
    {
        if (tab.Tag is not DataTable table || !Workspace.Items.Contains(tab)) return false;
        string current = ResultName(tab);
        name ??= AskText("Rename resultset", "Name for this result tab. Its data, source SQL and server stay unchanged.", current);
        name = string.Join(" ", (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0 || name == current) return false;
        if (name.Length > MaxResultName) name = name[..MaxResultName];
        tab.Header = (pinnedResults.Contains(table) ? "📌 " : "") + name;
        StatusText.Text = "Result renamed · " + name;
        return true;
    }
    private void ChartResult(DataGrid grid,TabItem tab)
    {
        var source=((DataView)grid.ItemsSource).Table!;
        var numeric=source.Columns.Cast<DataColumn>().Where(c=>c.DataType==typeof(decimal)||c.DataType==typeof(double)||c.DataType==typeof(float)||c.DataType==typeof(int)||c.DataType==typeof(long)||c.DataType==typeof(short)||c.DataType==typeof(byte)).ToArray();
        if(numeric.Length==0)throw new ArgumentException("The result has no numeric columns.");
        var window=new Window {Owner=this,Title="Bar chart · "+resultOrigins[(DataTable)tab.Tag].Source,Width=850,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new DockPanel {Margin=new Thickness(18)};
        var selector=new ComboBox {ItemsSource=numeric,DisplayMemberPath="Caption",SelectedIndex=0,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(selector,Dock.Top);panel.Children.Add(selector);
        var hint=new TextBlock {Text="First 80 rows of the current view · length = magnitude · red = negative",Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(hint,Dock.Top);panel.Children.Add(hint);
        var bars=new StackPanel();var scroll=new ScrollViewer {Content=bars,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};panel.Children.Add(scroll);
        void Render() {
            if(selector.SelectedItem is not DataColumn column)return;
            var rows=grid.Items.OfType<DataRowView>().Take(80).Select(row=>(Label:Convert.ToString(row[0],CultureInfo.InvariantCulture)??"",Value:row[column.Ordinal] is DBNull?double.NaN:Convert.ToDouble(row[column.Ordinal],CultureInfo.InvariantCulture))).Where(row=>double.IsFinite(row.Value)).ToArray();
            double maximum=rows.Length==0?0:rows.Max(row=>Math.Abs(row.Value));bars.Children.Clear();
            foreach(var row in rows) {
                var line=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
                line.Children.Add(new TextBlock {Text=row.Label,Width=180,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=row.Label});
                var bar=new Border {Width=maximum==0?1:Math.Max(1,Math.Abs(row.Value)/maximum*440),Height=16,Margin=new Thickness(0,0,12,0)};bar.SetResourceReference(Border.BackgroundProperty,row.Value<0?"ErrorInk":"Accent");line.Children.Add(bar);
                line.Children.Add(new TextBlock {Text=row.Value.ToString("G",CultureInfo.InvariantCulture)});bars.Children.Add(line);
            }
            if(rows.Length==0)bars.Children.Add(new TextBlock {Text="No finite values to plot."});
        }
        selector.SelectionChanged+=(_,_)=>Render();window.Content=panel;window.Loaded+=(_,_)=>Render();window.Show();
    }
    private void ExportResult(DataGrid grid, TabItem tab, string format)
    {
        var table=VisibleResult(grid); var origin=resultOrigins[(DataTable)tab.Tag]; string? target=null;
        if(format=="SQL INSERT") { target=AskText("Generate INSERT (does not execute)","Target table: owner.table or database.owner.table"); if(target==null)return; }
        string text=format switch { "JSON"=>ResultFormats.Json(table), "Markdown"=>ResultFormats.Markdown(table), "HTML report"=>ResultFormats.Html(table,origin.Sql,ResultName(tab)+" · "+origin.Source), "SQL IN list"=>ResultFormats.InList(table), _=>ResultFormats.Inserts(table,target!) };
        string extension=format=="JSON"?"json":format=="Markdown"?"md":format=="HTML report"?"html":"sql";
        var picker=new SaveFileDialog { Filter="File "+extension+"|*."+extension, DefaultExt="."+extension, FileName=FileStem(ResultName(tab))+"."+extension };
        if(picker.ShowDialog(this)==true) { AtomicFiles.SaveUtf8(picker.FileName,text); StatusText.Text="Exported · "+format+" · filtered view"; }
    }
}
