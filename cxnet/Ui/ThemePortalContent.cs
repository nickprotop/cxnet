using Rectangle = System.Drawing.Rectangle;

using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;

namespace Cxnet.Ui;

/// <summary>
/// The bordered content of the theme <see cref="ThemePortal"/>. Hosts the theme <see cref="ListControl"/>
/// as its child and draws a rounded border around it. Subclassing <see cref="PortalContentBase"/> is the
/// framework's supported way to give a desktop portal bordered, laid-out content: its <c>PaintDOM</c>
/// measures the hosted <see cref="PortalContentBase.Content"/> with tight constraints so the child fills
/// the border-shrunk area — a plain container inside a portal collapses its child instead.
/// </summary>
internal sealed class ThemePortalContent : PortalContentBase
{
    private readonly Rectangle _bounds;

    public ThemePortalContent(ListControl list, Rectangle bounds, Color border, Color background)
    {
        _bounds = bounds;
        BorderStyle = BoxChars.Rounded;
        BorderColor = border;
        BorderBackgroundColor = background;
        Content = list; // hosted + painted by PortalContentBase.PaintDOM into the inner (bordered) rect
    }

    public override Rectangle GetPortalBounds() => _bounds;

    // Mouse is dispatched to the hosted child by the base class; nothing extra to do here.
    public override bool ProcessMouseEvent(MouseEventArgs args) => false;

    // Content is hosted, so the base PaintDOM paints the child directly and never calls this.
    protected override void PaintPortalContent(CharacterBuffer buffer, LayoutRect bounds,
        LayoutRect clipRect, Color defaultFg, Color defaultBg)
    { }
}
