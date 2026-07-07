using System.Reflection;
using System.Text.Json;
using Cxnet.Sampling;

namespace Cxnet;

/// <summary>Parsed command-line options.</summary>
internal readonly record struct CliOptions(
    bool Tiny,
    bool Mini,
    bool Compact,
    string? Interface,
    int RefreshMs,
    bool Bits,
    bool NoColor,
    bool Json,
    bool Once,
    bool Version,
    bool Help);

internal static class Program
{
    private const int DefaultRefreshMs = 100;
    private const int JsonSampleGapMs = 300;

    private static int Main(string[] args)
    {
        CliOptions opts;
        try
        {
            opts = ParseArgs(args);
        }
        catch (ArgumentException ex)
        {
            System.Console.Error.WriteLine($"cxnet: {ex.Message}");
            return 2;
        }

        if (opts.Help)
        {
            PrintUsage();
            return 0;
        }

        if (opts.Version)
        {
            System.Console.WriteLine($"cxnet {GetVersion()}");
            return 0;
        }

        if (opts.Json)
        {
            RunJson(opts);
            return 0;
        }

        if (opts.Once)
        {
            RunOnce(opts);
            return 0;
        }

        // No exit-flag: launch the interactive TUI.
        return RunTui(opts);
    }

    private static int RunTui(CliOptions opts)
    {
        var sampler = new NetworkSampler(opts.Interface);
        var state = new Cxnet.State.MonitorState();

        var ws = new SharpConsoleUI.ConsoleWindowSystem(
            new SharpConsoleUI.Drivers.NetConsoleDriver(SharpConsoleUI.Drivers.RenderMode.Buffer));

        int intervalMs = opts.RefreshMs > 0 ? opts.RefreshMs : DefaultRefreshMs;
        var monitor = new Cxnet.Ui.MonitorWindow(ws, sampler, state, intervalMs, opts.Bits);
        monitor.Show();

        return ws.Run();
    }

    private static NetSample DoubleSample(NetworkSampler sampler, int gapMs)
    {
        sampler.Sample();               // establish/refresh baseline
        System.Threading.Thread.Sleep(gapMs);
        return sampler.Sample();        // meaningful delta
    }

    private static void RunJson(CliOptions opts)
    {
        var sampler = new NetworkSampler(opts.Interface);
        int gap = opts.RefreshMs > 0 ? Math.Max(opts.RefreshMs, JsonSampleGapMs) : JsonSampleGapMs;
        NetSample s = DoubleSample(sampler, gap);

        var payload = new
        {
            @interface = sampler.InterfaceName,
            downBytesPerSec = s.DownBytesPerSec,
            upBytesPerSec = s.UpBytesPerSec,
            timestamp = s.Timestamp.ToString("o")
        };

        System.Console.WriteLine(JsonSerializer.Serialize(payload));
    }

    private static void RunOnce(CliOptions opts)
    {
        var sampler = new NetworkSampler(opts.Interface);
        int gap = opts.RefreshMs > 0 ? Math.Max(opts.RefreshMs, JsonSampleGapMs) : JsonSampleGapMs;
        NetSample s = DoubleSample(sampler, gap);

        System.Console.WriteLine(
            $"{sampler.InterfaceName}  ↓ {s.DownBytesPerSec:F0} B/s  ↑ {s.UpBytesPerSec:F0} B/s");
    }

    private static CliOptions ParseArgs(string[] args)
    {
        bool tiny = false, mini = false, compact = false, bits = false, noColor = false;
        bool json = false, once = false, version = false, help = false;
        string? iface = null;
        int refreshMs = DefaultRefreshMs;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--tiny": tiny = true; break;
                case "--mini": mini = true; break;
                case "--compact": compact = true; break;
                case "--bits": bits = true; break;
                case "--no-color": noColor = true; break;
                case "--json": json = true; break;
                case "--once": once = true; break;
                case "--version": version = true; break;
                case "--help":
                case "-h": help = true; break;
                case "--interface":
                case "-i":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{a} requires an interface name");
                    iface = args[++i];
                    break;
                case "--refresh":
                case "-r":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{a} requires a duration (e.g. 500ms, 1s)");
                    refreshMs = ParseDuration(args[++i]);
                    break;
                default:
                    if (a.StartsWith("--interface=", StringComparison.Ordinal))
                        iface = a["--interface=".Length..];
                    else if (a.StartsWith("--refresh=", StringComparison.Ordinal))
                        refreshMs = ParseDuration(a["--refresh=".Length..]);
                    else
                        throw new ArgumentException($"unknown option '{a}' (try --help)");
                    break;
            }
        }

        return new CliOptions(tiny, mini, compact, iface, refreshMs, bits, noColor, json, once, version, help);
    }

    /// <summary>Parses a duration like "500ms" or "1s" into milliseconds.</summary>
    private static int ParseDuration(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        double scale = 1;
        string number = v;

        if (v.EndsWith("ms", StringComparison.Ordinal))
        {
            number = v[..^2];
            scale = 1;
        }
        else if (v.EndsWith("s", StringComparison.Ordinal))
        {
            number = v[..^1];
            scale = 1000;
        }

        if (!double.TryParse(number, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double n) || n < 0)
            throw new ArgumentException($"invalid duration '{value}'");

        int ms = (int)Math.Round(n * scale);
        return ms > 0 ? ms : DefaultRefreshMs;
    }

    private static string GetVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static void PrintUsage()
    {
        System.Console.WriteLine(
"""
cxnet - network throughput monitor

Usage:
  cxnet [options]

Options:
  -i, --interface <name>   Interface to monitor (default: busiest non-loopback)
  -r, --refresh <dur>      Refresh interval, e.g. 500ms or 1s (default: 100ms)
      --bits               Show bits/sec instead of bytes/sec
      --tiny               Tiny display mode
      --mini               Mini display mode
      --compact            Compact display mode
      --no-color           Disable color output
      --json               Print one JSON throughput sample and exit
      --once               Print one plain throughput line and exit
      --version            Print version and exit
  -h, --help               Print this help and exit
""");
    }
}
