using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AseIsql.Desktop;

// One modal for the workspace look: a live mini-console on the left, grouped setting cards on the right (Style, Text,
// Interface, Effects) and Boberto Studio behind the header action. Every control writes through Change, which owns
// validation, live application and the previews; nothing here persists by itself.
public sealed class PersonalizationWindow:Window
{
    private AppearanceSettings settings;
    private readonly Action<AppearanceSettings> changed;
    public TabControl Sections { get; }=new();
    public Dictionary<string,Button> PaletteButtons { get; }=[];
    // Keyed "Section/Title" so checks can find a toggle by where it lives, whatever row layout surrounds it.
    public Dictionary<string,CheckBox> Toggles { get; }=[];
    public CustomAccentPicker AccentPicker { get; }
    private readonly TextBlock previewTitle=new(),previewSql=new(),previewStatus=new();
    // The preview shows the configured Boberto live (same engine and profile as the console dock): look, accessory,
    // background, frame and idle animation, at the 16:9 proportion of the art.
    private readonly Border bobertoPreview=new() {Margin=new Thickness(0,14,0,0),Height=158,ClipToBounds=true,BorderThickness=new Thickness(1)};
    public BobertoDockControl? PreviewDock {get;private set;}
    private readonly Button studioButton;
    public Button SectionSwitch=>studioButton;
    private readonly List<Action> refreshControls=[];
    private bool refreshing;
    private bool closed;
    public BobertoStudioControl? Studio {get;private set;}
    private readonly Action<BitmapSource>? frameChanged;
    private readonly string? isolatedProfile;

    public PersonalizationWindow(AppearanceSettings initial,Action<AppearanceSettings> onChanged,bool studio=false,Action<BitmapSource>? onFrame=null,string? testProfile=null)
    {
        SetResourceReference(StyleProperty,typeof(Window));TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        settings=initial;changed=onChanged;frameChanged=onFrame;isolatedProfile=testProfile;Title="Personalization · DBMON SQL";Width=1300;Height=820;MinWidth=980;MinHeight=660;WindowSizing.FitToWorkArea(this);WindowStartupLocation=WindowStartupLocation.CenterOwner;
        AccentPicker=new CustomAccentPicker(hex=>Change(settings with {AccentHex=hex}));
        var root=new DockPanel {Margin=new Thickness(24,20,24,18)};

        // Header: title and purpose on the left, the opposite place (Studio / workspace) on the right.
        var heading=new DockPanel {Margin=new Thickness(0,0,0,18)};
        studioButton=new Button {Content="Boberto Studio",VerticalAlignment=VerticalAlignment.Center};studioButton.SetResourceReference(StyleProperty,"PillButton");
        studioButton.Click+=(_,_)=>Sections.SelectedIndex=Sections.SelectedIndex==4?0:4;DockPanel.SetDock(studioButton,Dock.Right);heading.Children.Add(studioButton);
        var titles=new StackPanel();
        var title=new TextBlock {FontWeight=FontWeights.SemiBold};var mark=new Run("◤ ");mark.SetResourceReference(TextElement.ForegroundProperty,"Accent");title.Inlines.Add(mark);title.Inlines.Add(new Run("Personalize your workspace"));title.SetResourceReference(TextBlock.FontSizeProperty,"TitleFontSize");titles.Children.Add(title);
        titles.Children.Add(Muted("Palette, accent, typography and interface for DBMON SQL. Every change applies at once and is saved on this computer.",new Thickness(25,4,0,0)));
        heading.Children.Add(titles);DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);

