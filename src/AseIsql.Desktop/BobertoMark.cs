using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AseIsql.Desktop;

/// <summary>
/// How the DBMON SQL mark looks (Personalize → Effects → Logo). Tile "accent" follows the palette, "none" drops it.
/// The default is the shipped logo: a black round head with round eyes on the palette accent, copper in Pergamino.
/// </summary>
public sealed record MarkStyle(string Shape="circle",string Head="#0E1015",string Eyes="round",string Tile="accent")
{
    // Same engine and catalogue as the BuzzPrint mark, so one customizer reads like the other. DBMON SQL is told apart
    // by its own default (a round head with round eyes), not by a different catalogue.
    public static readonly string[] Shapes=["hex","circle","blob","pebble","cloud","drop","clover","flower","star","burst","heart","squircle","diamond","ghost"];
    public static readonly string[] EyeStyles=["pill","oval","round","happy","line","pixel"];
    public static readonly string[] HeadColors=["#0E1015","#F4F4F5","#A86B3C","#F43F5E","#F97316","#F59E0B","#10B981","#14B8A6","#2684FF","#8B5CF6","#EC4899","#71717A"];
    public static readonly string[] TileColors=["accent","none","#0E1015","#F4F4F5","#F43F5E","#F59E0B","#10B981","#2684FF","#8B5CF6"];

    public static MarkStyle From(AppearanceSettings settings)=>new(settings.MarkShape,settings.MarkHead,settings.MarkEyes,settings.MarkTile);
    public AppearanceSettings Save(AppearanceSettings settings)=>settings with {MarkShape=Shape,MarkHead=Head,MarkEyes=Eyes,MarkTile=Tile};
    public static bool IsHex(string? value)=>AppearanceSettings.IsHex(value);
    public static Color ToColor(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
}

/// <summary>
/// The DBMON SQL mark: Boberto's head with two eyes on a tile, the same engine as BuzzPrint's. The same drawing is the
/// topbar logo, the icon of the running windows (<see cref="Render"/>) and the shipped .ico (<see cref="IconMaker"/>,
/// always the default style); the raster Boberto stays the mascot of the dock and Studio.
/// Alive marks sway and float gently, bounce under the mouse, squash when they change and morph from one head to the
/// next. Sway, float, bounce and squash are WPF animations on the render transform (composited, smooth); only the
/// morph, a blink or the eyes moving redraw. The header mark's eyes follow the cursor. Animations off = still mark.
/// </summary>
public sealed class BobertoMark : FrameworkElement
{
    /// <summary>Copper, the Pergamino accent: the tile of the default in-app mark.</summary>
    public static readonly Color Copper = Color.FromRgb(0xCB, 0x6A, 0x3E);
    /// <summary>Teal, the tile of the shipped icon (exe, taskbar, Store). Kept cool so it never reads like BuzzPrint's red-pink icon.</summary>
    public static readonly Color IconTeal = Color.FromRgb(0x0F, 0xA3, 0xA3);

