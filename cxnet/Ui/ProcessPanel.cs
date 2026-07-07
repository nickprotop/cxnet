using System.Net.NetworkInformation;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cxnet.Ui;

/// <summary>
/// A translucent floating overlay listing active TCP connections grouped by remote endpoint,
/// sorted by connection count. Background alpha &lt; 255 lets the waveforms show through — the
/// alpha-compositing showcase. Best-effort and never throws: an empty or errored data source
/// simply renders an empty-state line. Non-modal; Esc closes.
/// </summary>
internal static class ProcessPanel
{
    private const string OverlayName = "processes";
    private const int MaxRows = 14;

    // Same deep-navy translucent surface as the theme picker.
    private static readonly Color OverlayBackground = new Color(12, 18, 30, 190);

    /// <summary>
    /// Opens the connections overlay. Toggles: if one is already open it is closed instead.
    /// </summary>
    public static void Show(ConsoleWindowSystem ws)
    {
        var existing = ws.WindowStateService.FindWindowByName(OverlayName);
        if (existing is not null)
        {
            ws.CloseWindow(existing);
            return;
        }

        var listBuilder = Controls.List("Active connections (by remote)")
            .WithName("connlist")
            .Selectable(false)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithMargin(1, 0, 1, 0);

        var rows = GetConnectionRows();
        if (rows.Count == 0)
        {
            listBuilder.AddItem("[dim]No active connections[/]");
        }
        else
        {
            listBuilder.AddItem("[dim]remote address:port          state         count[/]");
            foreach (var row in rows)
                listBuilder.AddItem(row);
        }

        var list = listBuilder.Build();

        Window? window = null;
        window = new WindowBuilder(ws)
            .WithTitle("Connections")
            .WithName(OverlayName)
            .WithSize(60, 18)
            .Centered()
            .WithBackgroundColor(OverlayBackground)
            .AddControl(list)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    if (window is not null)
                        ws.CloseWindow(window);
                    e.Handled = true;
                }
            })
            .Build();

        ws.AddWindow(window);
        ws.SetActiveWindow(window);
    }

    /// <summary>
    /// Best-effort list of formatted connection rows, grouped by remote endpoint and sorted by
    /// count (descending). Returns an empty list on any error — the caller shows an empty state.
    /// </summary>
    private static List<string> GetConnectionRows()
    {
        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();

            // Group by "remote-endpoint | state" so the same peer in different states is
            // distinguishable, then sort by count descending.
            var grouped = connections
                .GroupBy(c => (
                    Remote: c.RemoteEndPoint.ToString(),
                    State: c.State.ToString()))
                .Select(g => (g.Key.Remote, g.Key.State, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Remote, StringComparer.Ordinal)
                .Take(MaxRows)
                .ToList();

            var result = new List<string>(grouped.Count);
            foreach (var (remote, state, count) in grouped)
            {
                string remoteCol = Truncate(remote, 28).PadRight(28);
                string stateCol = Truncate(state, 12).PadRight(12);
                result.Add($"[cyan]{remoteCol}[/] [dim]{stateCol}[/] {count,4}");
            }
            return result;
        }
        catch
        {
            // GetActiveTcpConnections can throw NotImplemented/Network errors on some platforms.
            return new List<string>();
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
