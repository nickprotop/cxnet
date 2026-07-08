using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Cxnet.Sampling;

/// <summary>
/// Reads cumulative RX/TX byte counters for a network interface and computes
/// per-second throughput deltas between successive reads.
/// </summary>
/// <remarks>
/// On Linux the counters come from <c>/proc/net/dev</c> for accuracy; on other
/// platforms they come from <see cref="NetworkInterface.GetIPStatistics"/>.
/// </remarks>
public sealed class NetworkSampler
{
    private long _lastRx;
    private long _lastTx;
    private long _lastTicks;

    /// <summary>The resolved interface name being sampled.</summary>
    public string InterfaceName { get; private set; }

    /// <summary>
    /// Creates a sampler for the given interface, or auto-selects the busiest
    /// non-loopback interface when <paramref name="interfaceName"/> is null.
    /// Establishes a baseline counter read so the first <see cref="Sample"/> is meaningful.
    /// </summary>
    public NetworkSampler(string? interfaceName = null)
    {
        InterfaceName = interfaceName ?? SelectBusiestInterface() ?? "lo";
        Baseline();
    }

    /// <summary>Names of all non-loopback interfaces that are up (used by auto-selection).</summary>
    public static IReadOnlyList<string> AvailableInterfaces()
    {
        var result = new List<string>();
        foreach (var info in AvailableInterfaceDetails())
            result.Add(info.Name);
        return result;
    }

    /// <summary>Human-facing details for an available interface, for the picker table. All fields are
    /// derived cross-platform from <see cref="NetworkInterface"/>.</summary>
    public readonly record struct InterfaceInfo(string Name, string Type, string IPv4, string Speed);

    /// <summary>
    /// All non-loopback interfaces that are up, with cross-platform display details: friendly type
    /// (Ethernet/Wi-Fi/…), first IPv4 address (empty if none), and link speed. No address or virtual/
    /// container filtering — every up interface is shown (an interface can carry byte counters without
    /// an IP, and the user may want to watch veth/docker/bridge interfaces too).
    /// </summary>
    public static IReadOnlyList<InterfaceInfo> AvailableInterfaceDetails()
    {
        var result = new List<InterfaceInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            result.Add(new InterfaceInfo(ni.Name, FriendlyType(ni), FirstIPv4(ni), FriendlySpeed(ni)));
        }
        return result;
    }

    private static string FriendlyType(NetworkInterface ni) => ni.NetworkInterfaceType switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ppp => "PPP",
        NetworkInterfaceType.Tunnel => "Tunnel",
        _ => ni.NetworkInterfaceType.ToString(),
    };

    private static string FirstIPv4(NetworkInterface ni)
    {
        try
        {
            foreach (var a in ni.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return a.Address.ToString();
            }
        }
        catch { /* best-effort */ }
        return "";
    }

    private static string FriendlySpeed(NetworkInterface ni)
    {
        long bps;
        try { bps = ni.Speed; } catch { return "-"; }
        if (bps <= 0)
            return "-";
        if (bps >= 1_000_000_000)
            return $"{bps / 1_000_000_000.0:0.#} Gb";
        if (bps >= 1_000_000)
            return $"{bps / 1_000_000} Mb";
        return $"{bps / 1_000} Kb";
    }

    /// <summary>Switches the sampled interface and re-establishes a baseline read.</summary>
    public void SelectInterface(string name)
    {
        InterfaceName = name;
        Baseline();
    }

    /// <summary>
    /// Reads the current counters and returns bytes/sec down and up computed
    /// against the previous read using a monotonic elapsed-time clock.
    /// </summary>
    public NetSample Sample()
    {
        var (rx, tx) = ReadCounters(InterfaceName);
        long nowTicks = Stopwatch.GetTimestamp();

        double elapsed = (nowTicks - _lastTicks) / (double)Stopwatch.Frequency;

        double down = 0;
        double up = 0;
        if (elapsed > 0)
        {
            // Guard against counter resets/wraparound producing negatives.
            long dRx = rx - _lastRx;
            long dTx = tx - _lastTx;
            if (dRx > 0) down = dRx / elapsed;
            if (dTx > 0) up = dTx / elapsed;
        }

        _lastRx = rx;
        _lastTx = tx;
        _lastTicks = nowTicks;

        return new NetSample(down, up, System.DateTime.UtcNow);
    }

    private void Baseline()
    {
        var (rx, tx) = ReadCounters(InterfaceName);
        _lastRx = rx;
        _lastTx = tx;
        _lastTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>Auto-select: the non-loopback interface with the highest total (rx+tx) cumulative bytes.</summary>
    private static string? SelectBusiestInterface()
    {
        string? best = null;
        long bestTotal = -1;
        foreach (var name in AvailableInterfaces())
        {
            var (rx, tx) = ReadCounters(name);
            long total = rx + tx;
            if (total > bestTotal)
            {
                bestTotal = total;
                best = name;
            }
        }
        return best;
    }

    private static (long rx, long tx) ReadCounters(string interfaceName)
    {
        if (OperatingSystem.IsLinux() && TryReadProcNetDev(interfaceName, out long rx, out long tx))
            return (rx, tx);
        return ReadViaNetworkInformation(interfaceName);
    }

    /// <summary>
    /// Parses <c>/proc/net/dev</c>. After the 2-line header each row is
    /// "iface: rxBytes ... txBytes ...": field[0]=rxBytes, field[8]=txBytes.
    /// </summary>
    private static bool TryReadProcNetDev(string interfaceName, out long rx, out long tx)
    {
        rx = 0;
        tx = 0;
        string[] lines;
        try
        {
            lines = File.ReadAllLines("/proc/net/dev");
        }
        catch
        {
            return false;
        }

        // Skip the 2-line header.
        for (int i = 2; i < lines.Length; i++)
        {
            string line = lines[i];
            int colon = line.IndexOf(':');
            if (colon < 0)
                continue;

            string name = line[..colon].Trim();
            if (name != interfaceName)
                continue;

            string[] fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9)
                return false;

            long.TryParse(fields[0], out rx);
            long.TryParse(fields[8], out tx);
            return true;
        }
        return false;
    }

    private static (long rx, long tx) ReadViaNetworkInformation(string interfaceName)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.Name != interfaceName)
                continue;
            var stats = ni.GetIPStatistics();
            return (stats.BytesReceived, stats.BytesSent);
        }
        return (0, 0);
    }
}