    public static readonly DependencyProperty TileProperty = DependencyProperty.Register(nameof(Tile), typeof(Brush), typeof(BobertoMark),
        new FrameworkPropertyMetadata(new SolidColorBrush(Copper), FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Tile brush used when the style's tile is "accent" (bind it to the Accent resource).</summary>
    public Brush Tile { get => (Brush)GetValue(TileProperty); set => SetValue(TileProperty, value); }

    /// <summary>The style every live mark uses; set it to restyle all of them at once.</summary>
    public static MarkStyle Current { get => current; set { current = value; CurrentChanged?.Invoke(); } }
    static MarkStyle current = new();
    public static event Action? CurrentChanged;
    /// <summary>A fixed look for previews (option cards); null = <see cref="Current"/>. Not named Style: that is
    /// FrameworkElement's own WPF style.</summary>
    public MarkStyle? Look { get => style; set { var before = Effective; style = value; Changed(before); } }
    MarkStyle? style;
    MarkStyle Effective => style ?? current;

    /// <summary>App-wide motion setting ("Animate Boberto" and Windows animations): new marks start animated or still.</summary>
    public static bool Motion { get; set; } = true;

    /// <summary>Eyes follow the cursor (header mark). Off for picker tiles, which still blink and move.</summary>
    public bool Follow { get; set; } = true;
    /// <summary>Turns without pause (loading indicator).</summary>
    public bool Spinning { get; set; }
    /// <summary>Turns without pause while this returns true (the topbar mark while any session runs SQL); a turn
    /// in progress finishes upright when it turns false.</summary>
    public Func<bool>? SpinWhile { get; set; }
    /// <summary>Resting tilt in degrees, so a row of shapes looks hand-placed rather than stamped.</summary>
    public double Tilt { get => tilt; set { tilt = value; rotate.Angle = value; if (animated && IsLoaded) StartIdle(); } }
    double tilt;

    /// <summary>Follow the cursor, blink and move. Off = still mark (reduced motion).</summary>
    public bool Animated
    {
        get => animated;
        set { animated = value; if (!value) { eye = target = default; blinkL = blinkR = 1; StopMotion(); InvalidateVisual(); } else if (IsLoaded) StartIdle(); Sync(); }
    }

    readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly Random random = new();
    readonly RotateTransform rotate = new(), wiggle = new();
    readonly ScaleTransform hover = new(1, 1), squish = new(1, 1);
    readonly TranslateTransform bob = new();
    bool animated = Motion;
    Vector eye, target;               // eye offset in 64-unit space
    double blinkL = 1, blinkR = 1;    // 1 open, 0 closed
    DateTime nextBlink = DateTime.Now.AddSeconds(2), blinkStart = DateTime.MinValue;
    enum Blink { Both, Alternate, Double }
    Blink blink;
    double blinkUnit = 150;
    DateTime nextGlance = DateTime.Now.AddMilliseconds(600);
    DateTime nextSpin = DateTime.MinValue, spinStart = DateTime.MinValue;
    double spin;                      // head rotation in degrees while a turn plays
    const double SpinMs = 1150;
    Outline? morphFrom; DateTime morphStart = DateTime.MinValue;
    const double MorphMs = 420;
    MarkStyle? lastDrawn;

    public BobertoMark()
    {
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(.5, .55);
        RenderTransform = new TransformGroup { Children = { squish, hover, wiggle, rotate, bob } };
        timer.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) => Sync();
        Loaded += (_, _) => { CurrentChanged += OnCurrentChanged; if (animated) StartIdle(); Sync(); };
        Unloaded += (_, _) => { CurrentChanged -= OnCurrentChanged; timer.Stop(); StopMotion(); };
        MouseLeftButtonDown += (_, _) => { if (animated) Start((Blink)random.Next(3), 170); };
    }

    /// <summary>The living mark beside a heading, so Boberto is present across the app. Watches the cursor.</summary>
    public static FrameworkElement WithBadge(UIElement content, double size = 48)
    {
        var mark = new BobertoMark { Width = size, Height = size, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        mark.SetResourceReference(TileProperty, "Accent");
        var row = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(mark, System.Windows.Controls.Dock.Left);
        row.Children.Add(mark);
        row.Children.Add(content);
        return row;
    }

    /// <summary>
    /// Gives a tool window BuzzPrint's heading: the living mark, the window title and, after " · ", its subtitle, above
    /// the window's own content. Applied once per window when it loads (App). Windows that already show the mark (the
    /// main window, Personalize, About, the app dialogs) or set Tag "no-badge" are left alone.
    /// </summary>
    public static void Brand(Window window)
    {
        if (window.Tag as string == "no-badge" || window.Tag as string == "badged" || window is MainWindow or PersonalizationWindow or AboutWindow) return;
        if (window.Content is not UIElement content || Contains(content)) return;
        window.Tag ??= "badged";
        string title = window.Title ?? "";
        int dot = title.IndexOf(" · ", StringComparison.Ordinal);
        var texts = new System.Windows.Controls.StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new System.Windows.Controls.TextBlock { Text = dot > 0 ? title[..dot] : title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        head.SetResourceReference(System.Windows.Controls.TextBlock.FontSizeProperty, "SectionFontSize");
        head.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Ink");
        texts.Children.Add(head);
        if (dot > 0)
        {
            var sub = new System.Windows.Controls.TextBlock { Text = title[(dot + 3)..], TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            sub.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Muted");
            sub.SetResourceReference(System.Windows.Controls.TextBlock.FontSizeProperty, "SmallFontSize");
            texts.Children.Add(sub);
        }
        var badge = WithBadge(texts, 40);
        System.Windows.Controls.DockPanel.SetDock(badge, System.Windows.Controls.Dock.Top);
        // A dock or vertical stack layout takes the heading as its first child, so the window keeps its own structure
        // (and its margins); any other content is wrapped.
        if (content is System.Windows.Controls.DockPanel dock && dock.LastChildFill || content is System.Windows.Controls.StackPanel { Orientation: System.Windows.Controls.Orientation.Vertical })
        {
            // A layout without its own margin gets one for the heading, so the mark does not touch the window edge.
            badge.Margin = ((FrameworkElement)content).Margin.Left < 8 ? new Thickness(14, 12, 14, 10) : new Thickness(0, 0, 0, 14);
            ((System.Windows.Controls.Panel)content).Children.Insert(0, badge);
            return;
        }
        badge.Margin = new Thickness(20, 16, 20, 6);
        var shell = new System.Windows.Controls.DockPanel();
        window.Content = null;
        shell.Children.Add(badge);
        shell.Children.Add(content);
        window.Content = shell;
    }

    static bool Contains(DependencyObject node)
    {
        if (node is BobertoMark) return true;
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject d && Contains(d)) return true;
        return false;
    }

    // ------------------------------------------------------------------ motion (composited)
    void StartIdle()
    {
        // Still body: no sway, float or hover bounce. The mark watches with its eyes and turns now and then.
        rotate.BeginAnimation(RotateTransform.AngleProperty, null); rotate.Angle = tilt;
    }

    void StopMotion()
    {
        rotate.BeginAnimation(RotateTransform.AngleProperty, null); rotate.Angle = tilt;
        bob.BeginAnimation(TranslateTransform.YProperty, null); bob.Y = 0;
        foreach (var dp in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) { hover.BeginAnimation(dp, null); squish.BeginAnimation(dp, null); }
        hover.ScaleX = hover.ScaleY = squish.ScaleX = squish.ScaleY = 1;
        wiggle.BeginAnimation(RotateTransform.AngleProperty, null); wiggle.Angle = 0;
    }

    /// <summary>A springy squash, as when the shape changes.</summary>
    public void Squish()
    {
        if (!animated) return;
        DoubleAnimationUsingKeyFrames Frames(params double[] v)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(560) };
            double[] at = { 0, 110, 240, 370, 560 };
            for (int i = 0; i < v.Length; i++) a.KeyFrames.Add(new EasingDoubleKeyFrame(v[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at[i])), new SineEase { EasingMode = EasingMode.EaseOut }));
            return a;
        }
        squish.BeginAnimation(ScaleTransform.ScaleXProperty, Frames(1, 1.2, .92, 1.04, 1));
        squish.BeginAnimation(ScaleTransform.ScaleYProperty, Frames(1, .82, 1.08, .97, 1));
    }

