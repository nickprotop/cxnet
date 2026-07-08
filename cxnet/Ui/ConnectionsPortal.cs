using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Layout;

namespace Cxnet.Ui;

/// <summary>
/// A desktop-level overlay listing active TCP connections in a <see cref="TableControl"/>, grouped by
/// remote endpoint and sorted by connection count. Anchored above the bottom hint bar and opening
/// upward, with a rounded border (via <see cref="PortalContent"/>). Best-effort and never throws: an
/// empty or errored data source renders an empty-state row. On Linux the rows are enriched with the
/// owning process name and PID (parsed from <c>ss -tnp</c>); enrichment failure is silently ignored.
/// Display-only (arrow to scroll; Esc / a second <c>n</c> / click-away closes).
/// </summary>
internal static class ConnectionsPortal
{
    private const int MaxRows = 14;
    private const int SsTimeoutMs = 1500; // best-effort `ss -tnp` enrichment timeout

    // Column widths (display columns) for the table.
    private const int RemoteWidth = 24;
    private const int StateWidth = 12;
    private const int CountWidth = 5;
    private const int ProcessWidth = 14;
    private const int PidWidth = 7;

    /// <summary>The single open portal, if any. Used to toggle (a second press closes it).</summary>
    private static DesktopPortal? _open;

    /// <summary>A single grouped connection row, optionally enriched with owning process/PID.</summary>
    private readonly record struct ConnectionInfo(string Remote, string State, int Count, string? Process, int? Pid);

    /// <summary>
    /// Opens the connections portal. Toggles: a second call closes it. Only one cxnet portal is open at
    /// a time (opening this closes any other). the framework routes unconsumed portal keys to global
    /// shortcuts working while the portal is open (same key closes it, another switches portals).
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
        if (PortalHost.SuppressReopen(typeof(ConnectionsPortal)))
            return;

        PortalHost.CloseAll(ws);

        var connections = GetConnections();
        bool showProcess = connections.Any(c => c.Process is not null);

        var tableBuilder = Controls.Table()
            .WithName("conntable")
            .NoBorder() // PortalContent draws the rounded border — no inner table border
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .AddColumn("Remote", TextJustification.Left, RemoteWidth)
            .AddColumn("State", TextJustification.Left, StateWidth)
            .AddColumn("Count", TextJustification.Right, CountWidth);
        if (showProcess)
        {
            tableBuilder
                .AddColumn("Process", TextJustification.Left, ProcessWidth)
                .AddColumn("PID", TextJustification.Right, PidWidth);
        }

        if (connections.Count == 0)
        {
            tableBuilder.AddRow(showProcess
                ? new[] { "No active connections", "", "", "", "" }
                : new[] { "No active connections", "", "" });
        }
        else
        {
            foreach (var c in connections)
            {
                tableBuilder.AddRow(showProcess
                    ? new[] { c.Remote, c.State, c.Count.ToString(), c.Process ?? "", c.Pid?.ToString() ?? "" }
                    : new[] { c.Remote, c.State, c.Count.ToString() });
            }
        }

        var table = tableBuilder.Build();

        // Size to the visible columns (each + a 1-col gap) plus border; height = rows + header + border,
        // capped so the table scrolls rather than overflowing.
        int contentCols = showProcess
            ? RemoteWidth + 1 + StateWidth + 1 + CountWidth + 1 + ProcessWidth + 1 + PidWidth
            : RemoteWidth + 1 + StateWidth + 1 + CountWidth;
        int width = contentCols + 4; // border + slack
        int rowCount = (connections.Count == 0 ? 1 : connections.Count) + 1; // + header
        int height = Math.Clamp(rowCount + 2, 6, MaxRows + 3);

        var rect = PortalHost.Anchor(ws, width, height);
        var content = new PortalContent(table, rect, PortalHost.Border(ws), PortalHost.Surface(ws));

        _open = ws.DesktopPortalService.CreatePortal(new DesktopPortalOptions(
            Content: content,
            Bounds: rect,
            DismissOnClickOutside: true,
            OnDismiss: () => { _open = null; PortalHost.NotifyDismissed(typeof(ConnectionsPortal)); }));
    }

    /// <summary>
    /// Best-effort list of connection rows, grouped by remote endpoint and sorted by count
    /// (descending). Returns an empty list on any error. On Linux the rows are additionally enriched
    /// with owning process/PID via <c>ss -tnp</c>; enrichment never affects the base data.
    /// </summary>
    private static List<ConnectionInfo> GetConnections()
    {
        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();

            var rows = connections
                .GroupBy(c => (Remote: c.RemoteEndPoint.ToString(), State: c.State.ToString()))
                .Select(g => new ConnectionInfo(g.Key.Remote, g.Key.State, g.Count(), null, null))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Remote, StringComparer.Ordinal)
                .Take(MaxRows)
                .ToList();

            var procMap = TryGetProcessMap();
            if (procMap.Count > 0)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (procMap.TryGetValue(rows[i].Remote, out var info))
                        rows[i] = rows[i] with { Process = info.name, Pid = info.pid };
                }
            }

            return rows;
        }
        catch
        {
            return new List<ConnectionInfo>();
        }
    }

    // Matches the users:(("name",pid=N,...)) fragment of an `ss -tnp` line.
    private static readonly Regex SsProcessRegex =
        new(@"\(""(?<name>[^""]+)"",pid=(?<pid>\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Best-effort map from a remote <c>address:port</c> endpoint to its owning process name and PID,
    /// parsed from <c>ss -tnp</c>. Linux-only; returns an empty map on ANY failure. NEVER throws.
    /// </summary>
    private static Dictionary<string, (string name, int pid)> TryGetProcessMap()
    {
        var map = new Dictionary<string, (string name, int pid)>(StringComparer.Ordinal);
        if (!OperatingSystem.IsLinux())
            return map;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ss",
                Arguments = "-tnp",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return map;

            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(SsTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return map;
            }

            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                // Whitespace columns: State Recv-Q Send-Q Local Peer users:(...) — peer is the 5th.
                var cols = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 5 || cols[0] == "State")
                    continue;

                string peer = cols[4];
                var m = SsProcessRegex.Match(line);
                if (m.Success && int.TryParse(m.Groups["pid"].Value, out int pid))
                    map[peer] = (m.Groups["name"].Value, pid);
            }
        }
        catch
        {
            return new Dictionary<string, (string name, int pid)>(StringComparer.Ordinal);
        }

        return map;
    }
}
