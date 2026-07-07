using Cxnet.Sampling;

namespace Cxnet.State;

/// <summary>Display unit for throughput values.</summary>
public enum Units
{
    /// <summary>Bytes per second (B/s, KB/s, ...).</summary>
    Bytes,

    /// <summary>Bits per second (bps, Kbps, ...).</summary>
    Bits
}

/// <summary>
/// Holds the live monitor state: the current sample, a bounded history ring for
/// sparklines/graphs, session peaks, and accumulated totals.
/// </summary>
public sealed class MonitorState
{
    /// <summary>
    /// Number of samples retained for graph history. Large enough to fill the full pixel-width of the
    /// waveform even in a wide window (Braille packs 2 columns per cell, so a ~120-col window needs
    /// ~240 points); a bigger ring just means the graph scrolls older data off the left.
    /// </summary>
    public const int HistorySize = 512;

    private readonly NetSample[] _ring = new NetSample[HistorySize];
    private int _head;   // index of the next write slot
    private int _count;  // number of valid entries in the ring

    /// <summary>The most recent sample added.</summary>
    public NetSample Current { get; private set; }

    /// <summary>Session peak download rate in bytes/sec.</summary>
    public double PeakDown { get; private set; }

    /// <summary>Session peak upload rate in bytes/sec.</summary>
    public double PeakUp { get; private set; }

    /// <summary>Accumulated downloaded bytes over the session.</summary>
    public double TotalDownBytes { get; private set; }

    /// <summary>Accumulated uploaded bytes over the session.</summary>
    public double TotalUpBytes { get; private set; }

    /// <summary>Current display units.</summary>
    public Units Units { get; set; } = Units.Bytes;

    /// <summary>Name of the interface being monitored.</summary>
    public string InterfaceName { get; set; } = string.Empty;

    /// <summary>Adds a sample, updating current, history ring, peaks, and totals.</summary>
    public void Add(NetSample s)
    {
        Current = s;

        _ring[_head] = s;
        _head = (_head + 1) % HistorySize;
        if (_count < HistorySize)
            _count++;

        if (s.DownBytesPerSec > PeakDown) PeakDown = s.DownBytesPerSec;
        if (s.UpBytesPerSec > PeakUp) PeakUp = s.UpBytesPerSec;

        // Rates are per-second; each sample represents roughly one refresh interval,
        // but for a simple accumulator we add the instantaneous per-second rate.
        double elapsed = ElapsedSeconds(s);
        TotalDownBytes += s.DownBytesPerSec * elapsed;
        TotalUpBytes += s.UpBytesPerSec * elapsed;

        _lastTimestamp = s.Timestamp;
    }

    private System.DateTime? _lastTimestamp;

    private double ElapsedSeconds(NetSample s)
    {
        if (_lastTimestamp is null)
            return 0;
        double dt = (s.Timestamp - _lastTimestamp.Value).TotalSeconds;
        return dt > 0 ? dt : 0;
    }

    /// <summary>Resets session peaks to zero (the <c>r</c> key).</summary>
    public void ResetPeaks()
    {
        PeakDown = 0;
        PeakUp = 0;
    }

    /// <summary>Recent samples in chronological (oldest → newest) order.</summary>
    public IReadOnlyList<NetSample> History()
    {
        var result = new NetSample[_count];
        int start = (_head - _count + HistorySize) % HistorySize;
        for (int i = 0; i < _count; i++)
            result[i] = _ring[(start + i) % HistorySize];
        return result;
    }

    /// <summary>Recent download rates (bytes/sec) in chronological order, for graphs.</summary>
    public double[] DownHistory()
    {
        var history = History();
        var result = new double[history.Count];
        for (int i = 0; i < history.Count; i++)
            result[i] = history[i].DownBytesPerSec;
        return result;
    }

    /// <summary>Recent upload rates (bytes/sec) in chronological order, for graphs.</summary>
    public double[] UpHistory()
    {
        var history = History();
        var result = new double[history.Count];
        for (int i = 0; i < history.Count; i++)
            result[i] = history[i].UpBytesPerSec;
        return result;
    }
}
