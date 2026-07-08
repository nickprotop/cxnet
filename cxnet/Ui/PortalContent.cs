using Rectangle = System.Drawing.Rectangle;

using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;

namespace Cxnet.Ui;

/// <summary>
/// Hosts an arbitrary control as bordered portal content. Wraps the supplied
/// <see cref="IWindowControl"/> as its child and draws a rounded border around it. Subclassing
/// <see cref="PortalContentBase"/> is the framework's supported way to give a desktop portal bordered,
/// laid-out content: its <c>PaintDOM</c> measures the hosted <see cref="PortalContentBase.Content"/>
/// with tight constraints so the child fills the border-shrunk area — a plain container inside a portal
/// collapses its child instead.
/// </summary>
/// <remarks>
/// Implements <see cref="IInteractiveControl"/> so the framework routes keys here while the portal is
/// open. A key is first offered to the <c>shortcutHandler</c> (so cxnet's global shortcuts still work
/// with a portal open — the same key toggles this portal closed, a different one switches portals);
/// if it declines, the key is forwarded to the hosted content for normal navigation.
/// </remarks>
internal sealed class PortalContent : PortalContentBase, IInteractiveControl
{
    private readonly Rectangle _bounds;
    private readonly Func<ConsoleKeyInfo, bool>? _shortcutHandler;

    public PortalContent(IWindowControl content, Rectangle bounds, Color border, Color background,
        Func<ConsoleKeyInfo, bool>? shortcutHandler = null)
    {
        _bounds = bounds;
        _shortcutHandler = shortcutHandler;
        BorderStyle = BoxChars.Rounded;
        BorderColor = border;
        BorderBackgroundColor = background;
        Content = content; // hosted + painted by PortalContentBase.PaintDOM into the inner (bordered) rect
    }

    public override Rectangle GetPortalBounds() => _bounds;

    // Mouse is dispatched to the hosted child by the base class; nothing extra to do here.
    public override bool ProcessMouseEvent(MouseEventArgs args) => false;

    // Content is hosted, so the base PaintDOM paints the child directly and never calls this.
    protected override void PaintPortalContent(CharacterBuffer buffer, LayoutRect bounds,
        LayoutRect clipRect, Color defaultFg, Color defaultBg)
    { }

    public bool IsEnabled { get; set; } = true;

    public bool ProcessKey(ConsoleKeyInfo key)
    {
        // Offer the key to cxnet's shortcut router first (toggle/switch portals while one is open).
        if (_shortcutHandler is not null && _shortcutHandler(key))
            return true;

        // Otherwise let the hosted control (list/table) handle navigation.
        return Content is IInteractiveControl interactive && interactive.ProcessKey(key);
    }
}
