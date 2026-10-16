using System.Windows;

namespace AseIsql.Desktop;

public static class WindowSizing
{
    // Generous defaults (Boberto Studio's section titles need the room) never exceed the screen's work area.
    public static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        if (window.Width > area.Width - 32) window.Width = Math.Max(window.MinWidth, area.Width - 32);
        if (window.Height > area.Height - 32) window.Height = Math.Max(window.MinHeight, area.Height - 32);
    }
}
