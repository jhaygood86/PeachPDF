using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// The immutable output of layout: one piece of laid-out content positioned inside exactly one
    /// fragmentainer (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">CSS Fragmentation
    /// Level 3 §2</see>). A fragment owns <b>geometry only</b> — style is read through the originating
    /// <see cref="BoxFragment.Box"/>, mirroring Chromium LayoutNG's
    /// <c>NGPhysicalFragment</c> → <c>LayoutObject</c> back-pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Coordinate space.</b> Every fragment rectangle is <i>fragmentainer-local</i>:
    /// <c>local.Y = documentY - <see cref="FragmentainerFragment.LocalOriginY"/></c>, with X unchanged
    /// (a page's horizontal margin offset is applied by the painter's own page translate, not by
    /// layout). A <see cref="BoxFragment.IsFixed"/> fragment subtracts nothing, so it lands at the
    /// same place on every page.
    /// </para>
    /// <para>
    /// The fragment tree is built once, at the end of <see cref="HtmlContainerInt.PerformLayout"/>,
    /// and is never mutated afterwards. Paint consumes it and must not read geometry off
    /// <see cref="CssBox"/>.
    /// </para>
    /// </remarks>
    /// <param name="Rect">the fragment's own rectangle, in fragmentainer-local coordinates</param>
    internal abstract record Fragment(RRect Rect);

    /// <summary>
    /// One line's worth of a box's own decoration area — the fragmentainer-local equivalent of a
    /// single <see cref="CssBox.Rectangles"/> entry, which is what backgrounds, borders, and text
    /// decoration are painted over. A block-level box with no line boxes of its own gets exactly one
    /// <see cref="LineFragment"/> covering its border box, with a null <see cref="Line"/> — the
    /// fragment-tree form of paint's historical "<c>Rectangles.Count == 0</c> ⇒ use
    /// <see cref="CssBoxProperties.Bounds"/>" fallback.
    /// </summary>
    /// <param name="Rect">the decoration rectangle, in fragmentainer-local coordinates</param>
    /// <param name="Line">the line box this rectangle belongs to, or null for a whole-box rectangle</param>
    internal sealed record LineFragment(RRect Rect, CssLineBox? Line) : Fragment(Rect);

    /// <summary>
    /// One laid-out word (or inline replaced run) positioned inside a fragmentainer. Words are
    /// monolithic — a word never straddles a fragmentainer boundary (see <see cref="CssRect.BreakPage"/>),
    /// so each <see cref="CssRect"/> produces exactly one <see cref="TextFragment"/>.
    /// </summary>
    /// <param name="Rect">the word's rectangle, in fragmentainer-local coordinates</param>
    /// <param name="Word">the word this fragment positions; the source of its text, font, and style</param>
    internal sealed record TextFragment(RRect Rect, CssRect Word) : Fragment(Rect);

    /// <summary>
    /// The portion of one <see cref="CssBox"/> that lives in one fragmentainer — the "box fragment"
    /// of <see href="https://www.w3.org/TR/css-break-3/#box-fragment">CSS Fragmentation Level 3 §2</see>.
    /// A box that spans a page boundary produces one <see cref="BoxFragment"/> per page it appears on.
    /// </summary>
    /// <remarks>
    /// The three child collections deliberately mirror what <c>CssBox.PaintImpCore</c> paints — its own
    /// decoration rectangles, then its own words, then its stacking-ordered child boxes — rather than
    /// LayoutNG's strict box → line-box → text nesting. PeachPDF's inline model keeps a box's words and
    /// its per-line decoration rectangles in two parallel collections
    /// (<see cref="CssBox.Words"/> / <see cref="CssBox.Rectangles"/>), and flattening that into a
    /// nested line-box tree would be a separate change with no consumer today.
    /// </remarks>
    /// <param name="Rect">
    /// the union of this fragment's own painted geometry (its <paramref name="Lines"/>, else its
    /// <paramref name="Children"/>), in fragmentainer-local coordinates
    /// </param>
    /// <param name="Box">the originating box — the source of all style and paint-handler dispatch</param>
    /// <param name="FragmentainerIndex">the pagination slot index this fragment belongs to</param>
    /// <param name="OriginY">
    /// the document Y that was subtracted to place this fragment in its fragmentainer's local space —
    /// <see cref="FragmentainerFragment.LocalOriginY"/>, or 0 for a fixed fragment. Paint needs it for
    /// the handful of coordinates layout still records in document space (a multi-column container's
    /// rule segments, a paginated table's per-page break bottoms) and to map an ancestor's
    /// <c>overflow</c> clip into this fragment's space.
    /// </param>
    /// <param name="WholeBoxRect">
    /// the box's <i>unfragmented</i> border box mapped into this fragmentainer's local space. Effects
    /// that are defined against the whole box rather than one fragment — the <c>transform</c> pivot and
    /// the <c>clip-path</c> reference box — resolve against this, preserving pre-fragment-tree
    /// behaviour. Per-fragment transform origins are deliberately out of scope here.
    /// </param>
    /// <param name="IsFixed">
    /// whether the box is <c>position: fixed</c> (or descends from one). A fixed fragment is emitted in
    /// every fragmentainer at identical coordinates — the explicit form of the repeat-on-every-page
    /// behaviour that previously fell out of paint suppressing the page scroll offset.
    /// </param>
    /// <param name="IsFirstFragment">
    /// whether this is the box's first fragment. Together with <paramref name="IsLastFragment"/> this
    /// identifies the edges a <c>box-decoration-break</c> value applies at
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see>).
    /// </param>
    /// <param name="IsLastFragment">whether this is the box's last fragment</param>
    /// <param name="Lines">this fragment's own decoration rectangles</param>
    /// <param name="Words">the words of this box that fall in this fragmentainer</param>
    /// <param name="Children">the fragments of this box's child boxes in this fragmentainer</param>
    internal sealed record BoxFragment(
        RRect Rect,
        CssBox Box,
        int FragmentainerIndex,
        double OriginY,
        RRect WholeBoxRect,
        bool IsFixed,
        bool IsFirstFragment,
        bool IsLastFragment,
        IReadOnlyList<LineFragment> Lines,
        IReadOnlyList<TextFragment> Words,
        IReadOnlyList<BoxFragment> Children) : Fragment(Rect)
    {
        /// <summary>
        /// This fragment's principal decoration rectangle — the one a replaced element (an image, an
        /// inline SVG, an <c>&lt;iframe&gt;</c>) paints its background and border over. Falls back to
        /// <see cref="Fragment.Rect"/> for a fragment that exists only to carry descendants.
        /// </summary>
        internal RRect PrimaryRect => Lines.Count > 0 ? Lines[0].Rect : Rect;

        /// <summary>
        /// This fragment's rectangle for <paramref name="word"/>. Replaced boxes hold a single word
        /// whose rectangle — not the box's own — positions the replaced content.
        /// </summary>
        internal bool TryGetWordRect(CssRect word, out RRect rect)
        {
            foreach (var candidate in Words)
            {
                if (ReferenceEquals(candidate.Word, word))
                {
                    rect = candidate.Rect;
                    return true;
                }
            }

            rect = RRect.Empty;
            return false;
        }
    }

    /// <summary>
    /// One fragmentainer — for PeachPDF, one materialized PDF page. Per
    /// <see href="https://www.w3.org/TR/css-break-3/#fragmentainer">CSS Fragmentation Level 3 §2</see>
    /// a fragmentainer is not a DOM element; it is a slot in a fragmentation context that content
    /// flows into. Multi-column columns are also fragmentainers per spec, but PeachPDF only models
    /// pages here — its columns engine still re-bands whole children itself.
    /// </summary>
    /// <param name="Rect">the fragmentainer's own content band, in its own local coordinates</param>
    /// <param name="SlotIndex">
    /// the pagination-slot index this fragmentainer occupies. Slot indices are <i>not</i> contiguous
    /// across <see cref="FragmentTree.Fragmentainers"/>: a slot containing no printable content is
    /// never materialized (CSS Paged Media Level 3 §3.2).
    /// </param>
    /// <param name="Geometry">the slot's resolved band and page margins, shared with layout</param>
    /// <param name="LocalOriginY">
    /// the document Y that maps to this fragmentainer's local origin —
    /// <c>PageTopOf(SlotIndex) - MarginTop</c>. Subtracted from every non-fixed fragment's document
    /// coordinates when the tree is built.
    /// </param>
    /// <param name="Root">the root box's fragment in this fragmentainer</param>
    internal sealed record FragmentainerFragment(
        RRect Rect,
        int SlotIndex,
        PageBandGeometry Geometry,
        double LocalOriginY,
        BoxFragment Root) : Fragment(Rect);

    /// <summary>
    /// The complete immutable result of laying out one document: its fragmentainers, in page order.
    /// </summary>
    /// <param name="Fragmentainers">the fragmentainers that carry printable content, in page order</param>
    internal sealed record FragmentTree(IReadOnlyList<FragmentainerFragment> Fragmentainers);
}
