namespace Cxnet.Ui;

/// <summary>
/// The responsive display modes cxnet adapts between as the terminal is resized.
/// Larger terminals show more chrome (header, sparkline, footer); smaller ones
/// progressively shed it down to a single status line.
/// </summary>
public enum DisplayMode
{
    /// <summary>Full layout: header + both waveforms + sparkline + peaks/totals + footer.</summary>
    Hero,

    /// <summary>Title row + both waveforms + a condensed stat line. No sparkline/footer.</summary>
    Compact,

    /// <summary>Graphs only: both waveforms. No header/stats/footer.</summary>
    Mini,

    /// <summary>A single status line: <c>↓ &lt;down&gt;  ↑ &lt;up&gt;</c>.</summary>
    Tiny,
}

/// <summary>
/// Resolves a <see cref="DisplayMode"/> from the usable desktop size, so the UI can
/// auto-switch layouts on resize (flow's signature responsive behaviour).
/// </summary>
public static class DisplayModeResolver
{
    // Thresholds tuned against the usable desktop (status bars excluded). Height is the
    // primary driver; width gates the too-narrow case into Mini so waveforms stay legible.
    private const int TinyMaxHeight = 3;      // single status line
    private const int MiniMaxHeight = 14;     // graphs only, no chrome
    private const int MiniMinWidth = 60;      // narrower than this → graphs only
    private const int CompactMaxHeight = 24;  // title + waveforms + condensed stats

    /// <summary>
    /// Resolves the display mode for the given usable desktop dimensions.
    /// </summary>
    /// <param name="width">Usable desktop width in columns.</param>
    /// <param name="height">Usable desktop height in rows.</param>
    /// <returns>The mode whose layout best fits the available space.</returns>
    public static DisplayMode Resolve(int width, int height)
    {
        if (height <= TinyMaxHeight)
            return DisplayMode.Tiny;

        if (height <= MiniMaxHeight || width < MiniMinWidth)
            return DisplayMode.Mini;

        if (height <= CompactMaxHeight)
            return DisplayMode.Compact;

        return DisplayMode.Hero;
    }
}
