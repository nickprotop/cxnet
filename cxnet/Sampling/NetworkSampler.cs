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

    /// <summary>
    /// Name prefixes of virtual / container / bridge interfaces that clutter the picker and are almost
    /// never what the user wants to monitor. Filtered out of <see cref="AvailableInterfaces"/> (which
    /// also feeds auto-selection, so a veth is never auto-picked). The currently-monitored interface is
    /// re-added by callers even if it matches, so an explicitly chosen virtual interface still shows.
    /// Best-effort and cross-platform: it's a name-prefix DROP, so prefixes that don't apply on a given
    /// OS simply never match (harmless). Covers Linux (veth/docker/br-/virbr/vnet/cni/cali/flannel),
    /// macOS (utun/awdl/llw/bridge/gif/stf), and common Windows virtual-adapter names (vEthernet/VMware/
    /// VirtualBox — matched case-insensitively against the interface Name).
    /// </summary>
    private static readonly string[] VirtualPrefixes =
    {
        // Linux
        "veth", "docker", "br-", "virbr", "vnet", "tun", "tap", "bond", "dummy", "cni", "cali", "flannel", "kube", "kvmbr", "vmbr", "lxc", "lxd", "wg", "zt",
        // macOS
        "utun", "awdl", "llw", "bridge", "gif", "stf", "ap",
        // Windows (friendly-name prefixes)
        "vEthernet", "VMware", "VirtualBox", "Hyper-V",
    };

    private static bool IsVirtual(string name)
    {
        foreach (var prefix in VirtualPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Non-loopback, non-virtual interfaces that are up and have an assigned address. Virtual /
    /// container / bridge interfaces (veth*, docker0, br-*, virbr0, …) are filtered out — see
    /// <see cref="VirtualPrefixes"/>.
    /// </summary>
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
    /// Available (non-loopback, up, addressed, non-virtual) interfaces with cross-platform display
    /// details: friendly type (Ethernet/Wi-Fi/…), first IPv4 address, and link speed.
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
            if (!HasUnicastAddress(ni))
                continue;
            if (IsVirtual(ni.Name))
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

    private static bool HasUnicastAddress(NetworkInterface ni)
    {
        try
        {
            return ni.GetIPProperties().UnicastAddresses.Count > 0;
        }
        catch
        {
            return false;
        }
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