    void OnCurrentChanged() { if (style == null) Changed(lastDrawn); else InvalidateVisual(); }

    /// <summary>A new style: morph the head from the one on screen and squash; still marks just redraw.</summary>
    void Changed(MarkStyle? before)
    {
        if (animated && IsLoaded && before != null && before.Shape != Effective.Shape)
        {
            morphFrom = Outline.For(before.Shape);
            morphStart = DateTime.Now;
            Squish();
            Sync();
        }
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ eyes, blink, morph (redraw only when needed)
    void Start(Blink kind, double unit) { blink = kind; blinkUnit = unit; blinkStart = DateTime.Now; }

    void Sync()
    {
        bool run = animated && IsVisible && IsLoaded;
        if (run && !timer.IsEnabled) timer.Start(); else if (!run && timer.IsEnabled) timer.Stop();
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);

    static double Open(double t) => t < 0 || t >= 1 ? 1 : Math.Abs(1 - 2 * t);   // closes, then reopens

    void Step()
    {
        var window = Window.GetWindow(this);
        if (window == null || window.WindowState == WindowState.Minimized) return;
        bool dirty = false;
        if (Follow && GetCursorPos(out var p) && PresentationSource.FromVisual(this) != null)
        {
            Point centre = PointToScreen(new Point(ActualWidth / 2, ActualHeight * .48));
            var d = new Vector(p.X - centre.X, p.Y - centre.Y);
            double len = d.Length;
            target = len < 1 ? default : d / len * Math.Min(1, len / 220);
            target = new Vector(target.X * 3.4, target.Y * 2.6);
        }
        else if (DateTime.Now >= nextGlance)
        {
            // Look around now and then: sideways, up or down, or back to the front.
            double r = random.NextDouble();
            target = r < .3 ? default : new Vector((random.NextDouble() * 2 - 1) * 3.4, (random.NextDouble() * 2 - 1) * 2.2);
            nextGlance = DateTime.Now.AddMilliseconds(1400 + random.Next(2600));
        }
        var next = eye + (target - eye) * .12;
        if ((next - eye).LengthSquared > 0.0004) { eye = next; dirty = true; }

        var now = DateTime.Now;
        if (blinkStart == DateTime.MinValue && now >= nextBlink) Start(random.NextDouble() < .15 ? Blink.Double : Blink.Both, 150);
        if (blinkStart != DateTime.MinValue)
        {
            dirty = true;
            double t = (now - blinkStart).TotalMilliseconds / blinkUnit;
            double length;
            switch (blink)
            {
                case Blink.Alternate: blinkL = Open(t); blinkR = Open(t - .6); length = 1.6; break;
                case Blink.Double: blinkL = blinkR = Math.Min(Open(t), Open(t - 1.3)); length = 2.3; break;
                default: blinkL = blinkR = Open(t); length = 1; break;
            }
            blinkL = Math.Max(.08, blinkL); blinkR = Math.Max(.08, blinkR);
            if (t >= length)
            {
                blinkStart = DateTime.MinValue; blinkL = blinkR = 1;
                nextBlink = now.AddMilliseconds(2500 + random.Next(3500));
            }
        }
        if (nextSpin == DateTime.MinValue) nextSpin = now.AddMilliseconds(3000 + random.Next(6000));
        if (spinStart == DateTime.MinValue && (now >= nextSpin || Spinning || SpinWhile?.Invoke() == true)) spinStart = now;
        if (spinStart != DateTime.MinValue)
        {
            dirty = true;
            double t = Math.Min(1, (now - spinStart).TotalMilliseconds / SpinMs);
            // Ease in, a quick whirl, ease out back to upright.
            double e = t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            spin = 360 * e;
            if (t >= 1) { spin = 0; spinStart = DateTime.MinValue; nextSpin = now.AddMilliseconds(8000 + random.Next(7000)); }
        }
        if (morphFrom != null) { dirty = true; if ((now - morphStart).TotalMilliseconds >= MorphMs) morphFrom = null; }
        if (dirty) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        lastDrawn = Effective;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        Outline? head = null;
        if (morphFrom != null)
        {
            double t = Math.Min(1, (DateTime.Now - morphStart).TotalMilliseconds / MorphMs);
            double k = 1 + 2.2 * Math.Pow(t - 1, 3) + 1.2 * Math.Pow(t - 1, 2);   // ease out with a jelly overshoot
            head = Outline.Lerp(morphFrom, Outline.For(Effective.Shape), k);
        }
        Draw(dc, s, Effective, Tile, eye, blinkL, blinkR, head, spin);
        dc.Pop();
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>Draws the mark in a square of <paramref name="size"/> px. Eye offset is in 64-unit space.</summary>
    public static void Draw(DrawingContext g, double size, MarkStyle st, Brush accent, Vector eye = default, double blinkLeft = 1, double blinkRight = 1, Outline? headOverride = null, double spin = 0)
    {
        bool tiny = size <= 40;
        g.PushTransform(new ScaleTransform(size / 64, size / 64));
        if (st.Tile != "none") g.DrawRoundedRectangle(st.Tile == "accent" ? accent : Frozen(MarkStyle.ToColor(st.Tile)), null, new Rect(2, 2, 60, 60), 14, 14);
        bool bare = st.Tile == "none";
        if (bare) g.PushTransform(new ScaleTransform(1.25, 1.25, 32, 32));   // no tile: the head fills the square

        var headColor = MarkStyle.ToColor(st.Head);
        var outline = headOverride ?? Outline.For(st.Shape);
        Geometry shape = outline.Geometry();
        if (spin % 360 != 0)
        {
            var turned = new GeometryGroup { Children = { shape }, Transform = new RotateTransform(spin, 32, 32) };
            turned.Freeze();
            shape = turned;
        }
        double cy = outline.EyeY;

        // A 2.5D turn, the way a solid toy head reads: where the eyes look, the head turns. Yaw/pitch come from the
        // eye offset (-1..1). The face narrows on the turning axis and slides toward the look, its far side shows as a
        // darker body behind it (an extrusion), the eyes slide further than the outline (parallax) and bunch up, and a
        // soft light stays put while the face moves under it. At rest it is a chunky object with a little depth below.
        double yaw = Math.Clamp(eye.X / 3.4, -1, 1), pitch = Math.Clamp(eye.Y / 2.6, -1, 1);
        double ay = Math.Abs(yaw), ap = Math.Abs(pitch);
        double sx = 1 - .18 * ay, sy = 1 - .11 * ap;
        double fx = yaw * 1.8, fy = pitch * 1.3;
        Matrix Face(double ox, double oy) => new(sx, 0, 0, sy, 32 - 32 * sx + ox, 32 - 32 * sy + oy);
        var back = new Vector(-yaw * 4.4, -pitch * 3.2 + 1.8 * (1 - ap));
        var sideColor = Shade(headColor, .62);
        const int Layers = 5;
        for (int i = Layers; i >= 1; i--)
        {
            g.PushTransform(new MatrixTransform(Face(fx + back.X * i / Layers, fy + back.Y * i / Layers)));
            g.DrawGeometry(Frozen(i == Layers ? Shade(headColor, .5) : sideColor), null, shape);
            g.Pop();
        }

        g.PushTransform(new MatrixTransform(Face(fx, fy)));
        g.DrawGeometry(Frozen(headColor), null, shape);
        if (!tiny || size >= 24)
        {
            g.PushClip(shape);
            // Light from the upper left; it stays in place, so the turning face slides under it.
            var light = new RadialGradientBrush(Color.FromArgb(84, 255, 255, 255), Color.FromArgb(0, 255, 255, 255))
                { MappingMode = BrushMappingMode.Absolute, Center = new Point(24 - fx * 2.2, cy - 12 - fy * 2), GradientOrigin = new Point(24 - fx * 2.2, cy - 12 - fy * 2), RadiusX = 24, RadiusY = 20 };
            light.Freeze();
            g.DrawRectangle(light, null, new Rect(0, 0, 64, 64));
            // The far side falls into shadow as it turns away.
            if (ay > .05 || ap > .05)
            {
                var far = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(32 - yaw * 26, cy - pitch * 22), EndPoint = new Point(32 + yaw * 4, cy + pitch * 4) };
                far.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(95 * Math.Max(ay, ap)), 0, 0, 0), 0));
                far.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
                far.Freeze();
                g.DrawRectangle(far, null, new Rect(0, 0, 64, 64));
            }
            g.Pop();
        }

