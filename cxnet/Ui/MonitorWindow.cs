using Cxnet.Sampling;
using Cxnet.State;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace Cxnet.Ui;

/// <summary>
/// The hero-mode monitor window: a large, rounded-border window showing live
/// download/upload throughput as Braille <see cref="LineGraphControl"/> waveforms,
/// a block <see cref="SparklineControl"/> history row, and a markup stat panel.
/// The active border color tracks the current speed via <see cref="Format.SpeedColor"/>.
/// </summary>
/// <remarks>
/// Charts are created once and fed on the window's own async thread; nothing is rebuilt
/// per frame. Feeds go through <see cref="Window.FindControl{T}(string)"/> because
/// <c>AddDataPoint</c> and <c>SetContent</c> are self-invalidating.
/// </remarks>
internal sealed class MonitorWindow
{
    private const int MinIntervalMs = 50;
    private const int MaxIntervalMs = 2000;
    private const int IntervalStepMs = 50;

    // Waveform vertical scale (bytes/sec). Auto-fit would flatten quiet periods, so we
    // pick a fixed, generous ceiling; bursts above it simply clip at the top.
    private const double GraphMaxBytesPerSec = 12 * 1024 * 1024; // ~12 MB/s full-scale

    private readonly ConsoleWindowSystem _ws;
    private readonly NetworkSampler _sampler;
    private readonly MonitorState _state;

    private int _intervalMs;
    private Window? _window;

    // Current responsive layout mode. Auto-switches on resize; the 'm' key cycles it manually
    // (that override holds until the next resize, which re-resolves from the desktop size).
    private DisplayMode _mode;
    private bool _rebuilding; // re-entrancy guard: a resize firing mid-rebuild must not recurse.

    public MonitorWindow(ConsoleWindowSystem ws, NetworkSampler sampler, MonitorState state, int intervalMs, bool bits, DisplayMode? initialMode = null)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _intervalMs = Math.Clamp(intervalMs > 0 ? intervalMs : 100, MinIntervalMs, MaxIntervalMs);
        _state.Units = bits ? Units.Bits : Units.Bytes;
        _state.InterfaceName = _sampler.InterfaceName;

