using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace AseIsql.Desktop;

public sealed record ThemePalette(string Id, string Label, bool DarkChrome, IReadOnlyDictionary<string, string> Colors)
{
    public override string ToString() => Label;
}

public static class Themes
{
    public sealed record AccentChoice(string Name, string? Hex)
    {
        public string Swatch => Hex ?? "#8FA4C0";
        public override string ToString() => Name;
    }
    public static IReadOnlyList<AccentChoice> Accents { get; } = [new("Theme accent", null), new("Cyan", "#00E5FF"), new("Blue", "#5B8CFF"), new("Violet", "#818CF8"), new("Emerald", "#10B981"), new("Teal", "#2DD4BF"), new("Amber", "#F59E0B"), new("Red", "#F43F5E"), new("Pink", "#EC4899"), new("Copper", "#CB6A3E"), new("Ice", "#88C0D0"), new("Lavender", "#BD93F9"), new("Magenta", "#FF2E97"), new("Lime", "#39FF14"), new("Rose", "#EB6F92")];
    public static ThemePalette WithAccent(ThemePalette palette, string? hex)
    {
        var colors = new Dictionary<string,string>(palette.Colors);
        var accent = (Color)ColorConverter.ConvertFromString(hex??colors["Accent"]);
        double Luminance(Color c) { double Linear(byte b) {double v=b/255d; return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);} return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B); }
        var surface=(Color)ColorConverter.ConvertFromString(colors["Surface"]);
        double Contrast(Color a,Color b) {double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        // Light palettes deepen a bright swatch until it remains legible as text and focus.
        while(Contrast(accent,surface)<4.5 && !palette.DarkChrome) accent=Color.FromRgb((byte)(accent.R*.9),(byte)(accent.G*.9),(byte)(accent.B*.9));
        if(hex!=null)colors["Accent"]=$"#{accent.R:X2}{accent.G:X2}{accent.B:X2}";
        colors["OnAccent"]=Contrast(accent,Colors.White)>=Contrast(accent,Color.FromRgb(7,16,31))?"#FFFFFF":"#07101F";
        byte Mix(byte foreground,byte background)=>(byte)Math.Round(foreground*.45+background*.55);
        colors["Selection"]=$"#{Mix(accent.R,surface.R):X2}{Mix(accent.G,surface.G):X2}{Mix(accent.B,surface.B):X2}";
        return palette with { Colors=colors };
    }
    private static readonly string[] Keys = ["Canvas", "Surface", "SurfaceRaised", "AlternateRow", "Ink", "Muted", "Rule", "RowRule", "Accent", "OnAccent", "Header", "HeaderInk", "Badge", "BadgeInk", "Notice", "NoticeInk", "LineNumber", "Selection", "SqlKeyword", "SqlString", "SqlComment", "SqlNumber", "ErrorInk"];
    public static IReadOnlyList<ThemePalette> All { get; } = LoadPalettes();
    public static ThemePalette Current { get; private set; } = All[0];
    private static IReadOnlyList<ThemePalette> LoadPalettes()
    {
        var palettes = System.Text.Json.JsonSerializer.Deserialize<List<ThemePalette>>(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "dbmon-palettes.json"))) ?? throw new InvalidOperationException("Missing theme catalog.");
        if (palettes.Count == 0 || palettes.Select(p => p.Id).Distinct().Count() != palettes.Count || palettes.Any(p => Keys.Any(key => !p.Colors.ContainsKey(key)))) throw new InvalidOperationException("Invalid theme catalog.");
        return palettes;
    }

    public static void Apply(ThemePalette palette, TextEditor? editor = null)
    {
        Current = palette;
        var resources = Application.Current.Resources;
        foreach (var (key, value) in palette.Colors) resources[key] = Brush(value);
        resources[SystemColors.WindowBrushKey] = resources["Surface"];
        resources[SystemColors.WindowTextBrushKey] = resources["Ink"];
        resources[SystemColors.ControlBrushKey] = resources["SurfaceRaised"];
        resources[SystemColors.ControlTextBrushKey] = resources["Ink"];
        resources[SystemColors.HighlightBrushKey] = resources["Selection"];
        resources[SystemColors.HighlightTextBrushKey] = resources["Ink"];
        resources[SystemColors.GrayTextBrushKey] = resources["Muted"];
        resources[SystemColors.MenuBrushKey] = resources["SurfaceRaised"];
        resources[SystemColors.MenuTextBrushKey] = resources["Ink"];
        ApplyDerived(palette, resources);
        if (editor == null) return;
        editor.TextArea.SelectionBrush = (Brush)resources["Selection"];
        editor.TextArea.SelectionForeground = (Brush)resources["Ink"];
        editor.TextArea.SelectionBorder = null;
        // Fresh definitions avoid mutating shared AvalonEdit highlighting objects.
        string xml = $"""
            <SyntaxDefinition name="ASE SQL" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="{palette.Colors["SqlComment"]}"/>
              <Color name="String" foreground="{palette.Colors["SqlString"]}"/>
              <Color name="Keyword" foreground="{palette.Colors["SqlKeyword"]}" fontWeight="bold"/>
              <Color name="Number" foreground="{palette.Colors["SqlNumber"]}"/>
              <RuleSet ignoreCase="true">
                <Span color="Comment" begin="--"/>
                <Span color="Comment" multiline="true" begin="/\*" end="\*/"/>
                <Span color="String" multiline="true" begin="'" end="'"/>
                <Keywords color="Keyword"><Word>SELECT</Word><Word>FROM</Word><Word>WHERE</Word><Word>AS</Word><Word>NULL</Word><Word>GO</Word><Word>DBCC</Word><Word>EXEC</Word><Word>DECLARE</Word><Word>BEGIN</Word><Word>END</Word><Word>INSERT</Word><Word>UPDATE</Word><Word>DELETE</Word><Word>JOIN</Word><Word>ON</Word><Word>ORDER</Word><Word>BY</Word><Word>GROUP</Word><Word>HAVING</Word><Word>AND</Word><Word>OR</Word><Word>IN</Word><Word>IS</Word><Word>NOT</Word><Word>TOP</Word><Word>SET</Word><Word>USE</Word></Keywords>
                <Rule color="Number">\b[0-9]+(\.[0-9]+)?\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new System.IO.StringReader(xml));
        editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    // DBMON derives its decoration from the accent with color-mix(); these are the same mixes as alpha brushes
    // (skin-neomirai.css): ring 60%, card ring 32%, outer card ring 42%, header rule 45%, row rule 10%, glow 22%.
    private static void ApplyDerived(ThemePalette palette, ResourceDictionary resources)
    {
        var accent = (Color)ColorConverter.ConvertFromString(palette.Colors["Accent"]);
        var ink = (Color)ColorConverter.ConvertFromString(palette.Colors["Ink"]);
        var canvas = (Color)ColorConverter.ConvertFromString(palette.Colors["Canvas"]);
        SolidColorBrush Mix(Color c, double alpha) { var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B)); brush.Freeze(); return brush; }
        resources["AccentRing"] = Mix(accent, .60);
        resources["AccentCardRing"] = Mix(accent, .32);
        resources["AccentCardOuter"] = Mix(accent, .42);
        resources["AccentRule"] = Mix(accent, .45);
        resources["AccentRowRule"] = Mix(accent, .10);
        resources["AccentWeak"] = Mix(accent, .13);
        resources["AccentTint"] = Mix(accent, .07);
        resources["AccentGlowColor"] = accent;
        resources["TopbarRule"] = Mix(accent, .37);
        resources["TopbarGlow"] = Mix(accent, .14);
        resources["FaintBorder"] = Mix(ink, .08);
        resources["Ok"] = Brush(palette.Colors["SqlString"]);
        resources["OkGlow"] = Mix((Color)ColorConverter.ConvertFromString(palette.Colors["SqlString"]), .25);
        resources["StrongBorder"] = Mix(ink, .16);
        // Neo Mirai background: a 112 px grid of 1 px lines in the text color at 3.5% (vertical) and 2.5% (horizontal).
        var grid = new DrawingGroup();
        grid.Children.Add(new GeometryDrawing(new SolidColorBrush(canvas), null, new RectangleGeometry(new Rect(0, 0, 112, 112))));
        grid.Children.Add(new GeometryDrawing(Mix(ink, .035), null, new RectangleGeometry(new Rect(0, 0, 1, 112))));
        grid.Children.Add(new GeometryDrawing(Mix(ink, .025), null, new RectangleGeometry(new Rect(0, 0, 112, 1))));
        var gridBrush = new DrawingBrush(grid) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 112, 112), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        gridBrush.Freeze(); resources["CanvasGrid"] = gridBrush;
        // Subbar hairlines: a 1 px accent line at 5% every 8 px.
        var hair = new DrawingGroup();
        hair.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        hair.Children.Add(new GeometryDrawing(Mix(accent, .05), null, new RectangleGeometry(new Rect(0, 0, 1, 8))));
        var hairBrush = new DrawingBrush(hair) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 8, 8), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        hairBrush.Freeze(); resources["SubbarHairlines"] = hairBrush;
        // smg_edge_bar_css(): transparent → accent 45% → 85% → 45% → transparent.
        var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(115, accent.R, accent.G, accent.B), .14));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(217, accent.R, accent.G, accent.B), .5));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(115, accent.R, accent.G, accent.B), .86));
        edge.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
        edge.Freeze(); resources["EdgeBar"] = edge;
        byte Lift(byte c) => (byte)Math.Min(255, c + (255 - c) * .26);
        resources["EdgeSweepColor"] = Color.FromArgb(240, Lift(accent.R), Lift(accent.G), Lift(accent.B));
    }

    private static SolidColorBrush Brush(string value) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }

    public static void ApplyChrome(Window window, ThemePalette palette)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        int enabled = palette.DarkChrome ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, ref int value, int size);
}
