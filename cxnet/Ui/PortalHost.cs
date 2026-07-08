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
    // then reaches the hint and would RE-open it. Record WHICH portal was just dismissed so an Open() of
    // that SAME portal from the click's second half is suppressed (clicking an open portal's own hint
    // just closes it) — while clicking a DIFFERENT hint still closes the old and opens the new.
    private const double ReopenSuppressMs = 250;
    private static object? _lastDismissedKey;
    private static System.DateTime _lastDismiss = System.DateTime.MinValue;

    /// <summary>Records a portal dismissal, tagged by the portal's identity key (each portal passes a
    /// stable key, e.g. its own <c>typeof</c>).</summary>
    public static void NotifyDismissed(object key)
    {
        _lastDismissedKey = key;
        _lastDismiss = System.DateTime.UtcNow;
    }

    /// <summary>True if the portal identified by <paramref name="key"/> was just dismissed (within
    /// <see cref="ReopenSuppressMs"/>) — i.e. this Open() is the re-open half of the click that closed
    /// it, and should be skipped. Opening a DIFFERENT portal is never suppressed.</summary>
    public static bool SuppressReopen(object key) =>
        ReferenceEquals(_lastDismissedKey, key) &&
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
