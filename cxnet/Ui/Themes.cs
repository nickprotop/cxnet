using SharpConsoleUI;
using SharpConsoleUI.Themes;

namespace Cxnet.Ui;

/// <summary>
/// cxnet's palette themes. Each theme is derived by the palette generator from two seed
/// colors (a Primary accent + a Background surface); everything else — borders, selection,
/// status colors, contrast text — is generated. The <c>t</c> key opens the translucent
/// <see cref="ThemePicker"/> to switch between them live over the running waveforms.
/// </summary>
internal static class Themes
{
    // Name → (Primary accent, Background surface). Distinct, cxnet-flavored palettes;
    // the palette generator derives the full theme from just these two seeds.
    private static readonly (string Name, string Description, string Primary, string Background)[] Palettes =
    {
        ("Aqua",   "Cyan-teal accent on deep sea",     "#2DD4BF", "#0B1F2A"),
        ("Ember",  "Warm ember orange on charcoal",    "#FB923C", "#1C1410"),
        ("Forest", "Fresh green on forest floor",      "#4ADE80", "#0E1A12"),
        ("Slate",  "Cool steel-blue on slate",         "#60A5FA", "#0F1620"),
        ("Grape",  "Vivid violet on deep purple",      "#A78BFA", "#160E24"),
        ("Rose",   "Soft rose-pink on wine",           "#FB7185", "#210E16"),
        ("Gold",   "Amber gold on espresso",           "#FBBF24", "#1A1508"),
        ("Mono",   "Neutral grey monochrome",          "#94A3B8", "#12161C"),
    };

    /// <summary>
    /// Registers every cxnet palette theme in the system's theme registry. Idempotent:
    /// re-registration is skipped so it is safe to call more than once.
    /// </summary>
    public static void RegisterThemes(ConsoleWindowSystem ws)
    {
        var existing = ws.ThemeRegistryService.GetAvailableThemeNames();
        foreach (var (name, description, primary, background) in Palettes)
        {
            if (existing.Contains(name))
                continue;

            // Capture locals for the factory closure.
            string p = primary;
            string b = background;
            ws.ThemeRegistryService.RegisterTheme(
                name,
                description,
                () => Theme.FromPalette(new Palette
                {
                    Primary = Color.FromHex(p),
                    Background = Color.FromHex(b),
                }));
        }
    }
}
