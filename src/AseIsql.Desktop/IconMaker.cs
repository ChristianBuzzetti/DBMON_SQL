using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AseIsql.Desktop;

/// <summary>
/// "DBMON SQL.exe --icon DIR" draws <see cref="BobertoMark"/> as vectors and writes dbmon-sql.ico (16–256, PNG
/// entries), dbmon-mark.png (256) and a preview sheet to DIR. Each size is drawn on its own pixel grid so the mark
/// stays crisp at 16 px. The assets in Assets/ are produced by this command; nothing else edits them.
/// </summary>
internal static class IconMaker
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new List<string>();
        try
        {
            var tile = new SolidColorBrush(BobertoMark.Copper);
            var entries = new List<(int Size, byte[] Data)>();
            foreach (int size in Sizes)
            {
                byte[] png = Png(BobertoMark.Render(size, tile, new MarkStyle()));   // the shipped icon is always the default mark
                entries.Add((size, png));
                if (size == 256) File.WriteAllBytes(Path.Combine(dir, "dbmon-mark.png"), png);
            }
            WriteIco(Path.Combine(dir, "dbmon-sql.ico"), entries);
            File.WriteAllBytes(Path.Combine(dir, "preview.png"), Png(Preview(tile)));
            log.Add("OK " + string.Join(" ", Sizes));
        }
        catch (Exception ex) { log.Add("FAIL " + ex); }
        File.WriteAllLines(Path.Combine(dir, "icon.log"), log);
    }

    // Preview sheet: every size on dark and on light, plus the small ones magnified 4x to judge them.
    private static BitmapSource Preview(Brush tile)
    {
        int w = 40 + Sizes.Sum(s => s + 24), h = 2 * (256 + 40) + 160;
        var visual = new DrawingVisual();
        using (var g = visual.RenderOpen())
        {
            g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27)), null, new Rect(0, 0, w, h / 2.0));
            g.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)), null, new Rect(0, h / 2.0, w, h / 2.0));
            for (int row = 0; row < 2; row++)
            {
                double x = 20, y = row * h / 2.0 + 20;
                foreach (int s in Sizes) { g.DrawImage(BobertoMark.Render(s, tile, new MarkStyle()), new Rect(x, y, s, s)); x += s + 24; }
                x = 20;
                foreach (int s in Sizes.Where(s => s <= 32))
                {
                    var group = new DrawingGroup();
                    RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
                    using (var gg = group.Open()) gg.DrawImage(BobertoMark.Render(s, tile, new MarkStyle()), new Rect(x, y + 260, s * 4, s * 4));
                    g.DrawDrawing(group);
                    x += s * 4 + 24;
                }
            }
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteIco(string path, List<(int Size, byte[] Data)> entries)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)entries.Count);
        int offset = 6 + 16 * entries.Count;
        foreach (var (size, data) in entries)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in entries) w.Write(data);
    }
}
