using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cxnet.Ui;

/// <summary>
/// A translucent floating overlay listing the registered cxnet themes. Each row shows two
/// colour swatches (the theme's primary/secondary) followed by its name, and its background
/// uses an alpha &lt; 255 so the live waveforms beneath composite through it — the alpha-blending
/// showcase. Arrowing the highlight <em>live-previews</em> the theme across the whole monitor;
/// Enter/click commits (keeps the previewed theme) and closes; Esc reverts to the theme that was
/// active when the overlay opened. Non-modal so the graphs keep animating.
/// </summary>
internal static class ThemePicker
{
    private const string OverlayName = "themepicker";

    /// <summary>Extra rows added to the theme count for the window's chrome/border/padding.</summary>
    private const int WindowChromeRows = 4;

    /// <summary>Columns for the two swatches (`██ ██ `) plus border/margin/padding breathing room.</summary>
    private const int RowChromeCols = 10;

    /// <summary>Minimum overlay width so short theme names still read as a panel.</summary>
    private const int MinWidth = 26;

    /// <summary>Formats a colour as an uppercase `#RRGGBB` hex string for markup.</summary>

    /// <summary>
    /// Opens the theme picker overlay. If one is already open it is closed instead (toggle),
    /// so pressing the key again dismisses it.
    /// </summary>
    public static void Show(ConsoleWindowSystem ws)
    {
        var existing = ws.WindowStateService.FindWindowByName(OverlayName);
        if (existing is not null)
        {
            ws.CloseWindow(existing);
            return;
        }

        var names = ws.ThemeRegistryService.GetAvailableThemeNames();

        // Capture the theme active on open so Esc can revert after live previewing.
        string? original = ws.ThemeStateService.CurrentTheme?.Name;
        var current = ws.ThemeStateService.CurrentTheme?.Name;

        var listBuilder = Controls.List("Themes")
            .WithName("themelist")
            .Selectable()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithMargin(1, 0, 1, 0);

        var fallbackSwatch = new Color(150, 150, 150);
        foreach (var name in names)
        {
            var t = ws.ThemeRegistryService.GetTheme(name);
            string primary = Palette.ToHex(t?.PrimaryColor ?? fallbackSwatch);
            string secondary = Palette.ToHex(t?.SecondaryColor ?? fallbackSwatch);
            string row = $"[{primary}]██[/][{secondary}]██[/] {name}";
            listBuilder.AddItem(row, tag: name);
        }

        var list = listBuilder.Build();

        // Highlight the currently-active theme FIRST, then subscribe — so setting the initial
        // selection below does not fire a spurious live preview.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == current)
            {
                list.SelectedIndex = i;
                break;
            }
        }

        // Live-preview: moving the highlight (arrow keys) recolours the whole monitor.
        list.SelectedItemChanged += (_, item) =>
        {
            if (item?.Tag is string n)
                ws.ThemeStateService.SwitchTheme(n);
        };

        Window? window = null;

        // Enter/click commits: the previewed theme is already applied, so just close.
        list.ItemActivated += (_, _) =>
        {
            if (window is not null)
                ws.CloseWindow(window);
        };

        // Size to content: height ≈ theme count + chrome; width fits `██ ██ ` + the longest name.
        int longestName = 0;
        foreach (var name in names)
            longestName = Math.Max(longestName, name.Length);
        int width = Math.Max(MinWidth, longestName + RowChromeCols);
        int height = names.Count + WindowChromeRows;

        // Panel background from the theme's translucent surface, a touch more opaque (205) than the
        // main window so it reads as a floating panel while the waveforms still show faintly through.
        var surface = Palette.Current(ws).Surface;
        var overlayBackground = new Color(surface.R, surface.G, surface.B, 205);

        window = new WindowBuilder(ws)
            .WithTitle("Themes")
            .WithName(OverlayName)
            .WithSize(width, height)
            .Centered()
            .WithBorderStyle(BorderStyle.Rounded)
            .WithPadding(1, 0)
            .WithBackgroundColor(overlayBackground)
            .AddControl(list)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
                    // Esc reverts to the theme active when the overlay opened.
                    if (original != null)
                        ws.ThemeStateService.SwitchTheme(original);
                    if (window is not null)
                        ws.CloseWindow(window);
                    e.Handled = true;
                }
            })
            .Build();

        ws.AddWindow(window);
        ws.SetActiveWindow(window);
    }
}
