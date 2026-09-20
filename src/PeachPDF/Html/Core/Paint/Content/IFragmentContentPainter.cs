using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Fragments;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints the content of one kind of box that the generic box paint
    /// (<see cref="FragmentPainter.PaintBoxContent"/>) cannot express — a replaced element, a list
    /// marker, a repeated table header. Selected by box type; see
    /// <see cref="FragmentContentPainters.For"/>. "Cannot express" is the whole bar: an
    /// <c>&lt;hr&gt;</c> had one of these for years purely to draw its own border by hand, which is
    /// how it came to be the one box in the document that ignored <c>border-style</c> (issue #1225).
    /// </summary>
    /// <remarks>
    /// This is the extension seam replaced-element boxes used to provide by overriding a virtual paint
    /// method on themselves. Adding a new kind of replaced element means adding an implementation here
    /// and one arm to <see cref="FragmentContentPainters.For"/> — not new paint code on
    /// <see cref="Dom.CssBox"/>.
    /// </remarks>
    internal interface IFragmentContentPainter
    {
        /// <summary>
        /// Paints <paramref name="fragment"/>'s content. All geometry comes from the fragment; the box
        /// reached through it supplies style and its already-resolved content.
        /// </summary>
        /// <param name="painter">the page's painter, for the shared decoration and child-walk primitives</param>
        /// <param name="g">the device to draw to</param>
        /// <param name="fragment">the fragment being painted</param>
        void Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment);
    }
}
