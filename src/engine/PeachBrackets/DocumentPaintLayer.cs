namespace PeachBrackets;

/// <summary>
/// Which part of a page a paint call draws. A host that scrolls a surface natively (a scroller moved by the compositor) cannot keep a
/// <c>position: fixed</c> or stuck <c>position: sticky</c> box still by repainting it afterwards: it would move with the content first and snap back a
/// frame later. It draws those boxes in a layer that does not scroll instead, with <see cref="Content"/> on the scrolled surface and
/// <see cref="ViewportAttached"/> on the layer over it. A host that plays animations on a screen adds a third, <see cref="Animated"/>, so a box that
/// only moves or fades is composited each frame over a surface that does not have to be painted again.
/// </summary>
public enum DocumentPaintLayer
{
    /// <summary>Everything, in one pass (the default, and all a document that is not scrolled needs).</summary>
    All = 0,

    /// <summary>Everything except the boxes attached to the viewport (see <see cref="ViewportAttached"/>).</summary>
    Content = 1,

    /// <summary>
    /// Only the boxes attached to the viewport of a <see cref="MediaType.Screen"/> document: every <c>position: fixed</c> box, and a
    /// <c>position: sticky</c> box that <see cref="HtmlDocument.SetViewport"/> has moved. They are drawn where they are on the surface, so a layer
    /// that does not scroll draws them under the translation that puts the viewport's top-left at its own. Nothing is drawn for a paged document.
    /// </summary>
    ViewportAttached = 2,

    /// <summary>
    /// Only the boxes whose <c>transform</c> or <c>opacity</c> is being animated that can be drawn apart from the rest of the page (see
    /// <see cref="PeachBrackets.Updates.AnimationFrame.OverlayChanged"/>), each with everything under it. With <see cref="HtmlDocumentOptions.AnimationsEnabled"/>,
    /// <see cref="Content"/> leaves those boxes out, so a frame that only moves or fades them repaints nothing of the scrolled surface: the host draws
    /// this layer over it every frame (under <see cref="ViewportAttached"/>), where each box is a single textured quad when the canvas keeps
    /// <see cref="PeachDrawing.Core.RetainedLayer"/>s. Boxes that cannot be drawn apart without changing what is on top of what (a fixed or sticky box that must sit under them,
    /// in-flow content over a negative z-index, a group that blends or reads its backdrop) stay in <see cref="Content"/> and are repainted as before. Nothing is drawn for a
    /// document that is not <see cref="MediaType.Screen"/>, or when no animation is running; <see cref="All"/> always draws every box in place.
    /// </summary>
    Animated = 3,
}
