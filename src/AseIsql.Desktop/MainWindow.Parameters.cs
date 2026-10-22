using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AseIsql.Desktop;

public partial class MainWindow
{
    // Values typed for :name placeholders during this session only; never written to disk.
    private Dictionary<string,string> parameterValues => activeSession.Parameters;

    // Shows the parameter form when sql has placeholders. Returns the filled SQL, the original text for "as written",
    // or null when the user cancels. SQL without placeholders comes back unchanged without a dialog.
    private string? FillParameters(string sql,string context,string action)
    {
        if(SqlParameters.Names(sql).Count==0)return sql;
        var form=new ParametersWindow(sql,context,action,DefaultParameter) {Owner=this};
        if(form.ShowDialog()!=true)return null;
        foreach(var (name,value) in form.Values)parameterValues[name]=value;
        return form.Result;
    }
    private string DefaultParameter(string name)
    {
        if(parameterValues.TryGetValue(name,out var remembered))return remembered;
        var facts=(engine as ISessionFacts)?.Facts;
        return name.ToLowerInvariant() switch {
            "database" or "db" or "orig_db"=>facts?.Database??"",
            "interval"=>"00:00:10",
            "min_spid"=>"0",
            "idle_seconds"=>"3600",
            "open_minutes"=>"30",
            "min_change_pct"=>"10",
            "pattern"=>"%",
            "since"=>DateTime.Today.AddDays(-1).ToString("yyyyMMdd",CultureInfo.InvariantCulture),
            _=>""
        };
    }
    private void FillParametersClick(object sender,RoutedEventArgs e)
    {
        bool selection=Editor.SelectionLength>0;string source=selection?Editor.SelectedText:Editor.Text;
        if(SqlParameters.Names(source).Count==0){StatusText.Text="No :name parameters in the "+(selection?"selection":"query");return;}
        string? filled=FillParameters(source,selection?"Selection":"Query","Replace");if(filled==null||filled==source)return;
        // One document change, so a single Undo restores the placeholders.
        if(selection)Editor.Document.Replace(Editor.SelectionStart,Editor.SelectionLength,filled);else Editor.Document.Replace(0,Editor.Document.TextLength,filled);
        StatusText.Text="Parameters filled · Ctrl+Z restores the placeholders";
    }
}

