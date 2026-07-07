using SharpConsoleUI;

namespace Cxnet.Ui;

/// <summary>cxnet's semantic colours, resolved from the active theme.</summary>
public readonly record struct Palette(Color Download, Color Upload, Color Surface, Color Muted, Color Accent)
{
    public static Palette Current(ConsoleWindowSystem ws)
    {
        var t = ws.ThemeStateService.CurrentTheme;
        Color download = t?.PrimaryColor ?? new Color(96, 165, 250);
        Color upload = t?.SecondaryColor ?? new Color(251, 146, 60);
        Color baseBg = t?.DesktopBackgroundColor ?? new Color(10, 16, 28);
        Color surface = new Color(baseBg.R, baseBg.G, baseBg.B, 140); // translucent glass
        Color muted = t?.InactiveBorderForegroundColor ?? new Color(70, 90, 120);
        return new Palette(download, upload, surface, muted, download);
    }
}