        // A CLI flag (--tiny/--mini/--compact) pins the initial mode; otherwise resolve from
        // the current desktop size. A subsequent resize re-resolves either way.
        var d = _ws.DesktopDimensions;
        _mode = initialMode ?? DisplayModeResolver.Resolve(d.Width, d.Height);
    }

    /// <summary>The built window.</summary>
    public Window? Window => _window;

    /// <summary>Builds the window and adds it to the system, making it active.</summary>
    public void Show()
    {
        _window ??= Build();
        _ws.AddWindow(_window);
        _ws.SetActiveWindow(_window);

        // Auto-switch layout modes when the terminal is resized. WindowResized fires on the UI
        // thread after the framework has repositioned windows, so mutating controls here is safe.
        _ws.WindowResized += OnWindowResized;
    }

    private Window Build()
    {
        var window = new WindowBuilder(_ws)
            .WithTitle($"cxnet · {_sampler.InterfaceName}")
            .WithName("cxnet")
            .WithBorderStyle(BorderStyle.Rounded)
            .WithSize(96, 34)
            .Centered()
            // ALPHA showcase: a translucent deep-navy background (a < 255) so the
            // waveforms read as glowing over a faded surface rather than a flat fill.
            .WithBackgroundColor(new Color(10, 16, 28, 200))
            .AddControl(BuildContentFor(_mode))
            .WithAsyncWindowThread(UpdateLoopAsync)
            .OnKeyPressed(OnKeyPressed)
            .Build();

        return window;
    }

    // ── Reusable control factories ──────────────────────────────────────────────────
    // Each mode composes a subset of these. Control NAMES are kept stable ("down"/"up"/
    // "spark"/"stats") so the async feed's FindControl<T>(name)?. calls no-op gracefully
    // when a control is absent in the current mode.

    private static LineGraphControl DownGraph() => Controls.LineGraph()
        .WithTitle("↓ Download", new Color(96, 165, 250))
        .WithMode(LineGraphMode.Braille)
        .WithMaxValue(GraphMaxBytesPerSec)
        .AddSeries("down", new Color(96, 165, 250), "cool")
        .WithYAxisLabels(false)
        .WithBaseline()
        .WithName("down")
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .WithMargin(1, 0, 1, 0)
        .Build();

    private static LineGraphControl UpGraph() => Controls.LineGraph()
        .WithTitle("↑ Upload", new Color(251, 146, 60))
        .WithMode(LineGraphMode.Braille)
        .WithMaxValue(GraphMaxBytesPerSec)
        .AddSeries("up", new Color(251, 146, 60), "warm")
        .WithYAxisLabels(false)
        .WithBaseline()
        .WithName("up")
        .WithVerticalAlignment(VerticalAlignment.Fill)
        .WithMargin(1, 0, 1, 0)
        .Build();

    private static SparklineControl Spark() => Controls.Sparkline()
        .WithMode(SparklineMode.Block)
        .WithHeight(3)
        .WithMaxValue(GraphMaxBytesPerSec)
        .WithName("spark")
        .WithMargin(1, 0, 1, 0)
        .Build();

    private MarkupControl Stats(IEnumerable<string> lines)
    {
        var stats = Controls.Markup(string.Empty)
            .WithName("stats")
            .WithMargin(1, 0, 1, 0)
            .Build();
        stats.SetContent(lines.ToList());
        return stats;
    }

    /// <summary>
    /// Builds the interior root control (a <see cref="GridControl"/>) laid out for the given mode.
    /// Called at construction and on every mode switch; the window shell is reused across switches.
    /// </summary>
    private IWindowControl BuildContentFor(DisplayMode mode)
    {
        switch (mode)
        {
            case DisplayMode.Tiny:
                // Single status line: ↓ <down>  ↑ <up>. No graphs.
                return Stats(TinyStatLines());

            case DisplayMode.Mini:
                // Graphs only — both waveforms, no header/stats/footer.
                return Controls.Grid()
                    .Columns(GridLength.Star(1))
                    .Rows(GridLength.Star(1), GridLength.Star(1))
                    .RowGap(1)
                    .WithPadding(1, 0, 1, 0)
                    .WithVerticalAlignment(VerticalAlignment.Fill)
                    .WithAlignment(HorizontalAlignment.Stretch)
                    .Place(DownGraph(), 0, 0)
                    .Place(UpGraph(), 1, 0)
                    .Build();

            case DisplayMode.Compact:
                // Title row + both waveforms + a condensed stat line. No sparkline/footer.
                return Controls.Grid()
                    .Columns(GridLength.Star(1))
                    .Rows(GridLength.Star(3), GridLength.Star(3), GridLength.Auto())
                    .RowGap(1)
                    .WithPadding(1, 0, 1, 0)
                    .WithVerticalAlignment(VerticalAlignment.Fill)
                    .WithAlignment(HorizontalAlignment.Stretch)
                    .Place(DownGraph(), 0, 0)
                    .Place(UpGraph(), 1, 0)
                    .Place(Stats(CompactStatLines()), 2, 0)
                    .Build();

            case DisplayMode.Hero:
            default:
                // Full layout: both waveforms + sparkline + full stat panel + keybinding footer.
                return Controls.Grid()
                    .Columns(GridLength.Star(1))
                    .Rows(GridLength.Star(3), GridLength.Star(3), GridLength.Auto(), GridLength.Auto())
                    .RowGap(1)
                    .WithPadding(1, 0, 1, 0)
                    .WithVerticalAlignment(VerticalAlignment.Fill)
                    .WithAlignment(HorizontalAlignment.Stretch)
                    .Place(DownGraph(), 0, 0)
                    .Place(UpGraph(), 1, 0)
                    .Place(Spark(), 2, 0)
                    .Place(Stats(BuildStatLines()), 3, 0)
                    .Build();
        }
    }

    /// <summary>
    /// Reflows the window content in place to <paramref name="mode"/> by clearing the window's
    /// controls and re-adding a freshly-built root. The window shell (and its async feed thread)
    /// is preserved; the feed re-finds controls by name after the swap. Re-entrant-safe.
    /// </summary>
    private void SwitchMode(DisplayMode mode)
    {
        if (_window is null || mode == _mode || _rebuilding)
            return;

        _rebuilding = true;
        try
        {
            _mode = mode;
            _window.ClearControls();
            _window.AddControl(BuildContentFor(_mode));
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private static DisplayMode NextMode(DisplayMode mode) => mode switch
    {
        DisplayMode.Hero => DisplayMode.Compact,
        DisplayMode.Compact => DisplayMode.Mini,
        DisplayMode.Mini => DisplayMode.Tiny,
        _ => DisplayMode.Hero,
    };

    private void OnWindowResized(object? sender, SharpConsoleUI.Helpers.Size size)
    {
        if (_window is null)
            return;

        var d = _ws.DesktopDimensions;
        var newMode = DisplayModeResolver.Resolve(d.Width, d.Height);
        if (newMode != _mode)
            SwitchMode(newMode);
    }

    /// <summary>
    /// Async feed: every <see cref="_intervalMs"/> ms takes a sample, records it in state,
    /// pushes it into the two waveforms + sparkline, refreshes the stat markup, and drives
    /// the active border color from the faster of the two rates.
    /// </summary>
    private async Task UpdateLoopAsync(Window window, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var s = _sampler.Sample();
            _state.Add(s);
            _state.InterfaceName = _sampler.InterfaceName;

            window.FindControl<LineGraphControl>("down")?.AddDataPoint("down", s.DownBytesPerSec);
            window.FindControl<LineGraphControl>("up")?.AddDataPoint("up", s.UpBytesPerSec);
            window.FindControl<SparklineControl>("spark")?.AddDataPoint(s.DownBytesPerSec);
            window.FindControl<MarkupControl>("stats")?.SetContent(CurrentStatLines());

            // ActiveBorderForegroundColor is a self-invalidating Window property setter;
            // per the MetricsWindow feed pattern it is safe to set from this thread.
            window.ActiveBorderForegroundColor =
                Format.SpeedColor(Math.Max(s.DownBytesPerSec, s.UpBytesPerSec));
        }
    }

    private List<string> BuildStatLines()
    {
        var cur = _state.Current;
        Units u = _state.Units;

        string down = Format.Scale(cur.DownBytesPerSec, u);
        string up = Format.Scale(cur.UpBytesPerSec, u);
        string peakDown = Format.Scale(_state.PeakDown, u);
        string peakUp = Format.Scale(_state.PeakUp, u);
        string totalDown = FormatTotalBytes(_state.TotalDownBytes);
        string totalUp = FormatTotalBytes(_state.TotalUpBytes);

        string downArrow = TrendArrow(cur.DownBytesPerSec, _state.PeakDown);
        string upArrow = TrendArrow(cur.UpBytesPerSec, _state.PeakUp);

        string downHex = ToHex(Format.SpeedColor(cur.DownBytesPerSec));
        string upHex = ToHex(Format.SpeedColor(cur.UpBytesPerSec));

        return new List<string>
        {
            $"[bold]↓[/] [{downHex}]{down,-12}[/] {downArrow}   " +
            $"[bold]↑[/] [{upHex}]{up,-12}[/] {upArrow}",
            $"[dim]peak[/]  ↓ {peakDown,-12} ↑ {peakUp,-12}",
            $"[dim]total[/] ↓ {totalDown,-12} ↑ {totalUp,-12}",
            $"[dim]iface[/] [cyan]{_state.InterfaceName}[/]   " +
            $"[dim]latency[/] [grey]— ms[/]   " +
            $"[dim]units[/] {(u == Units.Bits ? "bits" : "bytes")}   " +
            $"[dim]interval[/] {_intervalMs} ms",
            "[dim]q quit · r reset peaks · i iface · b bits/bytes · +/- interval · t themes · n conns[/]",
        };
    }

    /// <summary>Condensed stat block for Compact mode: current ↓/↑ + peaks + totals (no footer).</summary>
    private List<string> CompactStatLines()
    {
        var cur = _state.Current;
        Units u = _state.Units;

        string down = Format.Scale(cur.DownBytesPerSec, u);
        string up = Format.Scale(cur.UpBytesPerSec, u);
        string peakDown = Format.Scale(_state.PeakDown, u);
        string peakUp = Format.Scale(_state.PeakUp, u);
        string totalDown = FormatTotalBytes(_state.TotalDownBytes);
        string totalUp = FormatTotalBytes(_state.TotalUpBytes);

        string downHex = ToHex(Format.SpeedColor(cur.DownBytesPerSec));
        string upHex = ToHex(Format.SpeedColor(cur.UpBytesPerSec));

        return new List<string>
        {
            $"[bold]↓[/] [{downHex}]{down,-12}[/] [bold]↑[/] [{upHex}]{up,-12}[/] " +
            $"[dim]peak[/] ↓ {peakDown,-10} ↑ {peakUp,-10} " +
            $"[dim]total[/] ↓ {totalDown,-9} ↑ {totalUp,-9}",
        };
    }

    /// <summary>Single-line stat for Tiny mode: ↓ &lt;down&gt;  ↑ &lt;up&gt;.</summary>
    private List<string> TinyStatLines()
    {
        var cur = _state.Current;
        Units u = _state.Units;

        string down = Format.Scale(cur.DownBytesPerSec, u);
        string up = Format.Scale(cur.UpBytesPerSec, u);
        string downHex = ToHex(Format.SpeedColor(cur.DownBytesPerSec));
        string upHex = ToHex(Format.SpeedColor(cur.UpBytesPerSec));

        return new List<string>
        {
            $"[bold]↓[/] [{downHex}]{down}[/]  [bold]↑[/] [{upHex}]{up}[/]",
        };
    }

    private static string TrendArrow(double value, double peak)
    {
        if (value <= 0)
            return "[grey]·[/]";
        if (peak > 0 && value >= peak * 0.9)
            return "[red]▲[/]";
        if (value >= peak * 0.5)
            return "[yellow]▴[/]";
        return "[green]▾[/]";
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string FormatTotalBytes(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes < 0 ? 0 : bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return $"{v:0.0} {units[i]}";
    }

    private void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        var key = e.KeyInfo;

        // q or Ctrl+C → quit.
        if (key.Key == ConsoleKey.Q ||
            (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
        {
            _ws.Shutdown(0);
            e.Handled = true;
            return;
        }

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'r':
                _state.ResetPeaks();
                RefreshStats();
                e.Handled = true;
                break;

            case 'i':
                CycleInterface();
                e.Handled = true;
                break;

            case 'b':
                _state.Units = _state.Units == Units.Bytes ? Units.Bits : Units.Bytes;
                RefreshStats();
                e.Handled = true;
                break;

            case '+':
            case '=': // unshifted '+' key
                _intervalMs = Math.Clamp(_intervalMs + IntervalStepMs, MinIntervalMs, MaxIntervalMs);
                RefreshStats();
                e.Handled = true;
                break;

            case '-':
            case '_':
                _intervalMs = Math.Clamp(_intervalMs - IntervalStepMs, MinIntervalMs, MaxIntervalMs);
                RefreshStats();
                e.Handled = true;
                break;

            // m cycles the display mode manually (Hero → Compact → Mini → Tiny → Hero),
            // overriding the auto mode until the next resize re-resolves from the desktop size.
            case 'm':
                SwitchMode(NextMode(_mode));
                e.Handled = true;
                break;

            // t → translucent theme picker; n → translucent connections overlay.
            // Both toggle (a second press closes) and composite over the live waveforms.
            case 't':
                ThemePicker.Show(_ws);
                e.Handled = true;
                break;

            case 'n':
                ProcessPanel.Show(_ws);
                e.Handled = true;
                break;
        }
    }

    private void CycleInterface()
    {
        var available = NetworkSampler.AvailableInterfaces();
        if (available.Count == 0)
            return;

        int idx = -1;
        for (int i = 0; i < available.Count; i++)
        {
            if (available[i] == _sampler.InterfaceName)
            {
                idx = i;
                break;
            }
        }

        string next = available[(idx + 1) % available.Count];
        _sampler.SelectInterface(next);
        _state.InterfaceName = next;
        RefreshStats();
    }

    /// <summary>The stat lines appropriate for the current display mode.</summary>
    private List<string> CurrentStatLines() => _mode switch
    {
        DisplayMode.Tiny => TinyStatLines(),
        DisplayMode.Compact => CompactStatLines(),
        _ => BuildStatLines(), // Hero (Mini has no stats control, so this is harmless there)
    };

    private void RefreshStats()
    {
        _window?.FindControl<MarkupControl>("stats")?.SetContent(CurrentStatLines());
    }
}
