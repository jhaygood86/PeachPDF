using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// The immutable output of layout: one piece of laid-out content positioned inside exactly one
    /// fragmentainer (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">CSS Fragmentation
    /// Level 3 §2</see>). A fragment owns <b>geometry only</b> — style is read through the originating
    /// <see cref="BoxFragment.Box"/>, so fragments stay cheap and style keeps a single home.
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
    /// What one decoration rectangle needs in order to honour <c>box-decoration-break</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>): the
    /// unbroken box this rectangle is a slice of, this fragment's own box, and which of its four edges
    /// are real box edges rather than fragmentation breaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><paramref name="UnbrokenStrip"/> is per-rectangle, not shared.</b> §6.2's <c>slice</c> means
    /// "rendered with no breaks present, then sliced by the breaks" — and for an inline box broken across
    /// lines, the no-breaks rendering is the box on one infinitely long line. Each rectangle therefore
    /// carries that same strip <i>as seen from its own position</i>: same width, different X. With
    /// <c>strip.X = rect.X - (width of the rectangles before this one)</c>, the strip's left edge
    /// coincides with the first rectangle's left and its right edge with the last rectangle's right, so
    /// resolving a decoration against the strip and clipping it to the rectangle produces rounded corners
    /// at the true start and end only, square edges at every break, and a background that continues across
    /// lines — with no per-corner special-casing. Same shape as WebKit's
    /// <c>InlineFlowBox::paintFillLayer</c>.
    /// </para>
    /// <para>
    /// All four edge flags are <b>physical</b>, and every consumer is physical too — the left border edge,
    /// the left padding inset on an underline. The inline pair is derived from the two rectangles, so it
    /// cannot disagree with the strip and it stays correct when a right-to-left line reverses the inline
    /// progression. The block pair cannot be derived that way and is read from the break record instead;
    /// see <see cref="SliceGeometry.HasTopEdge"/>.
    /// </para>
    /// </remarks>
    /// <param name="UnbrokenStrip">
    /// the whole unbroken box as seen from this rectangle — what every <c>slice</c> decoration resolves
    /// against. Equal to <see cref="Fragment.Rect"/> for a box that is not broken.
    /// </param>
    /// <param name="FragmentRect">
    /// this rectangle cut to its fragmentainer's content band — what every <c>clone</c> decoration
    /// resolves against, so a box crossing a page boundary gets a closed border at the break rather than
    /// one the page clip cuts away. Equal to <see cref="Fragment.Rect"/> when the rectangle fits its band.
    /// </param>
    /// <param name="HasLeftEdge">whether this rectangle's left edge is the box's own, not a break</param>
    /// <param name="HasRightEdge">whether this rectangle's right edge is the box's own, not a break</param>
    /// <param name="HasTopEdge">
    /// whether this rectangle's top edge is the box's own, not a break. <b>Not</b> derived from geometry,
    /// unlike the inline pair: two fragments of one box in two columns of a page occupy the <i>same</i>
    /// block-axis range, so comparing rectangles cannot tell the box's own top from a column boundary. The
    /// honest source is the break record — which boxes layout said carry on past a fragmentainer — and that
    /// is what this reports.
    /// </param>
    /// <param name="HasBottomEdge">whether this rectangle's bottom edge is the box's own, not a break</param>
    internal sealed record SliceGeometry(
        RRect UnbrokenStrip,
        RRect FragmentRect,
        bool HasLeftEdge,
        bool HasRightEdge,
        bool HasTopEdge = true,
        bool HasBottomEdge = true);

    /// <summary>
    /// One line's worth of a box's own decoration area — the fragmentainer-local equivalent of a
    /// single <see cref="CssBox.Rectangles"/> entry, which is what backgrounds, borders, and text
    /// decoration are painted over. A block-level box with no line boxes of its own gets exactly one
    /// <see cref="LineFragment"/> covering its border box, with a null <see cref="Line"/> — the
    /// fragment-tree form of paint's historical "<c>Rectangles.Count == 0</c> ⇒ use
    /// <see cref="CssBox.Bounds"/>" fallback.
    /// </summary>
    /// <param name="Rect">the decoration rectangle, in fragmentainer-local coordinates</param>
    /// <param name="Line">the line box this rectangle belongs to, or null for a whole-box rectangle</param>
    /// <param name="Slice">
    /// how this rectangle relates to the unbroken box it came from — the input to
    /// <c>box-decoration-break</c> (§6.2). Only the builder can compute it: it needs every rectangle the
    /// box produced across every fragmentainer, and paint only ever sees one fragmentainer's survivors.
    /// </param>
    internal sealed record LineFragment(RRect Rect, CssLineBox? Line, SliceGeometry Slice) : Fragment(Rect);

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
    /// nesting text strictly inside line boxes inside the box. PeachPDF's inline model keeps a box's words and
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
    /// whether this is the box's first fragment — informational, and <b>not</b> the edges a
    /// <c>box-decoration-break</c> value applies at. The span behind it is unioned with every
    /// descendant's, so a box whose own geometry begins on page 2 still reports
    /// <c>IsFirstFragment</c> on page 1 when a descendant lands there. Those edges live on
    /// <see cref="LineFragment.Slice"/>, per rectangle, where they are derived from geometry that cannot
    /// disagree with what is painted.
    /// </param>
    /// <param name="IsLastFragment">whether this is the box's last fragment; see <paramref name="IsFirstFragment"/></param>
    /// <param name="IsMonolithic">
    /// whether the originating box is monolithic content —
    /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">§2</see>'s own set, a replaced element or
    /// a scroll container, as decided by
    /// <see cref="Fragmentation.MonolithicContent.IsMonolithic"/>. A property of the box rather than of
    /// this fragment, so every fragment of one box agrees. Recorded here because a consumer reading the
    /// tree — paint, or a later phase deciding where a break may fall — should not have to re-derive from
    /// <see cref="Box"/> a fact layout has already settled.
    /// </param>
    /// <param name="Lines">this fragment's own decoration rectangles</param>
    /// <param name="Words">the words of this box that fall in this fragmentainer</param>
    /// <param name="Children">the fragments of this box's child boxes in this fragmentainer</param>
    /// <param name="OverflowClip">
    /// the padding-edge rectangle of the nearest <c>overflow: hidden</c> box on this box's
    /// containing-block chain, already in this fragmentainer's local space — or null when no ancestor
    /// clips. Resolved by the builder rather than looked up from the boxes at paint time, because a box
    /// shown at several places at once (a repeated table header's shared source subtree) holds only the
    /// last position layout gave it, and the builder is the only thing that still knows which of them
    /// this fragment came from.
    /// </param>
    internal sealed record BoxFragment(
        RRect Rect,
        CssBox Box,
        int FragmentainerIndex,
        double OriginY,
        RRect WholeBoxRect,
        bool IsFixed,
        bool IsFirstFragment,
        bool IsLastFragment,
        bool IsMonolithic,
        IReadOnlyList<LineFragment> Lines,
        IReadOnlyList<TextFragment> Words,
        IReadOnlyList<BoxFragment> Children,
        RRect? OverflowClip) : Fragment(Rect)
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
    /// flows into. Multi-column columns are fragmentainers too, and content genuinely splits across
    /// them — but a column is not a <i>page</i>, so its fragments are ordinary
    /// <see cref="BoxFragment"/>s inside the page that holds them, each carrying the geometry the
    /// column it was filled in gave it.
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
