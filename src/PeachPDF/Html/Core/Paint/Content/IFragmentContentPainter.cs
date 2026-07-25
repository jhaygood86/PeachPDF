using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Fragments;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Paint.Content
{
    /// <summary>
    /// Paints the content of one kind of box that the generic box paint
    /// (<see cref="FragmentPainter.PaintBoxContent"/>) cannot express — a replaced element, a list
    /// marker, an <c>&lt;hr&gt;</c>, a repeated table header. Selected by box type; see
    /// <see cref="FragmentContentPainters.For"/>.
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
        ValueTask Paint(FragmentPainter painter, RGraphics g, BoxFragment fragment);
    }
}