        // Footer: reassurance on the left, Reset (quiet) and Done (primary) on the right.
        var foot=new DockPanel {Margin=new Thickness(0,16,0,0)};
        var close=new Button {Content="Done",IsCancel=true,IsDefault=true,MinWidth=96,Margin=new Thickness(8,0,0,0)};close.SetResourceReference(BackgroundProperty,"Accent");close.SetResourceReference(ForegroundProperty,"OnAccent");close.SetResourceReference(BorderBrushProperty,"Accent");close.FontWeight=FontWeights.SemiBold;close.Click+=(_,_)=>Close();
        var reset=new Button {Content="Reset appearance",ToolTip="Pergamino, theme accent, bundled fonts and default sizes"};reset.Click+=(_,_)=>Change(new AppearanceSettings());
        DockPanel.SetDock(close,Dock.Right);DockPanel.SetDock(reset,Dock.Right);foot.Children.Add(close);foot.Children.Add(reset);
        var saved=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var check=Glyph("",12);check.SetResourceReference(TextBlock.ForegroundProperty,"Ok");saved.Children.Add(check);saved.Children.Add(Muted("Changes apply instantly · saved locally for DBMON SQL"));foot.Children.Add(saved);
        DockPanel.SetDock(foot,Dock.Bottom);root.Children.Add(foot);

        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(348)});columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(20)});columns.ColumnDefinitions.Add(new ColumnDefinition());
        var preview=BuildPreview(testProfile);columns.Children.Add(preview);
        Sections.SetResourceReference(StyleProperty,"SegmentedTabs");
        Grid.SetColumn(Sections,2);columns.Children.Add(Sections);root.Children.Add(columns);Content=root;
        BuildStyle();BuildText();BuildInterface();BuildEffects();BuildBoberto();
        Sections.SelectionChanged+=async(_,args)=> {
            if(closed||args.Source!=Sections)return;
            bool full=Sections.SelectedIndex==4;
            // The header action always leads to the opposite place: Studio from the workspace sections, and back.
            studioButton.Content=full?"Workspace personalization":"Boberto Studio";preview.Visibility=full?Visibility.Collapsed:Visibility.Visible;columns.ColumnDefinitions[0].Width=new GridLength(full?0:348);columns.ColumnDefinitions[1].Width=new GridLength(full?0:20);
            if(full && Studio==null){Studio=new BobertoStudioControl(settings,isolatedProfile);Studio.FrameReady+=image=>frameChanged?.Invoke(image);((DockPanel)((TabItem)Sections.Items[4]).Content).Children.Add(Studio);}
            if(PreviewDock!=null)_=PreviewDock.PauseAsync(full||!settings.BobertoVisible);
            if(Studio!=null)await Studio.PauseAsync(!full);
        };
        Closed+=(_,_)=>{closed=true;Studio?.Dispose();PreviewDock?.Dispose();};
        PreviewKeyDown+=(_,key)=> {if(key.Key==Key.Escape)Close();};UpdatePreview();if(studio)Sections.SelectedIndex=4;
    }

    // ---- Live preview: a miniature of the console in the current palette, accent and editor type. ----
    private ContentControl BuildPreview(string? testProfile)
    {
        var card=new ContentControl();card.SetResourceReference(StyleProperty,"NeoCard");
        var scroll=new ScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var scene=new StackPanel {Margin=new Thickness(18,16,18,18)};scroll.Content=scene;card.Content=scroll;
        var top=new DockPanel {Margin=new Thickness(0,0,0,12)};var badge=new Border {BorderThickness=new Thickness(1),Padding=new Thickness(6,1,6,1),VerticalAlignment=VerticalAlignment.Center};badge.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");var badgeText=Mono("SAMPLE DATA",9);badgeText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");badge.Child=badgeText;DockPanel.SetDock(badge,Dock.Right);top.Children.Add(badge);top.Children.Add(SectionTitle("LIVE PREVIEW"));scene.Children.Add(top);
        previewTitle.FontWeight=FontWeights.SemiBold;previewTitle.SetResourceReference(TextBlock.FontSizeProperty,"SectionFontSize");scene.Children.Add(previewTitle);
        previewStatus.Margin=new Thickness(0,2,0,12);previewStatus.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");previewStatus.SetResourceReference(TextBlock.ForegroundProperty,"Muted");previewStatus.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");scene.Children.Add(previewStatus);

        var app=new Border {BorderThickness=new Thickness(1),ClipToBounds=true};app.SetResourceReference(Border.BorderBrushProperty,"StrongBorder");app.SetResourceReference(Border.BackgroundProperty,"Canvas");var stack=new StackPanel();app.Child=stack;
        // Topbar: wordmark, ok dot and a primary Run, as in the console header.
        var bar=new DockPanel {Margin=new Thickness(0)};var barFrame=new Border {Padding=new Thickness(10,7,8,7),Child=bar};barFrame.SetResourceReference(Border.BackgroundProperty,"Header");
        var run=new Border {Padding=new Thickness(9,3,9,3)};run.SetResourceReference(Border.BackgroundProperty,"Accent");var runText=new TextBlock {Text="▶ Run  F9",FontWeight=FontWeights.SemiBold,FontSize=11};runText.SetResourceReference(TextBlock.ForegroundProperty,"OnAccent");run.Child=runText;DockPanel.SetDock(run,Dock.Right);bar.Children.Add(run);
        var brand=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var word=Mono("DBMON SQL",11);word.FontWeight=FontWeights.Bold;word.SetResourceReference(TextBlock.ForegroundProperty,"Accent");brand.Children.Add(word);var dot=new Ellipse {Width=5,Height=5,Margin=new Thickness(6,1,0,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Shape.FillProperty,"Ok");brand.Children.Add(dot);bar.Children.Add(brand);
        stack.Children.Add(barFrame);var edge=new Border {Height=2};edge.SetResourceReference(Border.BackgroundProperty,"EdgeBar");stack.Children.Add(edge);
        // Editor: gutter, syntax colors and a selected line in the editor's selection mix.
        var editor=new Grid();editor.SetResourceReference(Panel.BackgroundProperty,"Surface");editor.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});editor.ColumnDefinitions.Add(new ColumnDefinition());
        var gutter=Mono("1\n2\n3\n4\n5",0);gutter.TextAlignment=TextAlignment.Right;gutter.Margin=new Thickness(8,8,10,8);gutter.SetResourceReference(TextBlock.ForegroundProperty,"LineNumber");editor.Children.Add(gutter);
        var selected=new Border {VerticalAlignment=VerticalAlignment.Top};selected.SetResourceReference(Border.BackgroundProperty,"Selection");Grid.SetColumnSpan(selected,2);editor.Children.Insert(0,selected);
        previewSql.Margin=new Thickness(0,8,8,8);previewSql.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");previewSql.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        void Code(string text,string? brush=null,bool bold=false){var part=new Run(text);if(brush!=null)part.SetResourceReference(TextElement.ForegroundProperty,brush);if(bold)part.FontWeight=FontWeights.Bold;previewSql.Inlines.Add(part);}
        Code("-- user databases online","SqlComment");Code("\n");Code("select","SqlKeyword",true);Code(" name, status\n");Code("from","SqlKeyword",true);Code(" master..sysdatabases\n");Code("where","SqlKeyword",true);Code(" dbid > ");Code("3","SqlNumber");Code("\n  ");Code("and","SqlKeyword",true);Code(" name ");Code("like","SqlKeyword",true);Code(" ");Code("'sales%'","SqlString");
        Grid.SetColumn(previewSql,1);editor.Children.Add(previewSql);
        // The selection band follows the third line at the current editor size.
        previewSql.SizeChanged+=(_,_)=> {double line=previewSql.ActualHeight/5;selected.Height=line;selected.Margin=new Thickness(0,8+line*2,0,0);gutter.FontSize=previewSql.FontSize;};
        stack.Children.Add(editor);
        // Result grid: uppercase mono headers on the accent rule, row rules and a selected row with the accent bar.
        var grid=new StackPanel();grid.SetResourceReference(Panel.BackgroundProperty,"Surface");
        var head=GridRow("NAME","STATUS","DBID",header:true);head.BorderThickness=new Thickness(0,1,0,2);head.SetResourceReference(Border.BorderBrushProperty,"AccentRule");grid.Children.Add(head);
        grid.Children.Add(GridRow("sales","online","4"));var current=GridRow("sales_hist","online","5",selectedRow:true);grid.Children.Add(current);grid.Children.Add(GridRow("staging","offline","6"));
        stack.Children.Add(grid);
        var status=new DockPanel();var statusFrame=new Border {Padding=new Thickness(10,4,10,5),Child=status,BorderThickness=new Thickness(0,1,0,0)};statusFrame.SetResourceReference(Border.BackgroundProperty,"Header");statusFrame.SetResourceReference(Border.BorderBrushProperty,"AccentRule");
        var session=Mono("master · SPID 42",10);session.SetResourceReference(TextBlock.ForegroundProperty,"Ink");DockPanel.SetDock(session,Dock.Right);status.Children.Add(session);var ready=Mono("Ready · 3 rows · 12 ms",10);ready.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.Children.Add(ready);
        stack.Children.Add(statusFrame);scene.Children.Add(app);

        bobertoPreview.SetResourceReference(Border.BorderBrushProperty,"StrongBorder");
        PreviewDock=new BobertoDockControl(settings,testProfile,cacheSnapshot:false,suspendRenderer:false);PreviewDock.StudioRequested+=()=>Dispatcher.BeginInvoke(()=> {if(!closed)Sections.SelectedIndex=4;});bobertoPreview.Child=PreviewDock;scene.Children.Add(bobertoPreview);
        return card;
    }
    private static Border GridRow(string name,string state,string id,bool header=false,bool selectedRow=false)
    {
        var row=new Grid();foreach(double width in new[]{1.4,1,.6})row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(width,GridUnitType.Star)});
        string[] cells=[name,state,id];
        for(int i=0;i<3;i++){var text=Mono(cells[i],header?9.5:0);if(header){text.FontWeight=FontWeights.Bold;text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");}else text.SetResourceReference(TextBlock.ForegroundProperty,state=="offline"&&i==1?"ErrorInk":"Ink");Grid.SetColumn(text,i);row.Children.Add(text);}
        // The selected row carries DBMON's 3 px accent bar and tint; every data row sits on the accent row rule.
        var inner=new Border {Child=row,Padding=new Thickness(selectedRow?7:10,header?6:5,10,5),BorderThickness=new Thickness(selectedRow?3:0,0,0,0)};
        if(selectedRow){inner.SetResourceReference(Border.BorderBrushProperty,"Accent");inner.SetResourceReference(Border.BackgroundProperty,"AccentTint");}
        if(header)return inner;
        var outer=new Border {Child=inner,BorderThickness=new Thickness(0,0,0,1)};outer.SetResourceReference(Border.BorderBrushProperty,"AccentRowRule");return outer;
    }

    // ---- Building blocks shared by the sections. ----
    private StackPanel Section(string name)
    {
        var content=new StackPanel {Margin=new Thickness(0,4,10,8)};var scroll=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Sections.Items.Add(new TabItem {Header=TabHeader(name),Content=scroll});return content;
    }
    // Segmented tab header with a Segoe Fluent glyph, as in Boberto Studio's section navigation.
    public static object TabHeader(string name)
    {
        string glyph=name switch {"Style"=>"","Text"=>"","Interface"=>"","Effects"=>"","Boberto"=>"",_=>""};
        var header=new StackPanel {Orientation=Orientation.Horizontal};var icon=new TextBlock {Text=glyph};icon.SetResourceReference(StyleProperty,"TabIcon");
        header.Children.Add(icon);header.Children.Add(new TextBlock {Text=name,VerticalAlignment=VerticalAlignment.Center});return header;
    }
    // A setting group: a hairline card with a ▚ section title, a muted purpose line and its controls.
    private static StackPanel Group(StackPanel section,string name,string description)
    {
        var card=new Border {BorderThickness=new Thickness(1),Padding=new Thickness(18,14,18,16),Margin=new Thickness(0,0,0,12)};card.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.CornerRadiusProperty,"StripRadius");
        var body=new StackPanel();body.Children.Add(SectionTitle(name));body.Children.Add(Muted(description,new Thickness(0,4,0,12)));card.Child=body;section.Children.Add(card);return body;
    }
    private static FrameworkElement SectionTitle(string name)
    {
        var row=new StackPanel {Orientation=Orientation.Horizontal};var marker=new TextBlock {Text="▚",FontSize=10,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};marker.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        var label=new TextBlock {Text=name};label.SetResourceReference(StyleProperty,"SectionLabel");row.Children.Add(marker);row.Children.Add(label);return row;
    }
    private static TextBlock Muted(string text,Thickness? margin=null)
    {
        var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0),VerticalAlignment=VerticalAlignment.Center};block.SetResourceReference(TextBlock.ForegroundProperty,"Muted");block.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");return block;
    }
    // Setting titles use the content ink explicitly: a selected tab's accent must never reach its content.
    private static TextBlock Heading(string text,Thickness? margin=null)
    {
        var block=new TextBlock {Text=text,FontWeight=FontWeights.SemiBold,Margin=margin??new Thickness(0,0,0,8)};block.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return block;
    }
    private static TextBlock Mono(string text,double size)
    {
        var block=new TextBlock {Text=text,VerticalAlignment=VerticalAlignment.Center};block.SetResourceReference(TextBlock.FontFamilyProperty,"MonoFont");if(size>0)block.FontSize=size;else block.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");return block;
    }
    private static TextBlock Glyph(string glyph,double size)
    {
        var block=new TextBlock {Text=glyph,FontSize=size,Margin=new Thickness(0,0,7,0)};block.SetResourceReference(StyleProperty,"GlyphText");block.FontSize=size;return block;
    }
    // Selectable card (palette tile, font option): accent ring, tint and a check mark when it is the current choice.
    private Button OptionCard(FrameworkElement body,string name,string? tag,Func<bool> isCurrent,Action select,string? tooltip=null)
    {
        var footer=new DockPanel {Margin=new Thickness(2,8,2,0)};var tick=Glyph("",11);tick.Margin=new Thickness(6,0,0,0);tick.SetResourceReference(TextBlock.ForegroundProperty,"Accent");DockPanel.SetDock(tick,Dock.Right);footer.Children.Add(tick);
        if(tag!=null){var tagText=Mono(tag,9.5);tagText.Margin=new Thickness(6,0,0,0);tagText.SetResourceReference(TextBlock.ForegroundProperty,"Muted");DockPanel.SetDock(tagText,Dock.Right);footer.Children.Add(tagText);}
        var label=new TextBlock {Text=name,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.Medium,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");footer.Children.Add(label);
        var content=new StackPanel();content.Children.Add(body);content.Children.Add(footer);
        var button=new Button {Content=content,Margin=new Thickness(0,0,8,8),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=tooltip??name};button.SetResourceReference(StyleProperty,"OptionCard");
        button.Click+=(_,_)=>select();
        refreshControls.Add(()=> {bool on=isCurrent();tick.Visibility=on?Visibility.Visible:Visibility.Hidden;button.BorderThickness=new Thickness(on?2:1);button.Padding=new Thickness(on?7:8);button.SetResourceReference(BorderBrushProperty,on?"Accent":"FaintBorder");button.SetResourceReference(BackgroundProperty,on?"AccentTint":"Surface");label.FontWeight=on?FontWeights.SemiBold:FontWeights.Medium;});
        return button;
    }
    private void Change(AppearanceSettings next){if(refreshing)return;next.Validate();settings=next;changed(next);UpdatePreview();if(Studio!=null)_=Studio.ApplyThemeAsync(next);if(PreviewDock!=null){bool pause=!next.BobertoVisible||Sections.SelectedIndex==4;_=PreviewDock.ApplyThemeAsync(next);if(PreviewDock.IsPaused!=pause)_=PreviewDock.PauseAsync(pause);}}
    private void UpdatePreview()
    {
        var palette=Themes.All.Single(p=>p.Id==settings.ThemeId);
        previewTitle.Text="DBMON SQL · "+PaletteName(palette);
        previewStatus.Text=(palette.DarkChrome?"dark":"light")+" · accent "+(settings.AccentHex==null?"auto "+palette.Colors["Accent"].ToUpperInvariant():Themes.Accents.FirstOrDefault(a=>a.Hex==settings.AccentHex)?.Name.ToLowerInvariant()+" "+settings.AccentHex);
        if(settings.AccentHex!=null&&!Themes.Accents.Any(a=>a.Hex==settings.AccentHex))previewStatus.Text=(palette.DarkChrome?"dark":"light")+" · accent custom "+settings.AccentHex;
        previewSql.FontSize=Math.Min(settings.EditorSize,16);
        bobertoPreview.SetResourceReference(Border.BackgroundProperty,"Header");bobertoPreview.Visibility=settings.BobertoVisible?Visibility.Visible:Visibility.Collapsed;
        refreshing=true;try{foreach(var action in refreshControls)action();}finally{refreshing=false;}
    }
    private static string PaletteName(ThemePalette palette)=>palette.Label.Replace(" · Light","").Replace(" · Dark","");

    // ---- Style: palettes grouped by chrome, then the accent row with a custom mixer. ----
    private void BuildStyle()
    {
        var section=Section("Style");
        var palettes=Group(section,"COLOR PALETTE","DBMON's 19 palettes. Each one sets surfaces, ink, SQL syntax colors and its own accent.");
        foreach(var (heading,dark) in new[]{("Dark",true),("Light",false)})
        {
            var members=Themes.All.Where(p=>p.DarkChrome==dark).ToList();
            var label=Mono(heading.ToUpperInvariant()+"  ·  "+members.Count,9.5);label.Margin=new Thickness(0,dark?0:8,0,8);label.SetResourceReference(TextBlock.ForegroundProperty,"LineNumber");palettes.Children.Add(label);
            var tiles=new UniformGrid {Columns=4};palettes.Children.Add(tiles);
            foreach(var palette in members){var button=OptionCard(PaletteMock(palette),PaletteName(palette),null,()=>settings.ThemeId==palette.Id,()=>Change(settings with {ThemeId=palette.Id,AccentHex=null}),palette.Label);PaletteButtons.Add(palette.Id,button);tiles.Children.Add(button);}
        }
        var accents=Group(section,"ACCENT","Auto follows the palette. Pick a preset or mix your own; on light palettes a bright accent is deepened until it reads as text.");
        var row=new WrapPanel();accents.Children.Add(row);
        foreach(var accent in Themes.Accents)row.Children.Add(AccentSwatch(accent.Name,accent.Hex));
        row.Children.Add(CustomSwatch());
        AccentPicker.Visibility=Visibility.Collapsed;AccentPicker.Margin=new Thickness(0,10,0,0);accents.Children.Add(AccentPicker);
    }
    // A palette tile is a tiny console: header strip, accent rule, ink and muted lines, SQL syntax chips.
    private static FrameworkElement PaletteMock(ThemePalette palette)
    {
        Brush C(string key)=>ColorBrush(palette.Colors[key]);
        var mock=new Border {Height=62,Background=C("Canvas"),BorderBrush=C("Rule"),BorderThickness=new Thickness(1),ClipToBounds=true};
        var stack=new StackPanel();mock.Child=stack;
        var top=new DockPanel {Background=C("Header"),Height=11};var dot=new Ellipse {Width=5,Height=5,Fill=C("Accent"),Margin=new Thickness(0,0,5,0)};DockPanel.SetDock(dot,Dock.Right);top.Children.Add(dot);top.Children.Add(new Border {Width=20,Height=2,Background=C("HeaderInk"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(5,0,0,0),Opacity=.7});stack.Children.Add(top);
        stack.Children.Add(new Border {Height=1.5,Background=C("Accent"),Opacity=.85});
        var body=new StackPanel {Margin=new Thickness(7,6,7,0)};stack.Children.Add(body);
        Border Line(string key,double width,double height=2,double bottom=5)=>new Border {Width=width,Height=height,Background=C(key),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,bottom)};
        var code=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,5)};code.Children.Add(new Border {Width=14,Height=3,Background=C("SqlKeyword"),Margin=new Thickness(0,0,4,0)});code.Children.Add(new Border {Width=22,Height=3,Background=C("Ink"),Margin=new Thickness(0,0,4,0)});code.Children.Add(new Border {Width=16,Height=3,Background=C("SqlString")});
        body.Children.Add(code);body.Children.Add(Line("Muted",58));body.Children.Add(Line("Accent",28,3,0));
        return mock;
    }
    private Button AccentSwatch(string name,string? hex)
    {
        FrameworkElement face;
        if(hex==null){var auto=new StackPanel {Orientation=Orientation.Horizontal};var chip=new Border {Width=14,Height=14,Margin=new Thickness(0,0,6,0)};refreshControls.Add(()=>chip.Background=ColorBrush(Themes.All.Single(p=>p.Id==settings.ThemeId).Colors["Accent"]));auto.Children.Add(chip);auto.Children.Add(new TextBlock {Text="Auto",VerticalAlignment=VerticalAlignment.Center});face=auto;}
        else face=new Border {Width=22,Height=22,Background=ColorBrush(hex)};
        return Swatch(face,name+(hex==null?" · follows the palette":" · "+hex),()=>settings.AccentHex==hex,()=>{AccentPicker.Visibility=Visibility.Collapsed;Change(settings with {AccentHex=hex});},hex==null);
    }
    // The custom swatch shows the mixed color once one is active; otherwise a hue wheel. It opens the mixer.
    private Button CustomSwatch()
    {
        var face=new Grid {Width=22,Height=22};var hues=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,1)};
        foreach(var (offset,color) in new[]{(0d,"#FF3B3B"),(.2,"#FFC83B"),(.4,"#3BFF6E"),(.6,"#3BC8FF"),(.8,"#8B3BFF"),(1d,"#FF3BC8")})hues.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color),offset));
        var fill=new Border {Background=hues};var plus=Glyph("",11);plus.Margin=new Thickness(0);plus.HorizontalAlignment=HorizontalAlignment.Center;plus.Foreground=Brushes.White;plus.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=3,ShadowDepth=0,Opacity=.8};
        face.Children.Add(fill);face.Children.Add(plus);
        bool IsCustom()=>settings.AccentHex!=null&&!Themes.Accents.Any(a=>a.Hex==settings.AccentHex);
        var button=Swatch(face,"Custom accent · mix any color",IsCustom,()=> {
            bool open=AccentPicker.Visibility!=Visibility.Visible;AccentPicker.Visibility=open?Visibility.Visible:Visibility.Collapsed;
            if(open)AccentPicker.Load(settings.AccentHex??((SolidColorBrush)Application.Current.Resources["Accent"]).Color.ToHex());
        },false);
        refreshControls.Add(()=> {bool custom=IsCustom();fill.Background=custom?ColorBrush(settings.AccentHex!):hues;plus.Text=custom?"":"";button.ToolTip=custom?"Custom accent · "+settings.AccentHex+" · click to edit":"Custom accent · mix any color";});
        return button;
    }
    private Button Swatch(FrameworkElement face,string tooltip,Func<bool> isCurrent,Action select,bool wide)
    {
        var ring=new Border {BorderThickness=new Thickness(2),Padding=new Thickness(2),Child=face};
        var button=new Button {Content=ring,Padding=new Thickness(wide?8:2,2,wide?8:2,2),Margin=new Thickness(0,0,6,6),ToolTip=tooltip,MinHeight=36,MinWidth=36,HorizontalContentAlignment=HorizontalAlignment.Center};button.SetResourceReference(StyleProperty,"OptionCard");
        button.Click+=(_,_)=>select();
        refreshControls.Add(()=> {bool on=isCurrent();ring.SetResourceReference(Border.BorderBrushProperty,on?"Ink":"Surface");button.SetResourceReference(BorderBrushProperty,on?"Accent":"FaintBorder");});
        return button;
    }
    private static Brush ColorBrush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    // ---- Text: fonts as sample cards, sizes as sliders with a readout. ----
    private void BuildText()
    {
        var section=Section("Text");
        var ui=Group(section,"INTERFACE","Menus, toolbar, tabs, dialogs and messages. The SQL editor keeps its own type.");
        ui.Children.Add(Heading("Interface font"));
        ui.Children.Add(FontChoices(("Inter","bundled",AppTypography.Inter,"Aa Bb Cc · Run query 123",()=>settings.UiFont=="inter",()=>Change(settings with {UiFont="inter"})),
            ("Segoe UI Variable","Windows",new FontFamily("Segoe UI Variable, Segoe UI"),"Aa Bb Cc · Run query 123",()=>settings.UiFont=="system",()=>Change(settings with {UiFont="system"}))));
        Number(ui,"Interface text size",11,17,()=>settings.UiSize,v=>Change(settings with {UiSize=v}));
        var sql=Group(section,"SQL EDITOR","Source text, result values and the editor gutter. Changes keep the SQL buffer, selection and undo history.");
        sql.Children.Add(Heading("SQL font"));
        sql.Children.Add(FontChoices(("JetBrains Mono","bundled",AppTypography.Mono,"select 0O 1lI from t;",()=>settings.EditorFont=="jetbrains",()=>Change(settings with {EditorFont="jetbrains"})),
            ("Consolas","Windows",new FontFamily("Consolas"),"select 0O 1lI from t;",()=>settings.EditorFont=="consolas",()=>Change(settings with {EditorFont="consolas"}))));
        Number(sql,"Editor text size",9,32,()=>settings.EditorSize,v=>Change(settings with {EditorSize=v}));
    }
    private UniformGrid FontChoices(params (string Name,string Tag,FontFamily Family,string Sample,Func<bool> IsCurrent,Action Select)[] options)
    {
        var grid=new UniformGrid {Columns=options.Length,Margin=new Thickness(0,0,-8,6)};
        foreach(var option in options){
            var sample=new Border {Padding=new Thickness(10,10,10,10),Height=50};sample.SetResourceReference(Border.BackgroundProperty,"Canvas");
            var text=new TextBlock {Text=option.Sample,FontFamily=option.Family,FontSize=17,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};text.SetResourceReference(TextBlock.ForegroundProperty,"Ink");sample.Child=text;
            grid.Children.Add(OptionCard(sample,option.Name,option.Tag,option.IsCurrent,option.Select));
        }
        return grid;
    }
    private void Number(StackPanel panel,string label,double min,double max,Func<double> value,Action<double> change)
    {
        var top=new DockPanel {Margin=new Thickness(0,10,0,6)};
        var readout=new Border {BorderThickness=new Thickness(1),Padding=new Thickness(8,1,8,1),VerticalAlignment=VerticalAlignment.Center};readout.SetResourceReference(Border.BorderBrushProperty,"FaintBorder");readout.SetResourceReference(Border.BackgroundProperty,"Canvas");var readoutText=Mono("",0);readoutText.SetResourceReference(TextBlock.ForegroundProperty,"Ink");readout.Child=readoutText;
        DockPanel.SetDock(readout,Dock.Right);top.Children.Add(readout);top.Children.Add(Heading(label,new Thickness(0)));panel.Children.Add(top);
        var row=new DockPanel();var low=Mono(min.ToString("0",CultureInfo.InvariantCulture),10);low.Width=22;low.SetResourceReference(TextBlock.ForegroundProperty,"LineNumber");var high=Mono(max.ToString("0",CultureInfo.InvariantCulture),10);high.Width=22;high.TextAlignment=TextAlignment.Right;high.SetResourceReference(TextBlock.ForegroundProperty,"LineNumber");
        DockPanel.SetDock(low,Dock.Left);DockPanel.SetDock(high,Dock.Right);row.Children.Add(low);row.Children.Add(high);
        var slider=new Slider {Minimum=min,Maximum=max,TickFrequency=1,IsSnapToTickEnabled=true,Value=value(),Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(slider);
        void Show()=>readoutText.Text=value().ToString("0",CultureInfo.InvariantCulture)+" px";
        refreshControls.Add(()=> {slider.Value=value();Show();});Show();slider.ValueChanged+=(_,_)=> {if(!refreshing)change(slider.Value);};panel.Children.Add(row);
    }

    // ---- Interface and Effects: setting rows with a title, a description and a toggle on the right. ----
    private void Toggle(StackPanel panel,string section,string title,string description,Func<bool> value,Action<bool> change,bool last=false)
    {
        var row=new DockPanel {Margin=new Thickness(0,0,0,last?0:2)};var frame=new Border {Child=row,Padding=new Thickness(0,10,0,10),BorderThickness=new Thickness(0,0,0,last?0:1)};frame.SetResourceReference(Border.BorderBrushProperty,"AccentRowRule");
        var box=new CheckBox {IsChecked=value(),Margin=new Thickness(16,0,0,0),VerticalAlignment=VerticalAlignment.Center,ToolTip=title};AutomationHelp(box,title);DockPanel.SetDock(box,Dock.Right);row.Children.Add(box);
        var text=new StackPanel();text.Children.Add(Heading(title,new Thickness(0,0,0,2)));text.Children.Add(Muted(description));row.Children.Add(text);
        // The whole row toggles, as a settings list does; the switch itself keeps keyboard focus and Space.
        text.Cursor=Cursors.Hand;text.Background=Brushes.Transparent;text.MouseLeftButtonUp+=(_,_)=>box.IsChecked=box.IsChecked!=true;
        refreshControls.Add(()=>box.IsChecked=value());box.Checked+=(_,_)=>{if(!refreshing)change(true);};box.Unchecked+=(_,_)=>{if(!refreshing)change(false);};
        Toggles[section+"/"+title]=box;panel.Children.Add(frame);
    }
    private static void AutomationHelp(CheckBox box,string name)=>System.Windows.Automation.AutomationProperties.SetName(box,name);
    private void BuildInterface()
    {
        var section=Section("Interface");
        var layout=Group(section,"LAYOUT","Density and shape of the work surface. Result data and SQL are never reflowed or changed.");
        Toggle(layout,"Interface","Compact result rows and toolbar","Tighter grid rows and toolbar padding for dense result sets.",()=>settings.Compact,v=>Change(settings with {Compact=v}));
        Toggle(layout,"Interface","Soft control corners","DBMON's rounded radii instead of Neo Mirai's square controls and corner brackets.",()=>settings.Rounded,v=>Change(settings with {Rounded=v}));
        Toggle(layout,"Interface","Show server messages","The Messages panel under the editor. It opens by itself when a run reports an error.",()=>settings.ShowMessages,v=>Change(settings with {ShowMessages=v}),last:true);
    }
    private void BuildEffects()
    {
        var section=Section("Effects");
        var companion=Group(section,"CONSOLE COMPANION","Boberto lives in the header band, beside the menus. Clicking him opens Boberto Studio.");
        Toggle(companion,"Effects","Show Boberto beside the editor","Hiding him suspends his renderer and returns the band to the workspace.",()=>settings.BobertoVisible,v=>Change(settings with {BobertoVisible=v}));
        var open=new Button {Content="Open Boberto Studio  →",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};open.Click+=(_,_)=>Sections.SelectedIndex=4;companion.Children.Add(open);
        var motion=Group(section,"MOTION","When off, the companion and Studio follow reduced motion. Windows' reduce-animation setting is honored as well.");
        Toggle(motion,"Effects","Animate Boberto and the neon edges","Idle gestures, breathing, the neon floor, the topbar sweep and the wordmark wave. Gestures you pick in Studio always play.",()=>settings.BobertoMotion,v=>Change(settings with {BobertoMotion=v}),last:true);
    }
    // Studio's page: reached from the header action, so its tab stays out of the section strip.
    private void BuildBoberto()=>Sections.Items.Add(new TabItem {Header=TabHeader("Boberto"),Content=new DockPanel(),Visibility=Visibility.Collapsed});
}