// Form for :name placeholders: one field per distinct name with a hint, a live preview of the resulting SQL and the
// reason a value is refused. The primary action stays disabled until every value is acceptable where it is used.
public sealed class ParametersWindow:Window
{
    private readonly string sql;
    public Dictionary<string,TextBox> Fields { get; }=new(StringComparer.OrdinalIgnoreCase);
    public string? Result {get;private set;}
    public Dictionary<string,string> Values=>Fields.ToDictionary(f=>f.Key,f=>f.Value.Text,StringComparer.OrdinalIgnoreCase);
    public Button Primary { get; }
    private readonly TextBox preview=new() {IsReadOnly=true,AcceptsReturn=true,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=170};
    private readonly TextBlock problem=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};

    private static readonly Dictionary<string,string> Hints=new(StringComparer.OrdinalIgnoreCase) {
        ["interval"]="Wait between samples, hh:mm:ss",["spid"]="Server process id",["min_spid"]="Lowest SPID to list; 0 lists all",
        ["idle_seconds"]="Seconds a session has been idle",["open_minutes"]="Minutes a transaction has been open",["database"]="Database name",
        ["databases"]="Comma-separated database names",["table"]="Table name, optionally owner.table",["object"]="Object name",["login"]="Login name",
        ["pattern"]="LIKE pattern, % and _ wildcards",["since"]="Date as YYYYMMDD",["orig_db"]="Source database",["xactname"]="Transaction name",
        ["min_change_pct"]="Minimum datachange() percentage",["date_column"]="Date column name",["history_table"]="History table name"};

    public ParametersWindow(string source,string context,string action,Func<string,string> defaults)
    {
        sql=source;SetResourceReference(StyleProperty,typeof(Window));TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        Title="Parameters · "+context;Width=640;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;ResizeMode=ResizeMode.CanResizeWithGrip;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var root=new DockPanel {Margin=new Thickness(20,18,20,16)};
        var title=new TextBlock {FontWeight=FontWeights.SemiBold};var mark=new Run("◤ ");mark.SetResourceReference(TextElement.ForegroundProperty,"Accent");title.Inlines.Add(mark);title.Inlines.Add(new Run("Fill parameters"));title.SetResourceReference(TextBlock.FontSizeProperty,"SectionFontSize");DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var lead=new TextBlock {Text=context+" uses :name placeholders. Values inside quotes are escaped; values outside quotes must be one name or number.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,14)};lead.SetResourceReference(TextBlock.ForegroundProperty,"Muted");lead.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");DockPanel.SetDock(lead,Dock.Top);root.Children.Add(lead);

        var footer=new DockPanel {Margin=new Thickness(0,14,0,0)};
        Primary=new Button {Content=action,IsDefault=true,MinWidth=96,Margin=new Thickness(8,0,0,0),FontWeight=FontWeights.SemiBold};Primary.SetResourceReference(BackgroundProperty,"Accent");Primary.SetResourceReference(ForegroundProperty,"OnAccent");Primary.SetResourceReference(BorderBrushProperty,"Accent");
        var asWritten=new Button {Content=action+" as written",Margin=new Thickness(8,0,0,0),ToolTip="Keep the :name placeholders unchanged"};
        var cancel=new Button {Content="Cancel",IsCancel=true};
        foreach(var button in new[]{Primary,asWritten,cancel}){DockPanel.SetDock(button,Dock.Right);footer.Children.Add(button);}
        footer.Children.Add(new Border());DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

        var body=new StackPanel();
        var fields=new Grid();fields.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});fields.ColumnDefinitions.Add(new ColumnDefinition());
        int row=0;
        foreach(string name in SqlParameters.Names(sql))
        {
            fields.RowDefinitions.Add(new RowDefinition());
            var label=new TextBlock {Text=":"+name,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,8,14,0)};label.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");label.SetResourceReference(TextBlock.ForegroundProperty,"Accent");Grid.SetRow(label,row);fields.Children.Add(label);
            var cell=new StackPanel {Margin=new Thickness(0,0,0,10)};var box=new TextBox {Text=defaults(name)};box.SetResourceReference(Control.FontFamilyProperty,"MonoFont");cell.Children.Add(box);
            var hint=new TextBlock {Text=Hints.GetValueOrDefault(name,"Value for :"+name)+(IsQuoted(name)?" · quoted in the SQL":" · used as a name or number"),Margin=new Thickness(2,3,0,0)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");hint.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");cell.Children.Add(hint);
            Grid.SetRow(cell,row);Grid.SetColumn(cell,1);fields.Children.Add(cell);
            box.TextChanged+=(_,_)=>Refresh();Fields[name]=box;row++;
        }
        body.Children.Add(fields);
        var previewLabel=new TextBlock {Text="PREVIEW",Margin=new Thickness(0,6,0,6)};previewLabel.SetResourceReference(StyleProperty,"SectionLabel");body.Children.Add(previewLabel);
        preview.SetResourceReference(Control.FontFamilyProperty,"MonoFont");body.Children.Add(preview);
        problem.SetResourceReference(TextBlock.ForegroundProperty,"ErrorInk");problem.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");body.Children.Add(problem);
        root.Children.Add(body);Content=root;

        Primary.Click+=(_,_)=> {if(Build() is {} filled){Result=filled;DialogResult=true;}};
        asWritten.Click+=(_,_)=> {Result=sql;DialogResult=true;};
        Loaded+=(_,_)=> {var first=Fields.Values.FirstOrDefault(f=>f.Text.Length==0)??Fields.Values.FirstOrDefault();first?.Focus();first?.SelectAll();};
        PreviewKeyDown+=(_,key)=> {if(key.Key==Key.Escape)Close();};
        Refresh();
    }
    private bool IsQuoted(string name)=>SqlParameters.Scan(sql).Where(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).All(p=>p.Quote!='\0');
    // The filled SQL, or null with the first refusal shown under the preview.
    public string? Build()
    {
        foreach(var (name,box) in Fields)
            if(SqlParameters.Problem(sql,name,box.Text) is {} reason){problem.Text=reason;return null;}
        problem.Text="";return SqlParameters.Apply(sql,Values);
    }
    private void Refresh()
    {
        string? filled=Build();Primary.IsEnabled=filled!=null;
        preview.Text=filled??sql;
        foreach(var (name,box) in Fields)box.SetResourceReference(Control.BorderBrushProperty,SqlParameters.Problem(sql,name,box.Text)==null?"FaintBorder":"ErrorInk");
    }
}
