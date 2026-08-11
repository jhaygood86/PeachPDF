using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Classifies content that cannot be broken, per
    /// <see href="https://www.w3.org/TR/css-break-3/#monolithic">CSS Fragmentation Level 3 §2</see>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One home for every question layout asks about breakability, in the same shape as
    /// <see cref="BreakValues"/> — and for the same reason. Before this existed, "monolithic" was spelled
    /// five different ways in five places, and the one spelling that was explicit — a
    /// <c>CssBox.LayoutMonolithicContent</c> that detached the fragmentainer around an engine's whole run,
    /// since deleted — encoded a different rule from the spec's.
    /// </para>
    /// <para>
    /// <b>The two questions below are not the same question, and keeping them apart is the point of this
    /// file.</b> <see cref="IsMonolithic"/> is §2's own set: a property of the <i>content</i>, which no
    /// user agent may break. <see cref="PaginatesItsOwnContent"/> is a PeachPDF implementation constraint:
    /// four layout engines fragment their own subtrees, so the driver must not hand them a half-laid-out
    /// one. Conflating them is what made the second look like a spec claim.
    /// </para>
    /// </remarks>
    internal static class MonolithicContent
    {
        // ── css-break-3 §2's own set ──────────────────────────────────────────

        /// <summary>
        /// Whether §2 forbids breaking inside <paramref name="box"/>.
        /// </summary>
        internal static bool IsMonolithic(CssBox box) => IsReplaced(box) || IsScrollContainer(box);

        /// <summary>
        /// Whether <paramref name="box"/> is a replaced element, whose content the UA cannot fragment
        /// because it has no fragmentable inner structure.
        /// </summary>
        /// <remarks>
        /// Discriminated by box type, the same way <c>FragmentContentPainters.For</c> selects the painter
        /// that draws these. Only <c>&lt;object&gt;</c> can answer no — it is replaced only once its
        /// <c>data</c> resource resolves to something renderable, which measurement decides.
        /// <c>&lt;hr&gt;</c> and a list marker have their own content painters but are not replaced
        /// elements, and they are never tall enough for the distinction to matter anyway.
        /// <c>CssBoxFormField</c> (<c>&lt;input&gt;</c>/<c>&lt;select&gt;</c>) is not a replaced
        /// element per spec either, but is included here anyway: an AcroForm widget annotation names
        /// exactly one page rect, so a form field must never fragment across a page break regardless
        /// of what §2 itself would otherwise allow.
        /// </remarks>
        internal static bool IsReplaced(CssBox box) => box switch
        {
            // Also matches CssBoxVideo, which resolves its poster through the same <object> machinery.
            CssBoxObject o => o.IsReplaced,
            CssBoxImage or CssBoxSvg or CssBoxFrame or CssBoxFormField => true,
            _ => false
        };

        /// <summary>
        /// Whether <paramref name="box"/> is a scroll container — §2's "elements with <c>overflow</c> other
        /// than <c>visible</c> or <c>clip</c>".
        /// </summary>
        /// <remarks>
        /// <para>
        /// The root element is excluded: its <c>overflow</c> propagates to the viewport rather than making
        /// it a scroll container
        /// (<see href="https://www.w3.org/TR/css-overflow-3/#overflow-propagation">CSS Overflow 3 §3.3</see>).
        /// A paginated renderer has no viewport for it to propagate to, so the declaration simply has no
        /// scroll container to name — which is also what stops the near-universal
        /// <c>html { overflow: hidden }</c> idiom from declaring a whole document unbreakable.
        /// </para>
        /// <para>
        /// <c>&lt;body&gt;</c> is excluded only <b>conditionally</b>, which §3.3 is specific about: the
        /// body's value propagates just when the root's own computed <c>overflow</c> is <c>visible</c>. If
        /// the root already declared one, the root took the propagation and the body is a scroll container
        /// in its own right — so <c>html { overflow: hidden } body { overflow: auto }</c> makes the body
        /// monolithic, where excluding it unconditionally would not.
        /// </para>
        /// <para>
        /// §2's <c>clip</c> exception is satisfied vacuously rather than deliberately: <c>Map.OverflowModes</c>
        /// accepts only <c>visible|hidden|scroll|auto</c>, so an authored <c>overflow: clip</c> fails to
        /// convert and the box keeps <c>visible</c>. Should <c>clip</c> ever be implemented, it has to be
        /// excluded here explicitly.
        /// </para>
        /// </remarks>
        internal static bool IsScrollContainer(CssBox box) =>
            box.Overflow.Value != Overflow.Visible && !IsViewportPropagationSource(box);

        private static bool IsViewportPropagationSource(CssBox box)
        {
            if (IsRootElement(box)) return true;

            // §3.3 propagates from the root's own body child only, and only while the root has not already
            // declared an overflow of its own.
            if (!IsNamed(box, "body") || box.ParentBox is not { } parent || !IsRootElement(parent))
                return false;

            return parent.Overflow.Value == Overflow.Visible;
        }

        private static bool IsRootElement(CssBox box) => box.IsRoot || IsNamed(box, "html");

        private static bool IsNamed(CssBox box, string name) =>
            string.Equals(box.HtmlTag?.Name, name, StringComparison.OrdinalIgnoreCase);

        // ── PeachPDF's own constraint, deliberately NOT §2 ────────────────────

        /// <summary>
        /// Whether <paramref name="box"/> runs a layout engine that fragments its own subtree — flex, grid,
        /// table and multi-column.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Not a spec claim.</b> Each of these engines decides where its own children go, so a break
        /// value inside one does not name a break point its parent's flow could take
        /// (<c>BreakPropagation.CanTravelOutOf</c>, this predicate's only caller). Narrowing this set — so
        /// flex and grid items, and multi-column columns, genuinely fragment — is what
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/315">#315</see> and
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/322">#322</see> amount to.
        /// </para>
        /// <para>
        /// <b>This is no longer "runs with breaking suppressed".</b> Every one of these engines now fills
        /// one fragmentainer per pass and can hand the driver a resumption record: the multi-column engine
        /// since <see href="https://github.com/jhaygood86/PeachPDF/issues/322">#322</see>, and the table
        /// engine since <see href="https://github.com/jhaygood86/PeachPDF/issues/464">#464</see> stopped
        /// running it behind a detached fragmentainer.
        /// </para>
        /// </remarks>
        /// <seealso cref="RunsAnEngineOfItsOwn"/>
        internal static bool PaginatesItsOwnContent(CssBox box) =>
            RunsAnEngineOfItsOwn(box.DerivedStyle.ActualDisplay) || box.EstablishesMultiColumnContext;

        /// <summary>
        /// The display-value half of <see cref="PaginatesItsOwnContent"/>, which
        /// <c>CssBox.LayoutContents</c> dispatches on directly — it needs to know <i>which</i> engine, so it
        /// cannot ask the combined question. Kept here so the two cannot name different sets.
        /// </summary>
        internal static bool RunsAnEngineOfItsOwn(string? display) =>
            display is Keywords.Flex or Keywords.InlineFlex
                    or Keywords.Grid or Keywords.InlineGrid
                    or Keywords.Table or Keywords.InlineTable;

        // ── §2's "overflows rather than being sliced", as a fitting question ──

        /// <summary>
        /// The block-axis space an enclosing <c>box-decoration-break: clone</c> reserves at the start and
        /// end of every fragment (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see>),
        /// which content has to clear on top of its own depth in order to fit anywhere.
        /// </summary>
        internal static (double Start, double End) ClonedBlockInsets(CssBox box, HtmlContainerInt container) =>
            container.HasCloneDecorations
                ? (DomUtils.ClonedBlockStart(box, stopAt: null), DomUtils.ClonedBlockEnd(box))
                : (0, 0);

        /// <summary>
        /// Whether content <paramref name="height"/> tall, plus the cloned decorations it must re-open and
        /// close with, fits in no fragmentainer at all.
        /// </summary>
        /// <remarks>
        /// This is §2's overflow-rather-than-slice rule expressed as the question layout actually needs to
        /// ask. Content with nowhere to fit must not be treated as breakable: moving it only repeats the
        /// question on the next fragmentainer, so every pass breaks again on the page it has just resumed
        /// on — which was verified to produce a zero-page document when a cloned inset exceeded the band.
        /// </remarks>
        internal static bool FitsNoFragmentainer(
            double height, double clonedStart, double clonedEnd, HtmlContainerInt container) =>
            height + clonedStart + clonedEnd >= container.PageSize.Height;

        /// <summary>
        /// Whether content <paramref name="height"/> tall, plus its cloned decorations, fits inside a
        /// content band <paramref name="bandHeight"/> tall.
        /// </summary>
        /// <remarks>
        /// Note this is <b>not</b> the negation of <see cref="FitsNoFragmentainer"/>. That one asks
        /// "could this ever fit anywhere?" against the nominal page height and treats an exact fit as not
        /// fitting — a boundary inherited from the per-word relocation it was extracted from, preserved
        /// because widening it there would change where words land. This one asks "will it fit
        /// <i>there</i>?" about one specific band, where an exact fit plainly does.
        /// </remarks>
        internal static bool FitsInBand(
            double height, double clonedStart, double clonedEnd, double bandHeight) =>
            height + clonedStart + clonedEnd <= bandHeight;
    }
}