// Inline accent mixer: saturation/value field, hue rail, hex entry and the contrast it will have on the surface.
// Dragging previews locally; releasing the mouse, Enter or Apply commits one validated #RRGGBB through the owner.
public sealed class CustomAccentPicker:Border
{
    private readonly Action<string> commit;
    private double hue,saturation=1,value=1;
    private readonly Border field=new() {Width=232,Height=132,ClipToBounds=true};
    private readonly Ellipse fieldThumb=new() {Width=12,Height=12,StrokeThickness=2,Stroke=Brushes.White,IsHitTestVisible=false};
    private readonly Canvas fieldLayer=new();
    private readonly Border rail=new() {Width=232,Height=12,Margin=new Thickness(0,10,0,0)};
    private readonly Border railThumb=new() {Width=6,Height=16,BorderThickness=new Thickness(1.5),BorderBrush=Brushes.White,Background=Brushes.Transparent,IsHitTestVisible=false};
    private readonly Canvas railLayer=new();
    private readonly Border sample=new() {Width=56,Height=56,BorderThickness=new Thickness(1)};
    public TextBox Hex { get; }=new() {Width=104,MaxLength=7};
    private readonly TextBlock contrast=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    public string Current=>ColorFromHsv(hue,saturation,value).ToHex();

    public CustomAccentPicker(Action<string> onCommit)
    {
        commit=onCommit;BorderThickness=new Thickness(1);Padding=new Thickness(14);SetResourceReference(BorderBrushProperty,"FaintBorder");SetResourceReference(BackgroundProperty,"Canvas");
        var layout=new DockPanel();Child=layout;
        // Saturation left→right over the hue, value top→bottom toward black.
        var whites=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),0);var blacks=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Colors.Black,90);
        var fieldStack=new Grid();fieldStack.Children.Add(new Border {Background=whites});fieldStack.Children.Add(new Border {Background=blacks});fieldStack.Children.Add(fieldLayer);fieldLayer.Children.Add(fieldThumb);field.Child=fieldStack;field.Cursor=Cursors.Cross;
        var hues=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};for(int i=0;i<=6;i++)hues.GradientStops.Add(new GradientStop(ColorFromHsv(i*60,1,1),i/6d));
        var railStack=new Grid();railStack.Children.Add(new Border {Background=hues});railStack.Children.Add(railLayer);railLayer.Children.Add(railThumb);rail.Child=railStack;rail.Cursor=Cursors.Hand;
        var left=new StackPanel {Margin=new Thickness(0,0,18,0)};left.Children.Add(field);left.Children.Add(rail);DockPanel.SetDock(left,Dock.Left);layout.Children.Add(left);
        var right=new StackPanel {HorizontalAlignment=HorizontalAlignment.Left};var caption=new TextBlock {Text="Custom accent",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};caption.SetResourceReference(TextBlock.ForegroundProperty,"Ink");right.Children.Add(caption);sample.HorizontalAlignment=HorizontalAlignment.Left;sample.SetResourceReference(BorderBrushProperty,"StrongBorder");right.Children.Add(sample);
        var hexRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};Hex.SetResourceReference(Control.FontFamilyProperty,"MonoFont");Hex.Padding=new Thickness(8,5,8,5);hexRow.Children.Add(Hex);
        var apply=new Button {Content="Apply",Margin=new Thickness(6,0,0,0),MinHeight=0,Padding=new Thickness(12,4,12,4)};apply.SetResourceReference(BackgroundProperty,"Accent");apply.SetResourceReference(Control.ForegroundProperty,"OnAccent");apply.SetResourceReference(BorderBrushProperty,"Accent");apply.Click+=(_,_)=>CommitHex();hexRow.Children.Add(apply);right.Children.Add(hexRow);
        contrast.SetResourceReference(TextBlock.ForegroundProperty,"Muted");contrast.SetResourceReference(TextBlock.FontSizeProperty,"SmallFontSize");contrast.MaxWidth=230;right.Children.Add(contrast);layout.Children.Add(right);
        Hex.KeyDown+=(_,e)=> {if(e.Key==Key.Enter){CommitHex();e.Handled=true;}};
        Hex.TextChanged+=(_,_)=>{if(TryParse(Hex.Text,out var color)){SetFrom(color,updateHex:false);}};
        Drag(field,point=>{saturation=Math.Clamp(point.X/field.ActualWidth,0,1);value=1-Math.Clamp(point.Y/field.ActualHeight,0,1);});
        Drag(rail,point=>hue=Math.Clamp(point.X/rail.ActualWidth,0,.9999)*360);
        Loaded+=(_,_)=>Render();
    }
    public void Load(string hex){if(TryParse(hex,out var color))SetFrom(color,updateHex:true);}
    private void Drag(FrameworkElement surface,Action<Point> move)
    {
        surface.MouseLeftButtonDown+=(_,e)=>{surface.CaptureMouse();move(e.GetPosition(surface));Render();e.Handled=true;};
        surface.MouseMove+=(_,e)=>{if(!surface.IsMouseCaptured)return;move(e.GetPosition(surface));Render();};
        surface.MouseLeftButtonUp+=(_,e)=>{if(!surface.IsMouseCaptured)return;surface.ReleaseMouseCapture();commit(Current);e.Handled=true;};
    }
    private void CommitHex()
    {
        if(TryParse(Hex.Text,out var color)){SetFrom(color,updateHex:true);commit(color.ToHex());}
        else contrast.Text="Use six hex digits, for example #3BA7FF.";
    }
    private void SetFrom(Color color,bool updateHex)
    {
        (hue,saturation,value)=ToHsv(color);Render(updateHex);
    }
    private void Render(bool updateHex=true)
    {
        var color=ColorFromHsv(hue,saturation,value);
        field.Background=new SolidColorBrush(ColorFromHsv(hue,1,1));sample.Background=new SolidColorBrush(color);
        double width=field.ActualWidth>0?field.ActualWidth:field.Width,height=field.ActualHeight>0?field.ActualHeight:field.Height;
        Canvas.SetLeft(fieldThumb,saturation*width-6);Canvas.SetTop(fieldThumb,(1-value)*height-6);fieldThumb.Stroke=value>.6&&saturation<.4?Brushes.Black:Brushes.White;
        Canvas.SetLeft(railThumb,hue/360*(rail.ActualWidth>0?rail.ActualWidth:rail.Width)-3);Canvas.SetTop(railThumb,-2);
        if(updateHex&&!Hex.IsKeyboardFocusWithin)Hex.Text=color.ToHex();
        var surface=Application.Current.Resources["Surface"] is SolidColorBrush brush?brush.Color:Colors.Black;double ratio=Contrast(color,surface);
        contrast.Text=$"Contrast on surface {ratio:0.0}:1 · "+(ratio>=4.5?"reads as text and focus":ratio>=3?"fine for rules and fills":"too faint for text; light palettes deepen it");
    }
    public static bool TryParse(string text,out Color color)
    {
        color=default;text=text.Trim();if(!text.StartsWith('#'))text="#"+text;
        if(!AppearanceSettings.IsHex(text))return false;
        color=(Color)ColorConverter.ConvertFromString(text);return true;
    }
    public static double Contrast(Color a,Color b)
    {
        static double Linear(byte channel){double v=channel/255d;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
        static double Luminance(Color c)=>.2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);
        double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    public static Color ColorFromHsv(double h,double s,double v)
    {
        double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;
        var (r,g,b)=h switch {<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        byte B(double channel)=>(byte)Math.Round((channel+m)*255);return Color.FromRgb(B(r),B(g),B(b));
    }
    public static (double H,double S,double V) ToHsv(Color color)
    {
        double r=color.R/255d,g=color.G/255d,b=color.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double h=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);if(h<0)h+=360;
        return (h,max==0?0:delta/max,max);
    }
}

public static class ColorText
{
    public static string ToHex(this Color color)=>$"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
