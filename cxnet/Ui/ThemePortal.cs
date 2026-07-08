using Rectangle = System.Drawing.Rectangle;

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;

namespace Cxnet.Ui;

/// <summary>
/// A desktop-level overlay listing the registered cxnet themes, anchored ABOVE the bottom hint
/// bar and opening upward (since the <c>t</c> hint lives at screen-bottom). Each row shows two
/// colour swatches (the theme's primary/secondary) followed by its name. Arrowing the highlight
/// <em>live-previews</em> the theme across the whole monitor; Enter/click commits (keeps the
/// previewed theme) and closes; Esc — or clicking away — reverts to the theme that was active
/// when the overlay opened. Non-modal so the graphs keep animating.
/// </summary>
internal static class ThemePortal
{
    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>Minimum overlay width so short theme names still read as a panel.</summary>
    private const int MinWidth = 26;

    /// <summary>Columns for the two swatches (`██ ██ `) plus border/padding breathing room.</summary>
    private const int RowChromeCols = 10;

    /// <summary>Border rows added to the theme count (top + bottom).</summary>
    private const int ChromeRows = 2;

    /// <summary>
    /// Opens the theme portal overlay. If one is already open it is closed instead (toggle),
    /// so pressing the key again dismisses it.
    /// </summary>
    public static void Open(ConsoleWindowSystem ws)
    {
        // Toggle: a second press closes the open portal.
        if (_open != null)
        {
            ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
            return;
        }

        // A hint click whose portal is open dismisses it on the click's first half; ignore the second
        // half so it closes rather than close-then-reopen.
        if (PortalHost.SuppressReopen(typeof(ThemePortal)))
            return;

        // Only one desktop portal open at a time: close any other before opening this one
        // (harmless no-op when none is open).
        PortalHost.CloseAll(ws);

        var names = ws.ThemeRegistryService.GetAvailableThemeNames();
        string? current = ws.ThemeStateService.CurrentTheme?.Name;

        var listBuilder = Controls.List("Themes")
            .WithName("themelist")
            .Selectable()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill);

        var fallbackSwatch = new Color(150, 150, 150);
        foreach (var name in names)
        {
            var t = ws.ThemeRegistryService.GetTheme(name);
            string primary = Palette.ToHex(t?.PrimaryColor ?? fallbackSwatch);
            string secondary = Palette.ToHex(t?.SecondaryColor ?? fallbackSwatch);
            string row = $"[{primary}]██[/][{secondary}]██[/] {name}";
            listBuilder.AddItem(row, tag: name);
        }

        var list = listBuilder.Build();

        // Pre-select the active theme.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == current)
            {
                list.SelectedIndex = i;
                break;
            }
        }

        // Size to content: height = theme rows + border; width fits `██ ██ ` + the longest name.
        int longestName = 0;
        foreach (var name in names)
            longestName = Math.Max(longestName, name.Length);
        int width = Math.Max(MinWidth, longestName + RowChromeCols);
        int height = names.Count + ChromeRows;

        // Upward anchor: near the left, bottom border flush on the last desktop row above the hint bar
        // (opens upward). Bounds is absolute screen-space; DesktopBottomRight.Y is that last row.
        var rect = PortalHost.Anchor(ws, width, height);

        // Wrap the list in a PortalContentBase that draws a rounded border and HOSTS the list.
        var content = new PortalContent(list, rect, PortalHost.Border(ws), PortalHost.Surface(ws));

        // Enter/click applies the selected theme and closes; Esc / click-away just closes (no preview,
        // no revert). ItemActivated can fire from a click (driver thread), so marshal the switch.
        list.ItemActivated += (_, item) =>
        {
            if (item?.Tag is string name)
                ws.EnqueueOnUIThread(() => ws.ThemeStateService.SwitchTheme(name));

            var portal = _open;
            _open = null;
            if (portal != null)
                ws.DesktopPortalService.RemovePortal(portal);
        };

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: () => { _open = null; PortalHost.NotifyDismissed(typeof(ThemePortal)); }));
    }
}
