using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AseIsql.Desktop;

/// <summary>
/// Help → Guide (F1), as in BuzzPrint: every feature explained step by step, with its shortcuts and a button that opens
/// it. Topics on the left (with search), the selected topic on the right as cards, previous / next at the bottom.
/// </summary>
public sealed class GuideWindow:Window
{
    private static GuideWindow? open;
    private readonly IReadOnlyList<GuideTopic> topics=GuideContent.All;
    private readonly StackPanel nav=new();
    private readonly StackPanel page=new() {Margin=new Thickness(28,6,28,28),MaxWidth=860,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly ScrollViewer pageScroll;
    private readonly TextBox search=new() {Margin=new Thickness(0,0,0,12)};
    private readonly Action<string> act;
    private GuideTopic current;
    public GuideTopic Current=>current;

    private GuideWindow(Window owner,string? topicId,Action<string> action)
    {
        act=action;
        SetResourceReference(StyleProperty,typeof(Window));
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        Title="DBMON SQL guide";Tag="no-badge";
        Width=1140;Height=800;MinWidth=820;MinHeight=560;
        Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SourceInitialized+=(_,_)=>Themes.ApplyChrome(this,Themes.Current);
        current=topics.FirstOrDefault(t=>t.Id==topicId)??topics[0];

        var root=new DockPanel {Margin=new Thickness(24,20,24,18)};
        var head=new StackPanel();
        var heading=new TextBlock {Text="DBMON SQL guide",FontWeight=FontWeights.SemiBold};heading.SetResourceReference(TextBlock.FontSizeProperty,"TitleFontSize");heading.SetResourceReference(TextBlock.ForegroundProperty,"Ink");head.Children.Add(heading);
        head.Children.Add(Muted("Everything you can do, step by step. Press a button to try each feature.",new Thickness(0,4,0,14)));
        var top=BobertoMark.WithBadge(head,48);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);

        // Left: search and topics.
        var left=new DockPanel {Width=270};
        var searchBox=new Grid();
        var hint=Muted("Search the guide…",new Thickness(10,0,0,12));hint.VerticalAlignment=VerticalAlignment.Center;hint.IsHitTestVisible=false;
        search.TextChanged+=(_,_)=>{hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;RenderNav();};
        searchBox.Children.Add(search);searchBox.Children.Add(hint);
        DockPanel.SetDock(searchBox,Dock.Top);left.Children.Add(searchBox);
        left.Children.Add(new ScrollViewer {Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var leftCard=Card(left,new Thickness(12));leftCard.Margin=new Thickness(0,0,16,0);
        DockPanel.SetDock(leftCard,Dock.Left);root.Children.Add(leftCard);

        // Right: the topic.
        pageScroll=new ScrollViewer {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(Card(pageScroll,new Thickness(0)));
        Content=root;
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape)Close();
            else if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control){search.Focus();search.SelectAll();e.Handled=true;}
        };
        Closed+=(_,_)=>{if(open==this)open=null;};
        RenderNav();
        ShowTopic(current);
    }

    // ------------------------------------------------------------------ navigation
    private static bool Has(string text,string query)=>CultureInfo.InvariantCulture.CompareInfo.IndexOf(text,query,CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace)>=0;
    private static bool Matches(GuideTopic t,string q)=>q.Length==0||Has(t.Title,q)||Has(t.Lead,q)||t.Blocks.Any(b=>Has(b.Heading,q)||b.Items.Any(i=>Has(i.A,q)||Has(i.B,q)));