        // Eyes contrast with the head: white on dark heads, ink on light ones.
        double lum = (.2126 * headColor.R + .7152 * headColor.G + .0722 * headColor.B) / 255;
        var eyeBrush = lum > .62 ? DarkEye : LightEye;
        double gap = (tiny ? 7 : 6.5) * (1 - .28 * ay);
        double ex = 32 + yaw * 3.8, ey = cy + pitch * 3;
        // The eye on the far side is a touch smaller (it is further away).
        DrawEyeScaled(g, st.Eyes, eyeBrush, new Point(ex - gap, ey), blinkLeft, tiny, 1 - .16 * Math.Max(0, yaw));
        DrawEyeScaled(g, st.Eyes, eyeBrush, new Point(ex + gap, ey), blinkRight, tiny, 1 - .16 * Math.Max(0, -yaw));
        g.Pop();
        if (bare) g.Pop();
        g.Pop();
    }

    static readonly Dictionary<Color, Brush> brushes = [];   // declared first: the eye brushes below use it
    static readonly Brush DarkEye = Frozen(Color.FromRgb(0x0E, 0x10, 0x15)), LightEye = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF5));
    static Brush Frozen(Color c)
    {
        if (brushes.TryGetValue(c, out var b)) return b;
        var nb = new SolidColorBrush(c); nb.Freeze();
        return brushes[c] = nb;
    }

    static Color Shade(Color c, double k)
    {
        // Darken; very dark heads lighten instead so their body still reads against the tile.
        double lum = (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255;
        if (lum < .12) return Color.FromRgb((byte)Math.Min(255, c.R + 70 * (1.2 - k)), (byte)Math.Min(255, c.G + 70 * (1.2 - k)), (byte)Math.Min(255, c.B + 76 * (1.2 - k)));
        return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }

    static void DrawEyeScaled(DrawingContext g, string kind, Brush b, Point c, double open, bool tiny, double scale)
    {
        if (Math.Abs(scale - 1) < .01) { DrawEye(g, kind, b, c, open, tiny); return; }
        g.PushTransform(new ScaleTransform(scale, scale, c.X, c.Y));
        DrawEye(g, kind, b, c, open, tiny);
        g.Pop();
    }

    static void DrawEye(DrawingContext g, string kind, Brush b, Point c, double open, bool tiny)
    {
        double k = tiny ? 1.2 : 1;
        switch (kind)
        {
            case "round": g.DrawEllipse(b, null, c, 4.6 * k, 4.6 * k * open); break;
            case "pill": { double h = 11.5 * k * open, w = 5.2 * k; g.DrawRoundedRectangle(b, null, new Rect(c.X - w / 2, c.Y - h / 2, w, h), w / 2, Math.Min(w / 2, h / 2)); break; }
            case "line": g.DrawRoundedRectangle(b, null, new Rect(c.X - 4.6 * k, c.Y - 1.6 * k * open, 9.2 * k, 3.2 * k * open), 1.6 * k, 1.6 * k * open); break;
            case "pixel": { double h = 7 * k * open; g.DrawRoundedRectangle(b, null, new Rect(c.X - 3.5 * k, c.Y - h / 2, 7 * k, h), 1.2, 1.2); break; }
            case "happy":
            {
                var arc = new StreamGeometry();
                using (var ctx = arc.Open()) { ctx.BeginFigure(new Point(c.X - 4.2 * k, c.Y + 2 * k), false, false); ctx.QuadraticBezierTo(new Point(c.X, c.Y - 5.5 * k * open), new Point(c.X + 4.2 * k, c.Y + 2 * k), true, true); }
                g.DrawGeometry(null, new Pen(b, 3.2 * k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
                break;
            }
            default: g.DrawEllipse(b, null, c, 3.6 * k, 5.4 * k * open); break;
        }
    }

    /// <summary>Icon of the running windows (taskbar, Alt+Tab): the current mark, with an "accent" tile drawn in the product teal.</summary>
    public static BitmapSource WindowIcon() => Render(64, Frozen(IconTeal));

    /// <summary>The mark as a bitmap of <paramref name="size"/> px (window icon, icon entries, previews).</summary>
    public static BitmapSource Render(int size, Brush accent, MarkStyle? style = null)
    {
        var visual = new DrawingVisual();
        using (var g = visual.RenderOpen()) Draw(g, size, style ?? current, accent);
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// A head as a radial profile: the distance from (32,32) at <see cref="N"/> evenly spaced angles, plus where the eyes
/// sit. Every head has one, so any two can morph by interpolating radii. Profiles are computed once and cached;
/// polar heads come straight from their curve, the others by casting rays against their outline.
/// </summary>
public sealed class Outline
{
    public const int N = 120;
    public double[] R { get; }
    public double EyeY { get; }
    Geometry? geometry;
    Outline(double[] r, double eyeY) { R = r; EyeY = eyeY; }

    static readonly Dictionary<string, Outline> cache = [];
    public static Outline For(string shape)
    {
        if (cache.TryGetValue(shape, out var o)) return o;
        return cache[shape] = Build(shape);
    }

    public static Outline Lerp(Outline a, Outline b, double t)
    {
        var r = new double[N];
        for (int i = 0; i < N; i++) r[i] = Math.Max(0, a.R[i] + (b.R[i] - a.R[i]) * t);
        return new Outline(r, a.EyeY + (b.EyeY - a.EyeY) * t);
    }

    public Geometry Geometry()
    {
        if (geometry != null) return geometry;
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            var pts = new List<Point>(N);
            for (int i = 0; i < N; i++) { double a = i * 2 * Math.PI / N; pts.Add(new Point(32 + Math.Cos(a) * R[i], 32 + Math.Sin(a) * R[i])); }
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        geo.Freeze();
        return geometry = geo;
    }

    static Outline Polar(Func<double, double> r, double eyeY = 32, double sx = 1, double sy = 1)
    {
        var rr = new double[N];
        for (int i = 0; i < N; i++)
        {
            double a = i * 2 * Math.PI / N, v = r(a);
            double x = Math.Cos(a) * v * sx, y = Math.Sin(a) * v * sy;
            rr[i] = Math.Sqrt(x * x + y * y);
        }
        return new Outline(rr, eyeY);
    }

    static Outline Build(string shape)
    {
        switch (shape)
        {
            case "circle": return Polar(_ => 19.5);
            case "blob": return Polar(a => 19.2 * (1 + .055 * Math.Cos(3 * a + .5) + .035 * Math.Sin(2 * a)));
            case "pebble": return Polar(a => 19 * (1 + .09 * Math.Cos(2 * a + .9) + .045 * Math.Cos(3 * a + 2.1)), 33, 1.08, .92);
            case "clover": return Polar(a => 16.8 + 4.4 * Math.Cos(4 * a + Math.PI / 4));
            case "flower": return Polar(a => 17.2 + 3 * Math.Cos(6 * a));
            case "star": return Polar(a => 13.6 + 9 * Math.Pow((1 + Math.Cos(4 * a)) / 2, 2.4));
            case "burst": return Polar(a => 17.6 + 2.4 * Math.Cos(9 * a));
        }
        Geometry g; double eyeY = 32;
        switch (shape)
        {
            case "squircle": g = new RectangleGeometry(new Rect(13, 13, 38, 38), 13, 13); break;
            case "diamond": g = Rounded(new Point(32, 12), new Point(52, 32), new Point(32, 52), new Point(12, 32)); break;
            case "ghost": eyeY = 31; g = System.Windows.Media.Geometry.Parse("M14.5 32 A17.5 17.5 0 0 1 49.5 32 V49 Q46.6 52.5 43.7 49 Q40.8 45.5 37.9 49 Q35 52.5 32 49 Q29 45.5 26.1 49 Q23.2 52.5 20.3 49 Q17.4 45.5 14.5 49 Z"); break;
            case "cloud":
                eyeY = 35;
                var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
                cloud.Children.Add(new EllipseGeometry(new Point(23, 37), 10.5, 10.5));
                cloud.Children.Add(new EllipseGeometry(new Point(33, 28.5), 13, 13));
                cloud.Children.Add(new EllipseGeometry(new Point(43, 36), 10, 10));
                cloud.Children.Add(new RectangleGeometry(new Rect(22, 34, 22, 13), 6, 6));
                g = cloud; break;
            case "drop": eyeY = 36; g = System.Windows.Media.Geometry.Parse("M32 11 C39 20 50.5 28.5 50.5 38.5 A18.5 18.5 0 0 1 13.5 38.5 C13.5 28.5 25 20 32 11 Z"); break;
            case "heart": eyeY = 31; g = System.Windows.Media.Geometry.Parse("M32 51 C19 43 11.5 35 12.5 25.5 C13.5 17.5 22.5 14 28.5 18.5 C30.5 20 31.5 21.5 32 23 C32.5 21.5 33.5 20 35.5 18.5 C41.5 14 50.5 17.5 51.5 25.5 C52.5 35 45 43 32 51 Z"); break;
            default: g = Rounded(new Point(32, 13), new Point(48.5, 22.5), new Point(48.5, 41.5), new Point(32, 51), new Point(15.5, 41.5), new Point(15.5, 22.5)); eyeY = 31; break;   // hex
        }
        return Cast(g, eyeY);
    }

    /// <summary>A polygon with soft corners: the fill plus a 7-unit round stroke (the classic mark's look).</summary>
    static Geometry Rounded(params Point[] pts)
    {
        var fig = new PathFigure(pts[0], pts.Skip(1).Select(p => (PathSegment)new LineSegment(p, true)), true);
        var poly = new PathGeometry([fig]);
        var widened = poly.GetWidenedPathGeometry(new Pen(Brushes.Black, 7) { LineJoin = PenLineJoin.Round });
        return System.Windows.Media.Geometry.Combine(poly, widened, GeometryCombineMode.Union, null);
    }

    /// <summary>Radii by ray casting from (32,32) against the flattened outline (farthest hit per angle).</summary>
    static Outline Cast(Geometry g, double eyeY)
    {
        var flat = g.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        var edges = new List<(Point a, Point b)>();
        foreach (var f in flat.Figures)
        {
            var pts = new List<Point> { f.StartPoint };
            foreach (var seg in f.Segments)
                if (seg is PolyLineSegment pl) pts.AddRange(pl.Points); else if (seg is LineSegment ls) pts.Add(ls.Point);
            for (int i = 0; i < pts.Count; i++) edges.Add((pts[i], pts[(i + 1) % pts.Count]));
        }
        var r = new double[N];
        var o = new Point(32, 32);
        for (int i = 0; i < N; i++)
        {
            double a = i * 2 * Math.PI / N, dx = Math.Cos(a), dy = Math.Sin(a), best = 0;
            foreach (var (p, q) in edges)
            {
                double ex = q.X - p.X, ey = q.Y - p.Y, den = dx * ey - dy * ex;
                if (Math.Abs(den) < 1e-9) continue;
                double t = ((p.X - o.X) * ey - (p.Y - o.Y) * ex) / den, u = ((p.X - o.X) * dy - (p.Y - o.Y) * dx) / den;
                if (t > 0 && u >= 0 && u <= 1 && t > best) best = t;
            }
            r[i] = best;
        }
        return new Outline(r, eyeY);
    }
}
