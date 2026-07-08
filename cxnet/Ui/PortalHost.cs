using Rectangle = System.Drawing.Rectangle;
using SharpConsoleUI;

namespace Cxnet.Ui;

/// <summary>Shared helpers for cxnet's bordered desktop portals (theme, connections, interface):
/// consistent upward anchoring above the hint bar and themed surface/border colours, plus a helper to
/// keep only one portal open at a time.</summary>
internal static class PortalHost
{
    private const int AnchorX = 2; // left inset under the hint region

    // A single physical click on a bottom-bar hint arrives as two mouse dispatches: the first
    // (Button1Pressed, a click OUTSIDE the open portal) dismisses the portal, the second (Button1Clicked)
    // then reaches the hint and would RE-open it. Record when a portal is dismissed so an Open() from
    // that same click's second half is suppressed — clicking a hint whose portal is open just closes it.
    private const double ReopenSuppressMs = 250;
    private static System.DateTime _lastDismiss = System.DateTime.MinValue;

    /// <summary>Records a portal dismissal (each portal calls this from its OnDismiss).</summary>
    public static void NotifyDismissed() => _lastDismiss = System.DateTime.UtcNow;

    /// <summary>True if a portal was dismissed within the last <see cref="ReopenSuppressMs"/> ms — i.e.
    /// an Open() now would be the re-open half of the click that just dismissed it, and should be skipped.</summary>
    public static bool SuppressReopen() =>
        (System.DateTime.UtcNow - _lastDismiss).TotalMilliseconds < ReopenSuppressMs;

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
