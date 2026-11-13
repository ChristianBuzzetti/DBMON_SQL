using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AseIsql.Desktop;

/// <summary>
/// The DBMON SQL mark: Boberto's head — a black circle with two eyes — on a rounded tile. The same drawing is the
/// topbar logo and the app icon (<see cref="IconMaker"/> writes it at every size); the copper tile does not follow
/// the palette, so the mark reads the same in every theme and in the taskbar. It is a sibling of the BuzzPrint mark, told apart by the round head and the tile
/// colour; the raster Boberto (Assets/dbmon-sql.png) stays the mascot in the dock and in Studio.
/// </summary>
public sealed class BobertoMark : FrameworkElement
{
    /// <summary>Copper, the Pergamino accent: the tile of the shipped icon.</summary>
    public static readonly Color Copper = Color.FromRgb(0xCB, 0x6A, 0x3E);
    private static readonly Color HeadColor = Color.FromRgb(0x0E, 0x10, 0x15);
    private static readonly Color EyeColor = Color.FromRgb(0xF4, 0xF4, 0xF5);

    public static readonly DependencyProperty TileProperty = DependencyProperty.Register(
        nameof(Tile), typeof(Brush), typeof(BobertoMark),
        new FrameworkPropertyMetadata(new SolidColorBrush(Copper), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Tile brush. The logo keeps the copper tile on every palette, as the app icon does; previews pass their own.</summary>
    public Brush Tile { get => (Brush)GetValue(TileProperty); set => SetValue(TileProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        Draw(dc, s, Tile);
        dc.Pop();
    }

    /// <summary>Draws the mark in a square of <paramref name="size"/> px, in a 64-unit space.</summary>
    public static void Draw(DrawingContext g, double size, Brush tile)
    {
        bool tiny = size <= 40;                     // at icon sizes the eyes need more room to survive
        g.PushTransform(new ScaleTransform(size / 64, size / 64));
        g.DrawRoundedRectangle(tile, null, new Rect(2, 2, 60, 60), 14, 14);
        g.DrawEllipse(new SolidColorBrush(HeadColor), null, new Point(32, 32), 19.5, 19.5);
        var eye = new SolidColorBrush(EyeColor);
        double gap = tiny ? 7 : 6.5, r = tiny ? 5.5 : 4.6;
        g.DrawEllipse(eye, null, new Point(32 - gap, 32), r, r);
        g.DrawEllipse(eye, null, new Point(32 + gap, 32), r, r);
        g.Pop();
    }

    /// <summary>The mark as a bitmap of <paramref name="size"/> px (icon entries, previews).</summary>
    public static BitmapSource Render(int size, Brush tile)
    {
        var visual = new DrawingVisual();
        using (var g = visual.RenderOpen()) Draw(g, size, tile);
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