    private void RenderNav()
    {
        nav.Children.Clear();
        string q=search.Text.Trim();
        var shown=topics.Where(t=>Matches(t,q)).ToList();
        if(shown.Count==0){nav.Children.Add(Muted("No results. Try another word.",new Thickness(6,6,0,0)));return;}
        foreach(var t in shown)
        {
            var topic=t;
            var row=new DockPanel();
            var glyph=new TextBlock {Text=t.Glyph,Width=30};glyph.SetResourceReference(StyleProperty,"GlyphText");glyph.FontSize=15;
            row.Children.Add(glyph);
            row.Children.Add(new TextBlock {Text=t.Title,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
            var item=new Border {Child=row,Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,0,0,2),BorderThickness=new Thickness(2,0,0,0),Cursor=Cursors.Hand,Tag=t.Id};
            item.SetResourceReference(Border.CornerRadiusProperty,"ControlRadius");
            item.MouseLeftButtonUp+=(_,_)=>ShowTopic(topic);
            item.MouseEnter+=(_,_)=>{if(current!=topic)item.SetResourceReference(Border.BackgroundProperty,"AlternateRow");};
            item.MouseLeave+=(_,_)=>Paint(item,current==topic);
            Paint(item,t==current);
            nav.Children.Add(item);
        }
        if(q.Length>0&&!shown.Contains(current))ShowTopic(shown[0]);
    }

    private static void Paint(Border item,bool on)
    {
        if(on){item.SetResourceReference(Border.BackgroundProperty,"AccentTint");item.SetResourceReference(Border.BorderBrushProperty,"Accent");}
        else{item.Background=Brushes.Transparent;item.BorderBrush=Brushes.Transparent;}
        if(item.Child is DockPanel row)foreach(var text in row.Children.OfType<TextBlock>())text.SetResourceReference(TextBlock.ForegroundProperty,on?"Accent":"Ink");
    }

    public void ShowTopic(GuideTopic t)
    {
        current=t;
        foreach(var item in nav.Children.OfType<Border>())Paint(item,(string)item.Tag==t.Id);
        page.Children.Clear();

        // Hero: icon tile, title, lead and the "try it" buttons.
        var hero=new DockPanel {Margin=new Thickness(0,22,0,0)};
        var tile=new Border {Width=60,Height=60,CornerRadius=new CornerRadius(16),Margin=new Thickness(0,0,18,0),VerticalAlignment=VerticalAlignment.Top};
        tile.SetResourceReference(Border.BackgroundProperty,"Accent");
        var glyph=new TextBlock {Text=t.Glyph,FontSize=28,Margin=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")};
        glyph.SetResourceReference(TextBlock.ForegroundProperty,"OnAccent");
        tile.Child=glyph;
        DockPanel.SetDock(tile,Dock.Left);hero.Children.Add(tile);
        var texts=new StackPanel();
        var title=new TextBlock {Text=t.Title,FontWeight=FontWeights.Bold,FontSize=26,TextWrapping=TextWrapping.Wrap};title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        texts.Children.Add(title);
        var lead=new TextBlock {Text=t.Lead,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0),LineHeight=22,FontSize=14.5};
        lead.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        texts.Children.Add(lead);
        if(t.Try.Count>0)
        {
            var actions=new WrapPanel {Margin=new Thickness(0,14,0,0)};
            bool first=true;
            foreach(var (label,action) in t.Try)
            {
                var button=new Button {Content=label,MinWidth=150,Margin=new Thickness(0,0,8,8),Padding=new Thickness(16,6,16,6)};
                if(first){button.FontWeight=FontWeights.SemiBold;button.SetResourceReference(BackgroundProperty,"Accent");button.SetResourceReference(ForegroundProperty,"OnAccent");button.SetResourceReference(BorderBrushProperty,"Accent");}
                string id=action;
                button.Click+=(_,_)=>act(id);
                actions.Children.Add(button);
                first=false;
            }
            texts.Children.Add(actions);
        }
        hero.Children.Add(texts);
        page.Children.Add(hero);

        foreach(var block in t.Blocks)page.Children.Add(Block(block));

        // Previous / next.
        int index=topics.ToList().IndexOf(t);
        var pager=new DockPanel {Margin=new Thickness(0,22,0,0)};
        if(index+1<topics.Count){var next=PagerButton("Next: "+topics[index+1].Title+"  →",topics[index+1]);DockPanel.SetDock(next,Dock.Right);pager.Children.Add(next);}
        if(index>0){var prev=PagerButton("←  Previous: "+topics[index-1].Title,topics[index-1]);prev.HorizontalAlignment=HorizontalAlignment.Left;pager.Children.Add(prev);}
        page.Children.Add(pager);
        pageScroll.ScrollToTop();
    }

    private Button PagerButton(string text,GuideTopic to)
    {
        var button=new Button {Content=text,Padding=new Thickness(14,6,14,6)};
        button.Click+=(_,_)=>ShowTopic(to);
        return button;
    }

    // ------------------------------------------------------------------ blocks
    private static FrameworkElement Block(GuideBlock b)
    {
        var body=new StackPanel();
        var label=new TextBlock {FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,12)};
        var mark=new Run("◤ ");mark.SetResourceReference(TextElement.ForegroundProperty,"Accent");
        label.Inlines.Add(mark);label.Inlines.Add(new Run(b.Heading));
        label.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");
        label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        body.Children.Add(label);
        int n=0;
        foreach(var (a,desc) in b.Items)
        {
            n++;
            var row=new DockPanel {Margin=new Thickness(0,0,0,10)};
            switch(b.Kind)
            {
                case "steps":
                {
                    var num=new Border {Width=26,Height=26,CornerRadius=new CornerRadius(13),Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Top};
                    num.SetResourceReference(Border.BackgroundProperty,"Accent");
                    var digits=new TextBlock {Text=n.ToString(CultureInfo.InvariantCulture),FontWeight=FontWeights.Bold,FontSize=12.5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                    digits.SetResourceReference(TextBlock.ForegroundProperty,"OnAccent");
                    num.Child=digits;
                    DockPanel.SetDock(num,Dock.Left);row.Children.Add(num);
                    row.Children.Add(Body(a,new Thickness(0,3,0,0)));
                    break;
                }
                case "keys":
                {
                    var chip=new Border {BorderThickness=new Thickness(1),Padding=new Thickness(9,3,9,4),Margin=new Thickness(0,0,14,0),MinWidth=150,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left};
                    chip.SetResourceReference(Border.BorderBrushProperty,"StrongBorder");
                    chip.SetResourceReference(Border.BackgroundProperty,"Surface");
                    chip.SetResourceReference(Border.CornerRadiusProperty,"ControlRadius");
                    var keys=new TextBlock {Text=a,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
                    keys.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");keys.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
                    chip.Child=keys;
                    var column=new Border {Width=210,Child=chip};
                    DockPanel.SetDock(column,Dock.Left);row.Children.Add(column);
                    row.Children.Add(Body(desc,new Thickness(0,3,0,0)));
                    break;
                }
                default:
                {
                    var dot=new Border {Width=6,Height=6,CornerRadius=new CornerRadius(3),Margin=new Thickness(4,8,14,0),VerticalAlignment=VerticalAlignment.Top};
                    dot.SetResourceReference(Border.BackgroundProperty,"Accent");
                    DockPanel.SetDock(dot,Dock.Left);row.Children.Add(dot);
                    row.Children.Add(Body(a,new Thickness(0)));
                    break;
                }
            }
            body.Children.Add(row);
        }
        var card=Card(body,new Thickness(20,16,20,8));
        card.Margin=new Thickness(0,18,0,0);
        card.SetResourceReference(Border.BackgroundProperty,"SurfaceRaised");
        return card;
    }

    private static TextBlock Body(string text,Thickness margin)
    {
        var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=margin,LineHeight=21,FontSize=14};block.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return block;
    }

    private static TextBlock Muted(string text,Thickness margin)
    {
        var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=margin};
        block.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        return block;
    }

    private static Border Card(UIElement child,Thickness padding)
    {
        var border=new Border {Child=child,Padding=padding,BorderThickness=new Thickness(1)};
        border.SetResourceReference(Border.BackgroundProperty,"Surface");
        border.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");
        border.SetResourceReference(Border.CornerRadiusProperty,"StripRadius");
        return border;
    }

    /// <summary>Opens the guide (or brings it forward), optionally on a topic. <paramref name="action"/> runs a
    /// "try it" button in the main window.</summary>
    public static GuideWindow Open(Window owner,Action<string> action,string? topicId=null)
    {
        if(open!=null)
        {
            if(topicId!=null&&open.topics.FirstOrDefault(t=>t.Id==topicId) is {} t)open.ShowTopic(t);
            if(open.WindowState==WindowState.Minimized)open.WindowState=WindowState.Normal;
            open.Activate();
            return open;
        }
        open=new GuideWindow(owner,topicId,action);
        open.Show();
        return open;
    }
}
