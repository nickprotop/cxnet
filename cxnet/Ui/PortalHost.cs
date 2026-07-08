using Rectangle = System.Drawing.Rectangle;
using SharpConsoleUI;

namespace Cxnet.Ui;

/// <summary>Shared helpers for cxnet's bordered desktop portals (theme, connections, interface):
/// consistent upward anchoring above the hint bar and themed surface/border colours, plus a helper to
/// keep only one portal open at a time.</summary>
internal static class PortalHost
{
    private const int AnchorX = 2; // left inset under the hint region

    /// <summary>Upward anchor: a <paramref name="width"/>×<paramref name="height"/> box whose bottom
    /// border sits on the last desktop row directly above the hint bar.</summary>
    public static Rectangle Anchor(ConsoleWindowSystem ws, int width, int height)
    {
        int y = System.Math.Max(0, ws.DesktopBottomRight.Y - height + 1);
        return new Rectangle(AnchorX, y, width, height);
    }

    /// <summary>The themed, semi-opaque panel surface (a bit more opaque than the main window).</summary>
    public static Color Surface(ConsoleWindowSystem ws)
    {
        var s = Palette.Current(ws).Surface;
        return new Color(s.R, s.G, s.B, 205);
    }

    /// <summary>The themed border colour for portals.</summary>
    public static Color Border(ConsoleWindowSystem ws) => Palette.Current(ws).Accent;

    /// <summary>Closes any open desktop portal — call before opening a new one so only one shows.</summary>
    public static void CloseAll(ConsoleWindowSystem ws) => ws.DesktopPortalService.DismissAllPortals();
}
