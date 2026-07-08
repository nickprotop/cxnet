using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using Cxnet.Sampling;

namespace Cxnet.Ui;

/// <summary>
/// A desktop-level overlay letting the user CHOOSE the monitored network interface (replacing the old
/// blind <c>i</c>-cycle). Lists <see cref="NetworkSampler.AvailableInterfaces"/>, highlights the current
/// one, and on Enter/click switches to the chosen interface. Anchored above the bottom hint bar with a
/// rounded border (via <see cref="PortalContent"/>). Toggles: a second <c>i</c> closes it; Esc /
/// click-away closes without changing.
/// </summary>
internal static class InterfacePortal
{
    private const int MinWidth = 20;
    private const int NameChromeCols = 6; // border + margin around the name
    private const int ChromeRows = 2;     // border top + bottom

    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>
    /// Opens the interface picker. Toggles: a second call closes it. Only one cxnet portal is open at a
    /// time. On selection, calls <see cref="NetworkSampler.SelectInterface"/> then <paramref name="onSelected"/>
    /// (used to reset peaks / refresh). <paramref name="shortcutHandler"/> keeps global shortcuts working
    /// while the portal is open.
    /// </summary>
    public static void Open(ConsoleWindowSystem ws, NetworkSampler sampler,
        Func<ConsoleKeyInfo, bool>? shortcutHandler, Action onSelected)
    {
        if (_open != null)
        {
            ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
            return;
        }

        PortalHost.CloseAll(ws);

        var names = NetworkSampler.AvailableInterfaces();
        string current = sampler.InterfaceName;

        var listBuilder = Controls.List("Interface")
            .WithName("ifacelist")
            .Selectable()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill);

        foreach (var name in names)
            listBuilder.AddItem(name, tag: name);

        var list = listBuilder.Build();

        // Pre-select the active interface.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == current)
            {
                list.SelectedIndex = i;
                break;
            }
        }

        // Enter/click selects: switch the sampler's interface, run the caller's refresh, close.
        list.ItemActivated += (_, item) =>
        {
            if (item?.Tag is string name)
            {
                sampler.SelectInterface(name);
                onSelected();
            }
            if (_open != null)
                ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
        };

        int longestName = 0;
        foreach (var name in names)
            longestName = Math.Max(longestName, name.Length);
        int width = Math.Max(MinWidth, longestName + NameChromeCols);
        int height = Math.Max(1, names.Count) + ChromeRows;

        var rect = PortalHost.Anchor(ws, width, height);
        var content = new PortalContent(list, rect, PortalHost.Border(ws), PortalHost.Surface(ws), shortcutHandler);

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: () => _open = null));
    }
}
