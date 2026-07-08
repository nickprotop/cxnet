using Rectangle = System.Drawing.Rectangle;

using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;

namespace Cxnet.Ui;

/// <summary>
/// A desktop-level overlay listing the <see cref="Backgrounds"/> presets (gradients, patterns, combined,
/// and animated effects incl. Matrix Rain). Single click / arrow APPLIES the background but keeps the
/// portal open so you can try several; double click / Enter applies and closes; Esc / click-away closes
/// without changing. Anchored above the bottom hint bar with a rounded border (via
/// <see cref="PortalContent"/>). Toggles: a second <c>g</c> closes it.
/// </summary>
internal static class BackgroundPortal
{
    private const int MinWidth = 24;
    private const int NameChromeCols = 6; // border + margin around the name
    private const int ChromeRows = 3;     // border top + bottom + the list's title row
    private const int MaxRows = 14;

    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>
    /// Opens the background picker. Toggles: a second call closes it. Only one cxnet portal is open at a
    /// time. Applies the chosen preset to <see cref="ConsoleWindowSystem.DesktopBackground"/>.
    /// </summary>
    public static void Open(ConsoleWindowSystem ws)
    {
        if (_open != null)
        {
            ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
            return;
        }

        // Suppress the re-open half of a hint click that just dismissed this portal.
        if (PortalHost.SuppressReopen(typeof(BackgroundPortal)))
            return;

        PortalHost.CloseAll(ws);

        var presets = Backgrounds.Presets;

        var listBuilder = Controls.List("Background")
            .WithName("bglist")
            .Selectable()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill);

        foreach (var (name, _) in presets)
            listBuilder.AddItem(name, tag: name);

        var list = listBuilder.Build();

        // Pre-select the current background (the last preset applied via the catalogue).
        string? current = Backgrounds.CurrentName;
        if (current != null)
        {
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i].Name == current)
                {
                    list.SelectedIndex = i;
                    break;
                }
            }
        }

        void Apply(string? name)
        {
            if (name != null)
                Backgrounds.Apply(ws, name);
        }

        int width = MinWidth;
        foreach (var (name, _) in presets)
            width = Math.Max(width, name.Length + NameChromeCols);
        int height = Math.Clamp(presets.Length + ChromeRows, ChromeRows + 1, MaxRows + ChromeRows);

        var rect = PortalHost.Anchor(ws, width, height);
        var content = new PortalContent(list, rect, PortalHost.Border(ws), PortalHost.Surface(ws));

        // Single click / arrow (SelectedItemChanged) applies the preset but keeps the portal open so you
        // can try several. Both events can fire from the driver (mouse) thread, so marshal to the UI thread.
        list.SelectedItemChanged += (_, item) =>
        {
            if (item?.Tag is string name)
                ws.EnqueueOnUIThread(() => Apply(name));
        };

        // Double click / Enter applies and closes.
        list.ItemActivated += (_, item) =>
        {
            var portal = _open;
            _open = null;
            ws.EnqueueOnUIThread(() =>
            {
                if (item?.Tag is string name)
                    Apply(name);
                if (portal != null)
                    ws.DesktopPortalService.RemovePortal(portal);
            });
        };

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: () => { _open = null; PortalHost.NotifyDismissed(typeof(BackgroundPortal)); }));
    }
}
