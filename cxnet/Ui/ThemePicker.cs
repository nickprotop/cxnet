using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;

namespace Cxnet.Ui;

/// <summary>
/// A translucent floating overlay listing the registered cxnet themes. Its background uses
/// an alpha &lt; 255 so the live waveforms beneath composite through it — the alpha-blending
/// showcase. Selecting a theme (Enter/click) calls
/// <see cref="SharpConsoleUI.Core.ThemeStateService.SwitchTheme(string)"/> and closes the
/// overlay; Esc closes without changing the theme. Non-modal so the graphs keep animating.
/// </summary>
internal static class ThemePicker
{
    private const string OverlayName = "themepicker";

    // Deep-navy surface at alpha 190 (< 255) → the waveforms show faintly through.
    private static readonly Color OverlayBackground = new Color(12, 18, 30, 190);

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
        var current = ws.ThemeStateService.CurrentTheme?.Name;

        var listBuilder = Controls.List("Themes")
            .WithName("themelist")
            .Selectable()
            .WithVerticalAlignment(SharpConsoleUI.Layout.VerticalAlignment.Fill)
            .WithMargin(1, 0, 1, 0);

        foreach (var name in names)
            listBuilder.AddItem(name, tag: name);

        var list = listBuilder.Build();

        // Highlight the currently-active theme.
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] == current)
            {
                list.SelectedIndex = i;
                break;
            }
        }

        Window? window = null;

        // Selecting an item switches the theme immediately and closes the overlay.
        list.ItemActivated += (_, item) =>
        {
            if (item?.Tag is string themeName)
                ws.ThemeStateService.SwitchTheme(themeName);
            if (window is not null)
                ws.CloseWindow(window);
        };

        window = new WindowBuilder(ws)
            .WithTitle("Themes")
            .WithName(OverlayName)
            .WithSize(34, 14)
            .Centered()
            .WithBackgroundColor(OverlayBackground)
            .AddControl(list)
            .OnKeyPressed((_, e) =>
            {
                if (e.KeyInfo.Key == ConsoleKey.Escape)
                {
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
