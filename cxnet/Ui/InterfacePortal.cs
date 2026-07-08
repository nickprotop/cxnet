using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;
using Cxnet.Sampling;

namespace Cxnet.Ui;

/// <summary>
/// A desktop-level overlay letting the user CHOOSE the monitored network interface (replacing the old
/// blind <c>i</c>-cycle). Renders a <see cref="TableControl"/> of the available interfaces with
/// cross-platform detail columns (Name / Type / IPv4 / Speed), highlights the current one, and on
/// Enter / double-click switches to the chosen interface. Anchored above the bottom hint bar with a
/// rounded border (via <see cref="PortalContent"/>). Toggles: a second <c>i</c> closes it; Esc /
/// click-away closes without changing.
/// </summary>
internal static class InterfacePortal
{
    private const int NameWidth = 12;
    private const int TypeWidth = 10;
    private const int IPv4Width = 16;
    private const int SpeedWidth = 8;
    private const int ChromeRows = 3; // border top + bottom + header row
    private const int MaxRows = 12;

    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>
    /// Opens the interface picker. Toggles: a second call closes it. Only one cxnet portal is open at a
    /// time. On selection, calls <see cref="NetworkSampler.SelectInterface"/> then <paramref name="onSelected"/>
    /// (used to reset peaks / refresh).
    /// </summary>
    public static void Open(ConsoleWindowSystem ws, NetworkSampler sampler, Action onSelected)
    {
        if (_open != null)
        {
            ws.DesktopPortalService.RemovePortal(_open);
            _open = null;
            return;
        }

        // Suppress the re-open half of a hint click that just dismissed this portal.
        if (PortalHost.SuppressReopen(typeof(InterfacePortal)))
            return;

        PortalHost.CloseAll(ws);

        string current = sampler.InterfaceName;

        // Available interfaces (virtual/container filtered); always include the currently monitored one
        // even if it would be filtered, so an explicitly chosen interface is still selectable.
        var details = NetworkSampler.AvailableInterfaceDetails().ToList();
        if (!string.IsNullOrEmpty(current) && !details.Any(d => d.Name == current))
            details.Insert(0, new NetworkSampler.InterfaceInfo(current, "", "", ""));

        // Row index → interface name, so RowActivated(index) resolves to the chosen interface.
        var names = details.Select(d => d.Name).ToList();

        var tableBuilder = Controls.Table()
            .WithName("ifacetable")
            .Interactive()
            .NoBorder() // the PortalContent already draws the rounded border — no inner table border
            .WithColumnSeparator('│', padded: true) // vertical column separators only (no row separators)
            .StretchHorizontal() // fill the portal width
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .AddColumn("Interface", TextJustification.Left, NameWidth)
            .AddColumn("Type", TextJustification.Left, TypeWidth)
            .AddColumn("IPv4", TextJustification.Left, IPv4Width)
            .AddColumn("Speed", TextJustification.Right, SpeedWidth);

        foreach (var d in details)
            tableBuilder.AddRow(d.Name, d.Type, d.IPv4, d.Speed);

        var table = tableBuilder.Build();
        table.TruncationFade = true; // fade clipped cell text instead of a hard cut

        // Pre-select the active interface.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == current)
            {
                table.SelectedRowIndex = i;
                break;
            }
        }

        // Enter / double-click applies the chosen interface then closes. RowActivated can fire from a
        // mouse click on the driver thread; RemovePortal does structural teardown that must run on the
        // UI thread, so marshal the whole handler.
        table.RowActivated += (_, index) =>
        {
            string? name = index >= 0 && index < names.Count ? names[index] : null;
            var portal = _open;
            _open = null;
            ws.EnqueueOnUIThread(() =>
            {
                if (name != null)
                {
                    sampler.SelectInterface(name);
                    onSelected();
                }
                if (portal != null)
                    ws.DesktopPortalService.RemovePortal(portal);
            });
        };

        // 4 columns with 3 padded ' │ ' separators (3 cols each).
        int contentCols = NameWidth + TypeWidth + IPv4Width + SpeedWidth + 3 * 3;
        int width = contentCols + 4; // border + slack
        // Portal height = data rows + chrome (border top+bottom + the table header row), capped.
        int height = Math.Clamp(details.Count + ChromeRows, ChromeRows + 1, MaxRows + ChromeRows);

        var rect = PortalHost.Anchor(ws, width, height);
        var content = new PortalContent(table, rect, PortalHost.Border(ws), PortalHost.Surface(ws));

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: () => { _open = null; PortalHost.NotifyDismissed(typeof(InterfacePortal)); }));
    }
}
