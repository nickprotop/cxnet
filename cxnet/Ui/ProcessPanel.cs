using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cxnet.Ui;

/// <summary>
/// A translucent floating overlay listing active TCP connections grouped by remote endpoint,
/// sorted by connection count. Background alpha &lt; 255 lets the waveforms show through — the
/// alpha-compositing showcase. Best-effort and never throws: an empty or errored data source
/// simply renders an empty-state line. On Linux the rows are best-effort enriched with the owning
/// process name and PID (parsed from <c>ss -tnp</c>); enrichment failure is silently ignored.
/// Non-modal; Esc closes.
/// </summary>
internal static class ProcessPanel
{
    private const string OverlayName = "processes";
    private const int MaxRows = 14;

    // Column widths (display columns) for the aligned table.
    private const int RemoteWidth = 28;
    private const int StateWidth = 12;
    private const int CountWidth = 5;
    private const int ProcessWidth = 14;
    private const int PidWidth = 7;

    // Milliseconds to wait for the best-effort `ss -tnp` enrichment before giving up.
    private const int SsTimeoutMs = 1500;

    /// <summary>A single grouped connection row, optionally enriched with owning process/PID.</summary>
    private readonly record struct ConnectionInfo(string Remote, string State, int Count, string? Process, int? Pid);

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

        var connections = GetConnections();
        bool showProcess = connections.Any(c => c.Process is not null);

        var listBuilder = Controls.List("Active connections (by remote)")
            .WithName("connlist")
            .Selectable(false)
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithMargin(1, 0, 1, 0);

        if (connections.Count == 0)
        {
            listBuilder.AddItem("[dim]No active connections[/]");
        }
        else
        {
            listBuilder.AddItem(BuildHeaderRow(showProcess));
            foreach (var c in connections)
                listBuilder.AddItem(BuildRow(c, showProcess));
        }

        var list = listBuilder.Build();

        // Translucent surface from the active theme — alpha < 255 lets the waveforms bleed through.
        var surface = Palette.Current(ws).Surface;
        var background = new Color(surface.R, surface.G, surface.B, 205);

        // Size to content: width depends on whether the process columns are shown; height tracks
        // the row count (+ chrome) but is capped so the list scrolls rather than overflowing.
        int width = showProcess ? 66 : 46;
        int rowCount = connections.Count == 0 ? 1 : connections.Count + 1; // + header
        int height = Math.Clamp(rowCount + 4, 8, MaxRows + 6);

        Window? window = null;
        window = new WindowBuilder(ws)
            .WithTitle("Connections")
            .WithName(OverlayName)
            .WithSize(width, height)
            .Centered()
            .WithBorderStyle(BorderStyle.Rounded)
            .WithPadding(1, 0)
            .WithBackgroundColor(background)
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

    private static string BuildHeaderRow(bool showProcess)
    {
        string remote = "remote address:port".PadRight(RemoteWidth);
        string state = "state".PadRight(StateWidth);
        string count = "count".PadLeft(CountWidth);
        if (!showProcess)
            return $"[dim]{remote} {state} {count}[/]";

        string process = "process".PadRight(ProcessWidth);
        string pid = "pid".PadLeft(PidWidth);
        return $"[dim]{remote} {state} {count} {process} {pid}[/]";
    }

    private static string BuildRow(ConnectionInfo c, bool showProcess)
    {
        string remoteCol = Truncate(c.Remote, RemoteWidth).PadRight(RemoteWidth);
        string stateCol = Truncate(c.State, StateWidth).PadRight(StateWidth);
        string countCol = c.Count.ToString().PadLeft(CountWidth);

        if (!showProcess)
            return $"[cyan]{remoteCol}[/] [dim]{stateCol}[/] {countCol}";

        string processCol = Truncate(c.Process ?? "", ProcessWidth).PadRight(ProcessWidth);
        string pidCol = (c.Pid?.ToString() ?? "").PadLeft(PidWidth);
        return $"[cyan]{remoteCol}[/] [dim]{stateCol}[/] {countCol} {processCol} [dim]{pidCol}[/]";
    }

    /// <summary>
    /// Best-effort list of connection rows, grouped by remote endpoint and sorted by count
    /// (descending). Returns an empty list on any error — the caller shows an empty state.
    /// On Linux the rows are additionally enriched with owning process/PID via <c>ss -tnp</c>;
    /// enrichment is entirely best-effort and never affects the base Remote/State/Count data.
    /// </summary>
    private static List<ConnectionInfo> GetConnections()
    {
        try
        {
            var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();

            // Group by "remote-endpoint | state" so the same peer in different states is
            // distinguishable, then sort by count descending.
            var rows = connections
                .GroupBy(c => (
                    Remote: c.RemoteEndPoint.ToString(),
                    State: c.State.ToString()))
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
            // GetActiveTcpConnections can throw NotImplemented/Network errors on some platforms.
            return new List<ConnectionInfo>();
        }
    }

    // Matches the users:(("name",pid=N,...)) fragment of an `ss -tnp` line.
    private static readonly Regex SsProcessRegex =
        new(@"\(""(?<name>[^""]+)"",pid=(?<pid>\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Best-effort map from a remote <c>address:port</c> endpoint to its owning process name and
    /// PID, parsed from <c>ss -tnp</c>. Linux-only; returns an empty map on ANY failure (non-Linux,
    /// ss missing, timeout, parse error). NEVER throws.
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

                // Whitespace columns: State Recv-Q Send-Q Local Peer users:(...)
                // The peer endpoint is the 5th column; the process fragment follows.
                var cols = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 5)
                    continue;

                // Skip the header row (starts with "State").
                if (cols[0] == "State")
                    continue;

                string peer = cols[4];
                var m = SsProcessRegex.Match(line);
                if (!m.Success)
                    continue;

                if (int.TryParse(m.Groups["pid"].Value, out int pid))
                    map[peer] = (m.Groups["name"].Value, pid);
            }
        }
        catch
        {
            // ss not present, permission denied, unexpected format — enrichment is optional.
            return new Dictionary<string, (string name, int pid)>(StringComparer.Ordinal);
        }

        return map;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
