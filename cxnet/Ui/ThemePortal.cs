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
public static class ThemePortal
{
    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>Minimum overlay width so short theme names still read as a panel.</summary>
    private const int MinWidth = 26;

    /// <summary>Columns for the two swatches (`██ ██ `) plus border/padding breathing room.</summary>
    private const int RowChromeCols = 10;

    /// <summary>Border rows added to the theme count (top + bottom).</summary>
    private const int ChromeRows = 2;

    /// <summary>Left inset — sits under the `t` hint region at screen-left.</summary>
    private const int AnchorX = 2;

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

        var names = ws.ThemeRegistryService.GetAvailableThemeNames();

        // Capture the theme active on open so Esc / click-away can revert after live previewing.
        string? original = ws.ThemeStateService.CurrentTheme?.Name;


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

        // Size to content: height = theme rows + border; width fits `██ ██ ` + the longest name.
        int longestName = 0;
        foreach (var name in names)
            longestName = Math.Max(longestName, name.Length);
        int width = Math.Max(MinWidth, longestName + RowChromeCols);
        int height = names.Count + ChromeRows;

        // Upward anchor: near the left, sitting ABOVE the hint bar (opens upward). Bounds is absolute
        // screen-space. The desktop render region clips at the row just above the bottom bar, so the
        // box's bottom border must land on the second-to-last desktop row — anchor one row higher than
        // the bar so the full box (bottom border included) stays inside the clip. (DesktopBottomRight.Y
        // is unusable here: it also subtracts the top-status height and would push the box up further.)
        int screenHeight = ws.ConsoleDriver.ScreenSize.Height;
        int bottomBarHeight = ws.BottomPanel?.Height ?? 0;
        int x = AnchorX;
        int y = Math.Max(0, screenHeight - bottomBarHeight - height - 1);
        var rect = new Rectangle(x, y, width, height);

        // Wrap the list in a PortalContentBase that draws a rounded border and HOSTS the list — the
        // framework's supported way to give a desktop portal bordered, laid-out content (a plain
        // container inside a portal collapses its child; PortalContentBase measures the child tight).
        var surface = Palette.Current(ws).Surface;
        var content = new ThemePortalContent(list, rect,
            border: Palette.Current(ws).Accent,
            background: new Color(surface.R, surface.G, surface.B, 205));

        // Highlight the currently-active theme FIRST, then subscribe — so setting the initial
        // selection below does not fire a spurious live preview.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == original)
            {
                list.SelectedIndex = i;
                break;
            }
        }

        // Live-preview: moving the highlight (arrow keys) recolours the whole monitor.
        list.SelectedItemChanged += (_, item) =>
        {
            if (item?.Tag is string n)
                ws.ThemeStateService.SwitchTheme(n);
        };

        // committed distinguishes Enter/click (keep) from Esc/click-away (revert). It MUST be set
        // true BEFORE RemovePortal in ItemActivated so the OnDismiss callback (fired by the removal)
        // sees the committed state and skips the revert.
        bool committed = false;

        // Reverts to the theme active on open unless the user committed. Fires on Esc (the framework
        // auto-removes the top portal) AND on click-outside — both routes run OnDismiss.
        Action onDismiss = () =>
        {
            if (!committed && original != null)
                ws.ThemeStateService.SwitchTheme(original);
            _open = null;
        };

        // Enter/click commits: the previewed theme is already applied, so just close.
        list.ItemActivated += (_, _) =>
        {
            committed = true;
            if (_open != null)
                ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
        };

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: onDismiss));
    }
}
