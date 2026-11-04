namespace PeachBrackets;

/// <summary>
/// Which part of a page a paint call draws. A host that scrolls a surface natively (a scroller moved by the compositor) cannot keep a
/// <c>position: fixed</c> or stuck <c>position: sticky</c> box still by repainting it afterwards: it would move with the content first and snap back a
/// frame later. It draws those boxes in a layer that does not scroll instead, with <see cref="Content"/> on the scrolled surface and
/// <see cref="ViewportAttached"/> on the layer over it.
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
}
