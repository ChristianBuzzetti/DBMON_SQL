using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AseIsql.Desktop;

/// <summary>
/// How the DBMON SQL mark looks (Personalize → Effects → Logo). Tile "accent" follows the palette, "none" drops it.
/// The default is the shipped logo: a black round head with round eyes on the copper tile of the app icon.
/// </summary>
public sealed record MarkStyle(string Shape="circle",string Head="#0E1015",string Eyes="round",string Tile="#CB6A3E")
{
    public static readonly string[] Shapes=["circle","hex","squircle","diamond","ghost"];
    public static readonly string[] EyeStyles=["round","oval","happy","line","pixel"];
    public static readonly string[] HeadColors=["#0E1015","#F4F4F5","#CB6A3E","#00E5FF","#10B981","#F59E0B","#818CF8"];
    public static readonly string[] TileColors=["accent","#CB6A3E","#0E1015","#F4F4F5","#00E5FF","#10B981","#F59E0B","#818CF8","none"];

    public static MarkStyle From(AppearanceSettings settings)=>new(settings.MarkShape,settings.MarkHead,settings.MarkEyes,settings.MarkTile);
    public AppearanceSettings Save(AppearanceSettings settings)=>settings with {MarkShape=Shape,MarkHead=Head,MarkEyes=Eyes,MarkTile=Tile};
    public static Color ToColor(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// The DBMON SQL mark: Boberto's head with two eyes on a tile. The same drawing is the topbar logo, the window and
/// taskbar icon of a running app (<see cref="Render"/>) and the shipped .ico (<see cref="IconMaker"/>, always the
/// default style). It is a sibling of the BuzzPrint mark, told apart by the round head and the copper tile; the
/// raster Boberto (Assets/dbmon-sql.png) stays the mascot of the dock and Studio.
/// </summary>
public sealed class BobertoMark:FrameworkElement
{
    /// <summary>Copper, the Pergamino accent: the tile of the shipped icon and of the default mark.</summary>
    public static readonly Color Copper=Color.FromRgb(0xCB,0x6A,0x3E);

    public static readonly DependencyProperty TileProperty=DependencyProperty.Register(
        nameof(Tile),typeof(Brush),typeof(BobertoMark),
        new FrameworkPropertyMetadata(new SolidColorBrush(Copper),FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Tile brush used when the style's tile is "accent" (bind it to the Accent resource).</summary>
    public Brush Tile { get=>(Brush)GetValue(TileProperty); set=>SetValue(TileProperty,value); }

    /// <summary>The style every live mark uses; setting it restyles all of them at once.</summary>
    public static MarkStyle Current { get=>current; set {current=value;CurrentChanged?.Invoke();} }
    private static MarkStyle current=new();
    public static event Action? CurrentChanged;

    /// <summary>A fixed look, for the option cards in Personalize; null follows <see cref="Current"/>. Not named
    /// Style: that is FrameworkElement's own WPF style.</summary>
    public MarkStyle? Look { get=>look; set {look=value;InvalidateVisual();} }
    private MarkStyle? look;

    public BobertoMark()
    {
        Loaded+=(_,_)=>CurrentChanged+=InvalidateVisual;
        Unloaded+=(_,_)=>CurrentChanged-=InvalidateVisual;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size=Math.Min(ActualWidth,ActualHeight);
        if(size<=0)return;
        dc.PushTransform(new TranslateTransform((ActualWidth-size)/2,(ActualHeight-size)/2));
        Draw(dc,size,look??current,Tile);
        dc.Pop();
    }

    /// <summary>Draws the mark in a square of <paramref name="size"/> px, in a 64-unit space.</summary>
    public static void Draw(DrawingContext g,double size,MarkStyle st,Brush accent)
    {
        bool tiny=size<=40;                                   // at icon sizes the eyes need more room to survive
        g.PushTransform(new ScaleTransform(size/64,size/64));
        if(st.Tile!="none")g.DrawRoundedRectangle(st.Tile=="accent"?accent:new SolidColorBrush(MarkStyle.ToColor(st.Tile)),null,new Rect(2,2,60,60),14,14);
        bool bare=st.Tile=="none";
        if(bare)g.PushTransform(new ScaleTransform(1.25,1.25,32,32));   // no tile: the head fills the square

        var headColor=MarkStyle.ToColor(st.Head);
        var ink=new SolidColorBrush(headColor);
        var round=new Pen(ink,7) {LineJoin=PenLineJoin.Round};
        double cy=32;
        switch(st.Shape)
        {
            case "hex": g.DrawGeometry(ink,round,Poly((32,13),(48.5,22.5),(48.5,41.5),(32,51),(15.5,41.5),(15.5,22.5)));cy=31;break;
            case "squircle": g.DrawRoundedRectangle(ink,null,new Rect(13,13,38,38),13,13);break;
            case "diamond": g.DrawGeometry(ink,round,Poly((32,12),(52,32),(32,52),(12,32)));break;
            case "ghost":
                g.DrawGeometry(ink,null,Geometry.Parse("M14.5 32 A17.5 17.5 0 0 1 49.5 32 V49 Q46.6 52.5 43.7 49 Q40.8 45.5 37.9 49 Q35 52.5 32 49 Q29 45.5 26.1 49 Q23.2 52.5 20.3 49 Q17.4 45.5 14.5 49 Z"));
                cy=31;break;
            default: g.DrawEllipse(ink,null,new Point(32,32),19.5,19.5);break;
        }

        // Eyes contrast with the head: white on dark heads, ink on light ones.
        double luminance=(.2126*headColor.R+.7152*headColor.G+.0722*headColor.B)/255;
        var eye=new SolidColorBrush(luminance>.62?Color.FromRgb(0x0E,0x10,0x15):Color.FromRgb(0xF4,0xF4,0xF5));
        double gap=tiny?7:6.5;
        DrawEye(g,st.Eyes,eye,new Point(32-gap,cy),tiny);
        DrawEye(g,st.Eyes,eye,new Point(32+gap,cy),tiny);
        if(bare)g.Pop();
        g.Pop();
    }

    private static void DrawEye(DrawingContext g,string kind,Brush brush,Point centre,bool tiny)
    {
        double k=tiny?1.2:1;
        switch(kind)
        {
            case "oval": g.DrawEllipse(brush,null,centre,3.6*k,5.4*k);break;
            case "line": g.DrawRoundedRectangle(brush,null,new Rect(centre.X-4.6*k,centre.Y-1.6*k,9.2*k,3.2*k),1.6*k,1.6*k);break;
            case "pixel": g.DrawRoundedRectangle(brush,null,new Rect(centre.X-3.5*k,centre.Y-3.5*k,7*k,7*k),1.2,1.2);break;
            case "happy":
            {
                // ∩ arc: a smiling, squinting eye.
                var arc=new StreamGeometry();
                using(var ctx=arc.Open()){ctx.BeginFigure(new Point(centre.X-4.2*k,centre.Y+2*k),false,false);ctx.QuadraticBezierTo(new Point(centre.X,centre.Y-5.5*k),new Point(centre.X+4.2*k,centre.Y+2*k),true,true);}
                g.DrawGeometry(null,new Pen(brush,3.2*k) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},arc);
                break;
            }
            default: g.DrawEllipse(brush,null,centre,4.6*k,4.6*k);break;
        }
    }

    private static Geometry Poly(params (double X,double Y)[] points)
    {
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X,points[0].Y),true,true);
            context.PolyLineTo(points.Skip(1).Select(p=>new Point(p.X,p.Y)).ToList(),true,true);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The mark as a bitmap of <paramref name="size"/> px (window icon, icon entries, previews).</summary>
    public static BitmapSource Render(int size,Brush accent,MarkStyle? style=null)
    {
        var visual=new DrawingVisual();
        using(var g=visual.RenderOpen())Draw(g,size,style??current,accent);
        var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
