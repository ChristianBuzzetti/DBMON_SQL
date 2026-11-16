using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AseIsql.Desktop;

/// <summary>Help → About, as in BuzzPrint: a centred card with the living mark, the name and version chip, what the app
/// does, and the credits as label/value rows. Replaces the plain text dialog.</summary>
public sealed class AboutWindow:Window
{
    private const string Repo="github.com/ChristianBuzzetti/DBMON_SQL";

    private AboutWindow()
    {
        SetResourceReference(StyleProperty,typeof(Window));
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        Title="About DBMON SQL";
        Width=500;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        SourceInitialized+=(_,_)=>Themes.ApplyChrome(this,Themes.Current);
        var root=new StackPanel {Margin=new Thickness(32,30,32,22)};

        // Hero: the living mark, the name and the version.
        var mark=new BobertoMark {Width=84,Height=84,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,14)};
        mark.SetResourceReference(BobertoMark.TileProperty,"Accent");
        root.Children.Add(mark);
        var name=new TextBlock {Text="DBMON SQL",FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center};
        name.SetResourceReference(TextBlock.FontSizeProperty,"TitleFontSize");name.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        root.Children.Add(name);
        var chips=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0)};
        var version=typeof(AboutWindow).Assembly.GetName().Version;
        if(version!=null)chips.Children.Add(Chip("v"+version.ToString(3)));
        root.Children.Add(chips);

        var about=new TextBlock {Text="A fast, keyboard-first SQL workbench for SAP ASE, SAP IQ and SAP Replication Server: sessions, results, recipes, activity monitor and health checks. Ctrl+E runs the selection.",TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,16,0,0),LineHeight=20};
        about.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        root.Children.Add(about);

        // Credits as label/value rows inside a quiet card.
        var card=new Border {Margin=new Thickness(0,20,0,0),Padding=new Thickness(16,6,16,6),BorderThickness=new Thickness(1)};
        card.SetResourceReference(Border.BackgroundProperty,"SurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");
        card.SetResourceReference(Border.CornerRadiusProperty,"StripRadius");
        var rows=new Grid();
        rows.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        var link=new TextBlock {Text=Repo,Cursor=Cursors.Hand,TextDecorations=TextDecorations.Underline};
        link.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        link.MouseLeftButtonUp+=(_,_)=>Open("https://"+Repo);
        AddRow(rows,"LICENSE",Value("MIT · © 2026 Christian Buzzetti"));
        AddRow(rows,"CODE",link);
        AddRow(rows,"BUILT WITH",Value(".NET 10 · WPF · AvalonEdit · SAP ODBC"));
        AddRow(rows,"FONTS",Value("Inter and JetBrains Mono (SIL Open Font License)"));
        AddRow(rows,"SERVERS",Value("Connects with sql.ini and the credentials you enter. Verify server compatibility in a non-production environment."));
        card.Child=rows;
        root.Children.Add(card);

        // Footer: repository on the left, close on the right.
        var foot=new DockPanel {Margin=new Thickness(0,20,0,0)};
        var ok=new Button {Content="OK",IsDefault=true,IsCancel=true,MinWidth=110,FontWeight=FontWeights.SemiBold};
        ok.SetResourceReference(BackgroundProperty,"Accent");ok.SetResourceReference(ForegroundProperty,"OnAccent");ok.SetResourceReference(BorderBrushProperty,"Accent");
        ok.Click+=(_,_)=>Close();
        DockPanel.SetDock(ok,Dock.Right);foot.Children.Add(ok);
        var github=new Button {Content="View on GitHub",HorizontalAlignment=HorizontalAlignment.Left,MinWidth=110};
        github.Click+=(_,_)=>Open("https://"+Repo);
        foot.Children.Add(github);
        root.Children.Add(foot);

        Content=root;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }

    private static Border Chip(string text)
    {
        var label=new TextBlock {Text=text,FontSize=11,FontWeight=FontWeights.SemiBold};
        label.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");
        label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var chip=new Border {Child=label,Padding=new Thickness(9,3,9,3),Margin=new Thickness(3,0,3,0),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10)};
        chip.SetResourceReference(Border.BackgroundProperty,"SurfaceRaised");
        chip.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");
        return chip;
    }

    private static TextBlock Value(string text)
    {
        var value=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap};value.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return value;
    }

    private static void AddRow(Grid grid,string label,FrameworkElement value)
    {
        int row=grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var caption=new TextBlock {Text=label,FontSize=10.5,Margin=new Thickness(0,9,18,9),VerticalAlignment=VerticalAlignment.Center};
        caption.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");
        caption.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        value.Margin=new Thickness(0,9,0,9);value.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetRow(caption,row);Grid.SetRow(value,row);Grid.SetColumn(value,1);
        grid.Children.Add(caption);grid.Children.Add(value);
        if(row>0)
        {
            var rule=new Border {Height=1,VerticalAlignment=VerticalAlignment.Top};
            rule.SetResourceReference(Border.BackgroundProperty,"FaintBorder");
            Grid.SetRow(rule,row);Grid.SetColumnSpan(rule,2);
            grid.Children.Add(rule);
        }
    }

    private static void Open(string url)
    {
        try{Process.Start(new ProcessStartInfo(url) {UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}
    }

    /// <summary>Not modal, for the UI checks and screenshots.</summary>
    public static Window ForCheck(Window owner)=>new AboutWindow {Owner=owner};

    public static void Show(Window owner)=>new AboutWindow {Owner=owner}.ShowDialog();
}
