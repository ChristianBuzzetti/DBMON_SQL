using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AseIsql.Desktop;

// Frames for the README tour, composed only from this process's own windows (main window, dialogs, menus and
// tooltips) with PrintWindow, plus a drawn pointer. Other applications can never appear in the recording, even when
// they cover the showcase. Frames are paced on a clock: a slow capture repeats the previous frame instead of
// shortening the timeline.
public sealed class ShowcaseRecorder : IDisposable
{
    private readonly IntPtr main;
    private readonly string folder;
    private readonly int fps;
    private readonly Thread worker;
    private volatile bool stopping;
    private readonly object gate = new();
    private Bitmap? last;
    public int Frames { get; private set; }

    public ShowcaseRecorder(IntPtr mainWindow, string directory, int framesPerSecond = 15)
    {
        main = mainWindow; folder = directory; fps = framesPerSecond; Directory.CreateDirectory(folder);
        worker = new Thread(Run) { IsBackground = true, Name = "Showcase recorder" }; worker.Start();
    }

    // Saves the most recent composed frame as a named still.
    public void Still(string path) { lock (gate) last?.Save(path, ImageFormat.Png); }

    private void Run()
    {
        var clock = Stopwatch.StartNew(); int written = 0; string? previous = null;
        while (!stopping)
        {
            using var frame = Compose();
            if (frame != null)
            {
                lock (gate) { last?.Dispose(); last = (Bitmap)frame.Clone(); }
                int due = (int)(clock.Elapsed.TotalSeconds * fps);
                // Fill any frames the capture fell behind on with the previous image, then write this one.
                while (previous != null && written < due) { File.Copy(previous, FramePath(written), true); written++; }
                string path = FramePath(written); frame.Save(path, ImageFormat.Png); previous = path; written++;
                Frames = written;
            }
            int wait = (int)((written / (double)fps - clock.Elapsed.TotalSeconds) * 1000);
            if (wait > 0) Thread.Sleep(wait);
        }
    }
    private string FramePath(int index) => Path.Combine(folder, "frame_" + index.ToString("D5") + ".png");

    private Bitmap? Compose()
    {
        if (DwmGetWindowAttribute(main, 9, out RECT canvas, Marshal.SizeOf<RECT>()) != 0) return null;
        int width = (canvas.Right - canvas.Left) & ~1, height = (canvas.Bottom - canvas.Top) & ~1;
        if (width <= 0 || height <= 0) return null;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        GetWindowThreadProcessId(main, out uint pid);
        // Top-level windows of this process, bottom to top, so dialogs, menus and tooltips land above the main window.
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) => { GetWindowThreadProcessId(hwnd, out uint owner); if (owner == pid && IsWindowVisible(hwnd) && !IsIconic(hwnd)) windows.Add(hwnd); return true; }, IntPtr.Zero);
        windows.Reverse();
        foreach (var hwnd in windows)
        {
            if (!GetWindowRect(hwnd, out RECT box) || box.Right <= box.Left || box.Bottom <= box.Top) continue;
            int w = box.Right - box.Left, h = box.Bottom - box.Top;
            if (w > 8000 || h > 8000) continue;
            using var shot = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(shot)) { IntPtr dc = g.GetHdc(); try { PrintWindow(hwnd, dc, 2); } finally { g.ReleaseHdc(dc); } }
            // Window rectangles include the invisible resize border: crop each to its visible frame.
            RECT visible = DwmGetWindowAttribute(hwnd, 9, out RECT frame, Marshal.SizeOf<RECT>()) == 0 ? frame : box;
            var source = new Rectangle(visible.Left - box.Left, visible.Top - box.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
            graphics.DrawImage(shot, new Rectangle(visible.Left - canvas.Left, visible.Top - canvas.Top, source.Width, source.Height), source, GraphicsUnit.Pixel);
        }
        if (GetCursorPos(out POINT pointer)) DrawPointer(graphics, pointer.X - canvas.Left, pointer.Y - canvas.Top);
        return bitmap;
    }

    // A standard arrow pointer: white fill, dark outline.
    private static void DrawPointer(Graphics graphics, int x, int y)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var points = new PointF[] { new(0, 0), new(0, 17), new(4.2f, 13.2f), new(7.2f, 19.6f), new(9.8f, 18.4f), new(6.9f, 12.2f), new(12.4f, 12.2f) }
            .Select(p => new PointF(x + p.X * 1.15f, y + p.Y * 1.15f)).ToArray();
        using var fill = new SolidBrush(Color.White); using var outline = new Pen(Color.FromArgb(20, 20, 20), 1.4f) { LineJoin = LineJoin.Round };
        graphics.FillPolygon(fill, points); graphics.DrawPolygon(outline, points);
    }

    public void Dispose() { stopping = true; worker.Join(5000); lock (gate) { last?.Dispose(); last = null; } }

    private delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
}
