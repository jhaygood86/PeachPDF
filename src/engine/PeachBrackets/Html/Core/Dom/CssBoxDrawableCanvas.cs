namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// A <c>canvas</c> whose children are drawable, as the probe document lays it out: a box that holds its children (so they have layout) at the size of its bitmap. A picture of something
    /// that has such a canvas inside it shows what a script drew on the canvas, not its children (see <c>DrawableCanvasFragmentPainter</c>).
    /// </summary>
    internal sealed class CssBoxDrawableCanvas : CssBox
    {
        public CssBoxDrawableCanvas(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
        }

        /// <summary>What a script drew on this canvas, or null.</summary>
        internal ScriptHosting.ScriptCanvasPainting? Painting =>
            HtmlTag is { SourceOrdinal: >= 0 } tag && HtmlContainer is { } container && container.CanvasPaintings.TryGetValue(tag.SourceOrdinal, out var painting) ? painting : null;
    }
}
