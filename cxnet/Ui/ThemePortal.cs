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
    public static void Open(ConsoleWindowSystem ws, Func<ConsoleKeyInfo, bool>? shortcutHandler = null)
    {
        // Toggle: a second press closes the open portal.
        if (_open != null)
        {
            ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
            return;
        }

        // Only one desktop portal open at a time: close any other before opening this one
        // (harmless no-op when none is open).
        PortalHost.CloseAll(ws);

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

        // Upward anchor: near the left, bottom border flush on the last desktop row above the hint bar
        // (opens upward). Bounds is absolute screen-space; DesktopBottomRight.Y is that last row.
        var rect = PortalHost.Anchor(ws, width, height);

        // Wrap the list in a PortalContentBase that draws a rounded border and HOSTS the list — the
        // framework's supported way to give a desktop portal bordered, laid-out content (a plain
        // container inside a portal collapses its child; PortalContentBase measures the child tight).
        var content = new PortalContent(list, rect, PortalHost.Border(ws), PortalHost.Surface(ws), shortcutHandler);

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

        // Reverts to the theme active on open unless the user committed. Fires on Esc AND click-outside;
        // click-outside dispatches on the driver input thread, so marshal the (structural) SwitchTheme
        // onto the UI thread. Clearing _open is a plain field write — safe on any thread, kept synchronous
        // so the toggle check never sees a stale reference.
        Action onDismiss = () =>
        {
            _open = null;
            if (!committed && original != null)
                ws.EnqueueOnUIThread(() => ws.ThemeStateService.SwitchTheme(original));
        };

        // Enter/click commits: the previewed theme is already applied, so just close. ItemActivated can
        // fire from a click (driver thread); RemovePortal → OnDismiss runs the revert-skip, so set
        // committed first, then remove. RemovePortal is thread-safe (removes from the portal list).
        list.ItemActivated += (_, _) =>
        {
            committed = true;
            var portal = _open;
            _open = null;
            if (portal != null)
                ws.DesktopPortalService.RemovePortal(portal);
        };

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: onDismiss));
    }
}
