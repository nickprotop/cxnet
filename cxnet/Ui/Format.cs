using Cxnet.State;
using SharpConsoleUI;

namespace Cxnet.Ui;

/// <summary>
/// Formatting helpers for the TUI: human-readable throughput scaling and a
/// speed-to-color gradient used for the live border and throughput numbers.
/// </summary>
internal static class Format
{
    private const double Kilo = 1024.0;

    // Speed-color gradient is log-scaled between an idle floor and a "fast" ceiling.
    private const double ColorFloorBytesPerSec = 4 * 1024;          // ~4 KB/s → fully "idle"
    private const double ColorCeilingBytesPerSec = 50 * 1024 * 1024; // ~50 MB/s → fully "hot"

    /// <summary>
    /// Formats a bytes/sec rate as a human string in the requested units.
    /// Bytes → "12.3 MB/s" (B/s, KB/s, MB/s, GB/s). Bits → value ×8 as
    /// "b/s, Kb/s, Mb/s, Gb/s". One decimal place.
    /// </summary>
    public static string Scale(double bytesPerSec, Units units)
    {
        if (double.IsNaN(bytesPerSec) || bytesPerSec < 0)
            bytesPerSec = 0;

        if (units == Units.Bits)
        {
            double bits = bytesPerSec * 8.0;
            string[] bitUnits = { "b/s", "Kb/s", "Mb/s", "Gb/s", "Tb/s" };
            return ScaleWith(bits, bitUnits);
        }

        string[] byteUnits = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        return ScaleWith(bytesPerSec, byteUnits);
    }

    private static string ScaleWith(double value, string[] unitLabels)
    {
        int idx = 0;
        while (value >= Kilo && idx < unitLabels.Length - 1)
        {
            value /= Kilo;
            idx++;
        }
        return $"{value:0.0} {unitLabels[idx]}";
    }

    /// <summary>
    /// Maps a bytes/sec rate to a gradient stop: idle = cool blue, rising through
    /// cyan/green/yellow to red at high speed. Uses a log scale between an idle
    /// floor and a ~50 MB/s ceiling. Used for the window border and throughput text.
    /// </summary>
    public static Color SpeedColor(double bytesPerSec)
    {
        double t = Normalize(bytesPerSec);

        // Gradient stops: blue → cyan → green → yellow → red.
        (double pos, Color color)[] stops =
        {
            (0.00, new Color(64, 128, 255)),  // cool blue (idle)
            (0.30, new Color(0, 200, 220)),   // cyan
            (0.55, new Color(60, 220, 90)),   // green
            (0.80, new Color(240, 210, 40)),  // yellow
            (1.00, new Color(240, 60, 50)),   // red (hot)
        };

        for (int i = 0; i < stops.Length - 1; i++)
        {
            var (p0, c0) = stops[i];
            var (p1, c1) = stops[i + 1];
            if (t <= p1)
            {
                double span = p1 - p0;
                double local = span > 0 ? (t - p0) / span : 0;
                return Lerp(c0, c1, local);
            }
        }
        return stops[^1].color;
    }

    /// <summary>Log-scaled 0..1 position of a rate between the idle floor and hot ceiling.</summary>
    private static double Normalize(double bytesPerSec)
    {
        if (double.IsNaN(bytesPerSec) || bytesPerSec <= ColorFloorBytesPerSec)
            return 0.0;
        if (bytesPerSec >= ColorCeilingBytesPerSec)
            return 1.0;

        double logMin = Math.Log(ColorFloorBytesPerSec);
        double logMax = Math.Log(ColorCeilingBytesPerSec);
        double logVal = Math.Log(bytesPerSec);
        return Math.Clamp((logVal - logMin) / (logMax - logMin), 0.0, 1.0);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
        byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
        byte bch = (byte)Math.Round(a.B + (b.B - a.B) * t);
        return new Color(r, g, bch);
    }
}
