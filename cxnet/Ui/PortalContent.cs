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
/// open; they are forwarded to the hosted content (list/table) for navigation and type-ahead. Any key
/// the content does not consume falls through — via the framework's portal-key handling — to the
/// global shortcut processor, so cxnet's global shortcuts still work with a portal open.
/// </remarks>
internal sealed class PortalContent : PortalContentBase, IInteractiveControl
{
    private readonly Rectangle _bounds;

    public PortalContent(IWindowControl content, Rectangle bounds, Color border, Color background)
    {
        _bounds = bounds;
        BorderStyle = BoxChars.Rounded;
        BorderColor = border;
        BorderBackgroundColor = background;
        Content = content; // hosted + painted by PortalContentBase.PaintDOM into the inner (bordered) rect

        // Give the hosted control portal focus so its ProcessKey runs (a focusable control ignores keys
        // unless HasFocus) — this is what makes arrow nav AND Enter/activation work inside the portal.
        if (content is IFocusableControl focusable)
            PortalFocusedControl = focusable;
    }

    public override Rectangle GetPortalBounds() => _bounds;

    // Forward mouse events to the hosted child (border-offset applied by the base) so clicking a
    // list/table row inside the portal works — the base does NOT auto-forward.
    public override bool ProcessMouseEvent(MouseEventArgs args) => ProcessHostedMouseEvent(args);

    // Content is hosted, so the base PaintDOM paints the child directly and never calls this.
    protected override void PaintPortalContent(CharacterBuffer buffer, LayoutRect bounds,
        LayoutRect clipRect, Color defaultFg, Color defaultBg)
    { }

    public bool IsEnabled { get; set; } = true;

    // Forward keys to the hosted control (list/table) for nav and type-ahead. Keys it does not consume
    // return false, so the framework's portal-key handling falls through to the global shortcuts.
    public bool ProcessKey(ConsoleKeyInfo key) =>
        Content is IInteractiveControl interactive && interactive.ProcessKey(key);
}
