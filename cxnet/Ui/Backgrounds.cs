using SharpConsoleUI;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout; // Cell, CharacterBuffer
using SharpConsoleUI.Rendering;

namespace Cxnet.Ui;

/// <summary>
/// The catalogue of desktop-background presets offered by the <see cref="BackgroundPortal"/> — the same
/// samples the framework's DemoApp demonstrates: solid gradients, tiled patterns, gradient+pattern
/// combinations, the framework's animated effects, and a Matrix digital-rain effect (ported here, as it
/// lives in the DemoApp, not the framework). Each entry is a name + a factory that produces a fresh
/// <see cref="DesktopBackgroundConfig"/> (fresh so animated effects get independent closure state).
/// </summary>
internal static class Backgrounds
{
    /// <summary>
    /// Name of the most recently applied preset (via <see cref="Apply"/>), so the picker can pre-select
    /// the current background. Null until one is applied through this catalogue.
    /// </summary>
    public static string? CurrentName { get; private set; }

    /// <summary>Applies the named preset to the window system and records it as current. No-op if unknown.</summary>
    public static void Apply(ConsoleWindowSystem ws, string name)
    {
        foreach (var (n, make) in Presets)
        {
            if (n == name)
            {
                ws.DesktopBackground = make();
                CurrentName = name;
                return;
            }
        }
    }

    /// <summary>Ordered (name, factory) presets for the picker.</summary>
    public static readonly (string Name, Func<DesktopBackgroundConfig> Make)[] Presets =
    {
        // ── Gradients ──────────────────────────────────────────────────────────────
        ("Midnight Blue", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(20, 30, 80), new Color(5, 5, 15)), GradientDirection.Vertical)),
        ("Ocean Depth", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(10, 40, 90), new Color(5, 10, 25)), GradientDirection.Vertical)),
        ("Forest Night", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(10, 50, 25), new Color(5, 15, 10)), GradientDirection.Vertical)),
        ("Sunset", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(120, 40, 20), new Color(40, 10, 60)), GradientDirection.Horizontal)),
        ("Aurora", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(20, 60, 100), new Color(60, 20, 80)), GradientDirection.DiagonalDown)),
        ("Ember", () => DesktopBackgroundConfig.FromGradient(
            ColorGradient.FromColors(new Color(80, 15, 10), new Color(40, 10, 50)), GradientDirection.DiagonalUp)),

        // ── Patterns ───────────────────────────────────────────────────────────────
        ("Checkerboard", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.Checkerboard)),
        ("Dots", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.Dots)),
        ("Hatch Down", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.HatchDown)),
        ("Hatch Up", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.HatchUp)),
        ("Crosshatch", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.Crosshatch)),
        ("Light Shade", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.LightShade)),
        ("Medium Shade", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.MediumShade)),
        ("Dense Shade", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.DenseShade)),
        ("Horizontal Lines", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.HorizontalLines)),
        ("Vertical Lines", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.VerticalLines)),
        ("Grid", () => DesktopBackgroundConfig.FromPattern(DesktopPatterns.Grid)),

        // ── Combined (gradient + pattern) ──────────────────────────────────────────
        ("Midnight Grid", () => Combined(new Color(20, 30, 80), new Color(5, 5, 15), GradientDirection.Vertical, DesktopPatterns.Grid)),
        ("Ocean Dots", () => Combined(new Color(10, 40, 90), new Color(5, 10, 25), GradientDirection.Vertical, DesktopPatterns.Dots)),
        ("Forest Crosshatch", () => Combined(new Color(10, 50, 25), new Color(5, 15, 10), GradientDirection.Vertical, DesktopPatterns.Crosshatch)),
        ("Sunset Checkerboard", () => Combined(new Color(120, 40, 20), new Color(40, 10, 60), GradientDirection.Horizontal, DesktopPatterns.Checkerboard)),
        ("Aurora Lines", () => Combined(new Color(20, 60, 100), new Color(60, 20, 80), GradientDirection.DiagonalDown, DesktopPatterns.HorizontalLines)),

        // ── Animated ───────────────────────────────────────────────────────────────
        ("Color Cycling", () => DesktopEffects.ColorCycling()),
        ("Pulse", () => DesktopEffects.Pulse(new Color(15, 25, 60))),
        ("Drifting Gradient", () => DesktopEffects.DriftingGradient(new Color(20, 40, 80), new Color(60, 20, 70))),
        ("Matrix Rain", MatrixRain),
    };

    private static DesktopBackgroundConfig Combined(Color from, Color to, GradientDirection dir, DesktopPattern pattern) =>
        new()
        {
            Gradient = new GradientBackground(ColorGradient.FromColors(from, to), dir),
            Pattern = pattern,
        };

    /// <summary>
    /// A Matrix digital-rain effect (ported from the framework's DemoApp) as a paint-callback background.
    /// Per-column falling trails of half-width kana/digits, a bright head fading to dark green. State is
    /// captured in the closure, so each call produces independent rain (re-init on resize).
    /// </summary>
    private static DesktopBackgroundConfig MatrixRain()
    {
        const int trailLength = 14;
        const double spawnChance = 0.04;
        const string glyphs = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789";

        int[]? heads = null;
        int[]? speeds = null;
        int[]? cooldowns = null;
        char[,]? chars = null;
        Random? rng = null;
        int lastW = 0, lastH = 0;

        return new DesktopBackgroundConfig
        {
            AnimationIntervalMs = 70,
            PaintCallback = (buffer, width, height, _) =>
            {
                rng ??= new Random();

                if (heads == null || width != lastW || height != lastH)
                {
                    lastW = width;
                    lastH = height;
                    heads = new int[width];
                    speeds = new int[width];
                    cooldowns = new int[width];
                    chars = new char[height, width];
                    for (int c = 0; c < width; c++)
                    {
                        heads[c] = -1;
                        speeds[c] = rng.Next(1, 3);
                        cooldowns[c] = 0;
                    }
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            chars[y, x] = glyphs[rng.Next(glyphs.Length)];
                }

                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        buffer.SetCell(x, y, new Cell(' ', Color.Black, Color.Black));

                for (int col = 0; col < width; col++)
                {
                    if (heads![col] >= 0)
                    {
                        if (cooldowns![col] <= 0)
                        {
                            heads[col] += speeds![col];
                            cooldowns[col] = speeds[col] > 1 ? 0 : 1;
                        }
                        else
                        {
                            cooldowns[col]--;
                        }

                        if (heads[col] - trailLength >= height)
                            heads[col] = -1;
                    }
                    else if (rng.NextDouble() < spawnChance)
                    {
                        heads[col] = 0;
                        speeds![col] = rng.Next(1, 3);
                        cooldowns![col] = 0;
                        for (int y = 0; y < height; y++)
                            chars![y, col] = glyphs[rng.Next(glyphs.Length)];
                    }

                    if (heads![col] < 0) continue;

                    for (int i = 0; i < trailLength; i++)
                    {
                        int row = heads[col] - i;
                        if (row < 0 || row >= height) continue;

                        if (i < 3 && rng.NextDouble() < 0.3)
                            chars![row, col] = glyphs[rng.Next(glyphs.Length)];

                        char ch = chars![row, col];
                        Color fg;
                        if (i == 0)
                        {
                            fg = new Color(200, 255, 200); // bright head
                        }
                        else
                        {
                            double fade = 1.0 - (double)i / trailLength;
                            fg = new Color((byte)(40 * fade), (byte)(200 * fade), 0);
                        }

                        buffer.SetCell(col, row, new Cell(ch, fg, Color.Black));
                    }
                }
            },
        };
    }
}
