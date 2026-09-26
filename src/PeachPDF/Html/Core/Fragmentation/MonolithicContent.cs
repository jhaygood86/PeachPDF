using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
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
        /// <remarks>
        /// The block-size test runs before <see cref="BreaksInBlockFlow"/>, which walks the ancestors and
        /// the subtree: a capped scroll container is monolithic wherever it is placed.
        /// </remarks>
        internal static bool IsMonolithic(CssBox box) =>
            IsReplaced(box)
            || (IsScrollContainer(box) && (HasConstrainedBlockSize(box) || !BreaksInBlockFlow(box)));

        /// <summary>
        /// Whether <paramref name="box"/> is a block box whose breaks its parent's block flow decides, the
        /// only placement where an auto-height scroll container is fragmented rather than kept monolithic.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An <c>inline-block</c> is excluded because §2 lets inline-level boxes that establish an
        /// independent formatting context stay monolithic, and because PeachPDF cannot yet fragment one
        /// inside its line. Letting it try clipped the whole box to nothing.
        /// </para>
        /// <para>
        /// A float is excluded although it computes to <c>display: block</c>. A float is placed at its
        /// assigned position (<c>CssBox.LayoutContentAtItsAssignedPosition</c>), which cannot carry a
        /// break into the next fragmentainer. Letting its content break there silently dropped every line
        /// after the boundary.
        /// </para>
        /// <para>
        /// A flex or grid item is excluded because its engine sets and pins the item's own <c>height</c>
        /// while measuring and committing it (<c>ItemContentCommit</c>). A block-size test would then
        /// answer differently before and after the commit, and the line-relocation mover and the commit
        /// layout would disagree about the same item.
        /// </para>
        /// <para>
        /// An absolutely positioned, fixed or running box is excluded too. It is placed by its offsets and takes
        /// no part in placing the in-flow boxes after it (CSS 2.1 §9.3.1), so a break taken inside it ends the
        /// pass while those boxes are placed back on the page the break left, which is already emitted.
        /// </para>
        /// <para>
        /// A box that avoids breaks inside itself, or sits in one that does, is excluded
        /// (<see cref="AvoidsBreakInside"/>): it lost content when let through.
        /// </para>
        /// </remarks>
        private static bool BreaksInBlockFlow(CssBox box) =>
            box.DerivedStyle.ActualDisplay is Keywords.Block or Keywords.ListItem
            && !IsFloat(box)
            && !box.IsExcludedFromFlow
            && !AvoidsBreakInside(box)
            && box.ParentBox?.DerivedStyle.ActualDisplay is not (Keywords.Flex or Keywords.InlineFlex
                or Keywords.Grid or Keywords.InlineGrid)
            && EveryAncestorCarriesABreak(box)
            && EveryDescendantCarriesABreak(box);

        /// <summary>
        /// Whether every ancestor of <paramref name="box"/> is a kind known to carry a break taken inside
        /// it on into the next fragmentainer: an in-flow block or list item that is not a multi-column
        /// container, a block-level flex or grid container, or a block-level table and its row groups,
        /// rows and cells.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An allow-list, not a deny-list, because the failure is silent and the deny-list kept missing
        /// cases. Several placements lay their content out at an assigned position that drops a break
        /// token: an <c>inline-block</c>, a float or page float (<c>LayoutContentAtItsAssignedPosition</c>),
        /// a vertical writing-mode block (<c>LayoutVerticalBlockChildren</c>, see
        /// <see cref="IsUnresumableOrthogonalFlow"/>), an <c>inline-table</c>, and a table caption
        /// (<c>LayoutCaptionGroup</c>). A break taken anywhere inside one loses every line after it. Each
        /// was found only after the previous one was excluded, by measuring lines disappear.
        /// </para>
        /// <para>
        /// A multi-column container is excluded although it is a block. Its columns are fragmentainers
        /// of their own, and a scroll container that broke inside one lost its lines at the column
        /// boundary, even on a single page.
        /// </para>
        /// <para>
        /// Anything not listed keeps a scroll container inside it monolithic, which is the behaviour
        /// before auto-height scroll containers became fragmentable. So an unlisted placement can only
        /// leave that fix out, never lose content.
        /// </para>
        /// </remarks>
        private static bool EveryAncestorCarriesABreak(CssBox box)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                var carriesABreak = ancestor.DerivedStyle.ActualDisplay
                        is Keywords.Block or Keywords.ListItem or Keywords.Flex or Keywords.Grid
                        or Keywords.Table or Keywords.TableRowGroup or Keywords.TableHeaderGroup
                        or Keywords.TableFooterGroup or Keywords.TableRow or Keywords.TableCell
                    && !IsFloat(ancestor)
                    && !ancestor.EstablishesMultiColumnContext
                    && !IsUnresumableOrthogonalFlow(ancestor)
                    && !AvoidsBreakInside(ancestor);

                if (!carriesABreak) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether everything inside <paramref name="box"/> is a kind known to carry a break taken inside
        /// the box on into the next fragmentainer: inline content, blocks and list items that are not
        /// multi-column containers, and tables with their row groups, rows, cells and columns.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The counterpart of <see cref="EveryAncestorCarriesABreak"/>, and an allow-list for the same
        /// reason. A fragmenting box resumes on the next fragmentainer from a break token, and some of its
        /// contents are laid out by paths that drop that token. A page float is moved to its page edge
        /// whole. An absolutely or fixed positioned box is laid out after its containing block, against
        /// geometry the break has already moved on from. A multi-column, flex or grid container runs an
        /// engine that decides its own breaks, and a table caption is laid out by
        /// <c>LayoutCaptionGroup</c>. Letting a scroll container break around one of these can lose content
        /// that the monolithic slice used to draw; a multi-column child lost 29 words on a single page.
        /// </para>
        /// <para>
        /// A float and an atomic inline (<c>inline-block</c>, <c>inline-table</c>) are not allowed yet. A
        /// block-level float breaks between its lines, and its break ends the pass while the in-flow content
        /// beside it is placed back on the page the break left, which is already emitted; text beside a float
        /// that crossed a boundary was lost. An atomic inline taller than the space left on a page lost the
        /// tail of its slice. Around either, the scroll container stays monolithic, as it was before auto-height
        /// scroll containers became fragmentable.
        /// </para>
        /// <para>
        /// A box with <c>display: none</c> generates nothing, so neither it nor its subtree is inspected.
        /// </para>
        /// </remarks>
        private static bool EveryDescendantCarriesABreak(CssBox box)
        {
            var generation = box.HtmlContainer?.LayoutGeneration ?? -1;
            if (box.DescendantsCarryABreak is { } cached && cached.Generation == generation) return cached.Value;

            var value = WalkDescendants(box);
            box.DescendantsCarryABreak = (generation, value);
            return value;
        }

        private static bool WalkDescendants(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                var display = child.DerivedStyle.ActualDisplay;
                if (display is Keywords.None) continue;

                var carriesABreak = display
                        is Keywords.Inline or Keywords.Block or Keywords.ListItem
                        or Keywords.Table or Keywords.TableRowGroup
                        or Keywords.TableHeaderGroup or Keywords.TableFooterGroup or Keywords.TableRow
                        or Keywords.TableCell or Keywords.TableColumn or Keywords.TableColumnGroup
                    && !child.IsAbsolutelyPositioned
                    && !IsFloat(child)
                    && !child.EstablishesMultiColumnContext
                    && !DomUtils.IsAtomicInline(child);

                if (!carriesABreak || !EveryDescendantCarriesABreak(child)) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether <paramref name="box"/> asks not to be broken inside across pages (<c>break-inside: avoid</c>
        /// or <c>avoid-page</c>, css-break-3 §3.2).
        /// </summary>
        /// <remarks>
        /// Such a box moves whole when it fits, so a scroll container in it has no break to carry. One taller
        /// than a page has to break anyway (§4.4). When it does, a scroll container that fragments inside it
        /// lost the lines at the boundary, as a plain block there can (#1369). Kept monolithic, the scroll
        /// container is laid out as it was before auto-height scroll containers became fragmentable.
        /// </remarks>
        private static bool AvoidsBreakInside(CssBox box) =>
            BreakValues.AvoidsBreak(box.BreakInside.Value, FragmentationContext.Page);

        /// <summary>
        /// Whether <paramref name="box"/> floats, beside inline content (<c>left</c>/<c>right</c>/
        /// <c>inside</c>/<c>outside</c>) or to a page edge (<c>top</c>/<c>bottom</c>/<c>top-bottom</c>/
        /// <c>snap</c>). A page float is moved to its edge whole, so it cannot continue a break either.
        /// </summary>
        private static bool IsFloat(CssBox box) => box.IsFloated || box.IsPageFloated;

        /// <summary>
        /// Whether <paramref name="box"/> is a replaced element, whose content the UA cannot fragment
        /// because it has no fragmentable inner structure.
        /// </summary>
        /// <remarks>
        /// Discriminated by box type, the same way <c>FragmentContentPainters.For</c> selects the painter
        /// that draws these. Only <c>&lt;object&gt;</c> can answer no — it is replaced only once its
        /// <c>data</c> resource resolves to something renderable, which measurement decides.
        /// A list marker has a content painter of its own but is not a replaced element, and is never
        /// tall enough for the distinction to matter anyway.
        /// <c>CssBoxFormField</c> (<c>&lt;input&gt;</c>/<c>&lt;select&gt;</c>) is not a replaced
        /// element per spec either, but is included here anyway: an AcroForm widget annotation names
        /// exactly one page rect, so a form field must never fragment across a page break regardless
        /// of what §2 itself would otherwise allow.
        /// </remarks>
        internal static bool IsReplaced(CssBox box) => box switch
        {
            // Also matches CssBoxVideo, which resolves its poster through the same <object> machinery.
            CssBoxObject o => o.IsReplaced,
            CssBoxImage or CssBoxSvg or CssBoxMath or CssBoxFrame or CssBoxFormField => true,
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

        /// <summary>
        /// Whether <paramref name="box"/>'s block size is capped by its own style (a non-auto logical
        /// height, or a specified maximum logical height), so that its content can actually overflow it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// §2 calls elements that actually scroll monolithic, and makes the rest of the overflow set
        /// optional: "UAs may consider as monolithic any elements with <c>overflow</c> set to <c>auto</c>
        /// or <c>scroll</c> and any elements with <c>overflow: hidden</c> and a non-auto logical height
        /// (and no specified maximum logical height)". PeachPDF draws the line at whether the block size
        /// can clip anything. A scroll container whose block size grows with its content has nothing to
        /// scroll or clip in the block axis on paper, so it fragments like any block, as browsers do when
        /// printing. Treating it as monolithic instead means slicing it once it is taller than a page,
        /// and a line on the slice boundary is then lost.
        /// </para>
        /// <para>
        /// PeachPDF treats a scroll container as monolithic only when its block size is fixed: a non-auto
        /// <c>height</c> with no <c>max-height</c>, the one case the sentence names for <c>hidden</c>, and
        /// for <c>auto</c>/<c>scroll</c> also an <c>aspect-ratio</c> or both block-axis insets, which the
        /// "may" allows. A box capped by <c>max-height</c> alone breaks like a plain block for every
        /// overflow value, as Chrome prints it. The one exception is a box whose content actually overflows
        /// its cap. Its clipped lines lie past the box's own end, and a break among them ended the pass while
        /// the content after the box was placed back on the page the break left, so it was drawn on no page.
        /// Layout records such a box (<see cref="HtmlContainerInt.NoteScrollContainerClips"/>) and lays the
        /// document out again with it monolithic.
        /// </para>
        /// <para>
        /// A percentage against an indefinite base behaves as <c>auto</c>/<c>none</c> (CSS 2.1 §10.5,
        /// §10.7), and is treated as such here. Each is tested against the base layout itself resolves it
        /// against: <c>height</c> against the box's percentage base (for an absolutely positioned box, its
        /// nearest positioned ancestor), <c>max-height</c> against its in-flow containing block. A block
        /// size can also be fixed without either property, by a preferred <c>aspect-ratio</c> or by both
        /// block-axis insets of an absolutely positioned box, and those cap the box too.
        /// </para>
        /// <para>
        /// In a vertical writing mode the logical height is the physical width. Its percentages resolve
        /// against the containing block's width, which is definite unless that block is itself vertical,
        /// and a percentage is taken as a cap. A vertical containing block would make it no cap, but no box
        /// that reaches this question has one: a vertical block lays its children out at assigned
        /// positions, and a vertical multi-column container is a fragmentainer of its own, so
        /// <see cref="EveryAncestorCarriesABreak"/> keeps a box under either monolithic before its size is
        /// asked about.
        /// </para>
        /// </remarks>
        internal static bool HasConstrainedBlockSize(CssBox box)
        {
            var vertical = IsVertical(box);
            var (size, maxSize) = vertical ? (box.Width, box.MaxWidth) : (box.Height, box.MaxHeight);

            if (!vertical)
            {
                return (Constrains(size, isMax: false) && !Constrains(maxSize, isMax: true))
                       || box.HtmlContainer?.ScrollContainersThatClip.Contains(box) == true
                       || (box.Overflow.Value != Overflow.Hidden
                           && (HasPreferredAspectRatio(box) || IsSizedByBothBlockInsets(box, vertical: false)));
            }

            // The height and max-height percentages resolve against different bases in layout: height
            // against the box's own percentage base (GetBoxHeight), max-height against its in-flow
            // containing block (ApplyHeight's clamp), whatever the box's position. Each mirrors its own.
            return Constrains(size, isMax: false)
                || Constrains(maxSize, isMax: true)
                || HasPreferredAspectRatio(box)
                || IsSizedByBothBlockInsets(box, vertical);

            // Only a length caps anything: layout applies a height or max-height only when it passes
            // IsValidLength, so a keyword (auto, none) leaves the box unconstrained whatever its spelling.
            bool Constrains(string value, bool isMax) =>
                CssValueParser.IsValidLength(value)
                && (!CssValueParser.DependsOnPercentage(value)
                    || vertical
                    || IsInsideAFlexOrGridItem(box)
                    || HeightPercentageResolves(isMax));

            bool HeightPercentageResolves(bool isMax) => isMax
                ? CssLayoutEngine.IsHeightDefinite(box.ContainingBlock)
                : CssLayoutEngine.PercentageHeightResolves(box);
        }

        /// <summary>
        /// Whether <paramref name="box"/> declares a preferred <c>aspect-ratio</c>, which gives an
        /// auto-sized block axis a size from the other axis rather than from content (CSS Box Sizing 4 §5).
        /// A scroll container's automatic minimum size is zero, so its content can overflow that size.
        /// </summary>
        /// <remarks>
        /// Asked of the declaration rather than through <c>CssLayoutEngine.TryGetAspectRatioHeight</c>,
        /// which needs the box's used width and so would answer differently before its width is laid out.
        /// </remarks>
        private static bool HasPreferredAspectRatio(CssBox box) =>
            !string.IsNullOrEmpty(box.AspectRatio)
            && !string.Equals(box.AspectRatio, Keywords.Auto, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="box"/> is absolutely positioned with both block-axis insets set, so an
        /// auto block size fills the space between them rather than growing with content (CSS 2.1
        /// §10.6.4, and §10.3.7 for the physical width a vertical writing mode uses as its block size).
        /// </summary>
        private static bool IsSizedByBothBlockInsets(CssBox box, bool vertical) =>
            box.Position.Value is PositionMode.Absolute
            && (vertical
                ? box.Left.Value.IsValue && box.Right.Value.IsValue
                : box.Top.Value.IsValue && box.Bottom.Value.IsValue);

        /// <summary>
        /// Whether <paramref name="box"/> descends from a flex or grid item. The item's own height is set
        /// and pinned by its engine partway through layout, so whether a percentage against it resolves
        /// would change within one pass. A percentage there is treated as capping the box, the stable
        /// answer this file gave before auto-height scroll containers became fragmentable.
        /// </summary>
        private static bool IsInsideAFlexOrGridItem(CssBox box)
        {
            for (var item = box.ParentBox; item?.ParentBox is { } parent; item = parent)
            {
                if (parent.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex
                    or Keywords.Grid or Keywords.InlineGrid)
                    return true;
            }

            return false;
        }

        private static bool IsVertical(CssBox box) =>
            box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr;

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

        // ── A third, PeachPDF-only reason - not §2, not "runs its own engine" ─

        /// <summary>
        /// Whether <paramref name="box"/> is a box <c>CssBox.LayoutContents</c> actually routes to
        /// <c>CssLayoutEngine.CreateVerticalLineBoxes</c> or <c>CssBox.LayoutVerticalBlockChildren</c> - a
        /// vertical-writing-mode block box holding inline-only or block-level content - and must therefore
        /// be treated as an indivisible unit by its parent's fragmentation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Not a spec claim</b> (like <see cref="PaginatesItsOwnContent"/>, not <see cref="IsMonolithic"/>'s
        /// §2 set): nothing in css-break-3 forbids fragmenting a vertical-writing-mode box's content across a
        /// page boundary. This is a PeachPDF implementation limitation — neither <c>CreateVerticalLineBoxes</c>
        /// nor <c>LayoutVerticalBlockChildren</c> (issue #760) can hand back a resumption record, laying out
        /// their whole subtree in one pass instead, the same practical constraint a replaced element has for
        /// a genuinely different (spec) reason. See the no-vertical-writing-mode-layout accepted gap for the
        /// tracked follow-up that lifts this.
        /// </para>
        /// <para>
        /// <b>Deliberately not just <c>box.WritingMode.Value is VerticalRl/VerticalLr</c></b> — that alone
        /// is not this box's own dispatch fact, it's every one of its descendants' too, since
        /// <c>writing-mode</c> inherits: a whole multi-paragraph <c>&lt;body style="writing-mode:
        /// vertical-rl"&gt;</c> would otherwise report every one of its ordinarily-paginating block
        /// children (each laid out through the ordinary, resumable per-child block-flow path) as
        /// unresumable too, breaking normal multi-page pagination for the common case this feature does not
        /// touch. The extra conditions here mirror <c>CssBox.LayoutContents</c>'s own dispatch gate exactly
        /// - every box satisfying them goes through either <c>CreateVerticalLineBoxes</c> or
        /// <c>LayoutVerticalBlockChildren</c>, never the ordinary per-child block-flow path - so this
        /// predicate can never disagree with what actually ran.
        /// </para>
        /// <para>
        /// <b><c>DomUtils.ContainsInlinesOnly(box) || !box.EstablishesMultiColumnContext</c>, not either one
        /// alone</b> — <c>LayoutContents</c> checks the inlines-only vertical branch <i>before</i> the
        /// multi-column branch, so a box that is both (an admittedly narrow combination: a vertical,
        /// multi-column box holding only inline content) still dispatches to the unresumable
        /// <c>CreateVerticalLineBoxes</c>, never to <c>CssLayoutEngineColumns</c> — dropping the
        /// <c>ContainsInlinesOnly</c> arm entirely (checking only <c>!EstablishesMultiColumnContext</c>, as
        /// issue #760's original landing briefly did) wrongly reported that combination as resumable, a real
        /// regression a post-change review caught. A vertical multi-column box with genuine block-level
        /// children, by contrast, does reach the resumable <c>CssLayoutEngineColumns</c> path (issue #764),
        /// which is what <c>!EstablishesMultiColumnContext</c> alone still correctly excludes for that case.
        /// </para>
        /// </remarks>
        internal static bool IsUnresumableOrthogonalFlow(CssBox box) =>
            box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr
            && box.PlacesItselfAsBlockBox
            && !RunsAnEngineOfItsOwn(box.DerivedStyle.ActualDisplay)
            && (DomUtils.ContainsInlinesOnly(box) || !box.EstablishesMultiColumnContext);

        /// <summary>
        /// Whether <paramref name="box"/> is a table box under a vertical writing mode that
        /// <c>CssLayoutEngineTable.RelocateColumnsAcrossPageBoundaries</c> cannot help (issue #783's own
        /// first-cut scope boundary) — a table using any feature that pass declines to touch, so it must
        /// keep today's whole-table "move as one unit" treatment. A <b>plain-grid</b> vertical table (no
        /// excluded feature) is deliberately <i>not</i> unconditionally unresumable any more: real
        /// per-column pagination relocates its content across page boundaries as a genuine subtree
        /// translation (see that method's own remarks for why this is sound without a new interleaved
        /// layout pass), and whether that relocation actually found anything to do is answered the same
        /// way an ordinary <c>horizontal-tb</c> table's own row-break decision already is —
        /// <see cref="CssBox.PaginatedItsOwnContentWithoutBreaking"/> reading <c>PageBreakBottoms</c> as a
        /// post-layout fact, not asked here as a style-derived property. That is also the safety net for
        /// every case this predicate itself declines: if <c>RelocateColumnsAcrossPageBoundaries</c> finds
        /// nothing to relocate (fits on one page) or bails out entirely (a single column too tall for any
        /// page — the one sub-case even a plain grid still cannot help), it leaves <c>PageBreakBottoms</c>
        /// untouched, and the table falls through to exactly today's monolithic move-whole behavior with
        /// no special-casing needed here for those outcomes.
        /// </summary>
        /// <remarks>
        /// Not a spec claim, the same way <see cref="IsUnresumableOrthogonalFlow"/> isn't: css-tables-3
        /// does not forbid fragmenting a vertical table's own content across a page boundary. This is the
        /// same practical constraint that predicate already tracks for vertical-writing-mode block
        /// content — narrowed, for the one remaining engine (<c>CssLayoutEngineTable</c>), to the specific
        /// combinations of features real per-column pagination doesn't yet reach. See the
        /// no-vertical-writing-mode-layout accepted gap and issue #783 for the tracked follow-up that
        /// lifts the remaining excluded-feature combinations.
        /// </remarks>
        internal static bool IsUnresumableVerticalTable(CssBox box) =>
            box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr
            && box.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable
            && HasColumnPaginationExcludedFeature(box);

        /// <summary>
        /// Whether <paramref name="box"/> (a table) uses a feature <c>CssLayoutEngineTable
        /// .RelocateColumnsAcrossPageBoundaries</c>'s first cut does not support: a <c>rowspan</c>/
        /// <c>colspan</c> cell, a <c>&lt;caption&gt;</c>, a <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>,
        /// collapsed borders, or the table itself being a flex/grid item. Kept as one whole-table
        /// combinator, consulted by both that method (to decide whether to attempt relocation at all) and
        /// <see cref="IsUnresumableVerticalTable"/> (to keep such a table's own already-shipped,
        /// already-tested combined scenario — #762's own showcase, which exercises all five original
        /// features together — byte-for-byte unchanged), so the two can never disagree about which tables
        /// are in scope.
        /// </summary>
        /// <remarks>
        /// The flex/grid-item exclusion exists because <c>LineRelocation.MayNotBeCut</c> reads this same
        /// predicate (via <see cref="IsUnresumableVerticalTable"/>/<see cref="IsMonolithicForFragmentation"/>)
        /// to decide whether a flex line or grid row containing this table must move to the next
        /// fragmentainer as one unit. Before this predicate existed, every vertical table was unconditionally
        /// monolithic, so that question always came out "yes" for a line holding one. A plain-grid table
        /// answering "no" now would let <c>LineRelocation</c> leave the line straddling a boundary while
        /// <c>RelocateColumnsAcrossPageBoundaries</c> - which reasons purely about the table's own column
        /// axis against the page grid, with no awareness of the flex/grid line it sits in - relocates the
        /// table's columns independently of whatever the line does with the table's siblings. Excluding a
        /// flex/grid-item table here keeps it monolithic in that context, exactly as before, deferring real
        /// coordination between the two mechanisms to a future, separately-scoped follow-up.
        /// </remarks>
        internal static bool HasColumnPaginationExcludedFeature(CssBox box)
        {
            if (box.BorderCollapse.Value == BorderCollapseMode.Collapse) return true;

            if (box.ParentBox is { } parent &&
                parent.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex
                    or Keywords.Grid or Keywords.InlineGrid)
            {
                return true;
            }

            foreach (var child in box.Boxes)
            {
                switch (child.DerivedStyle.ActualDisplay)
                {
                    case Keywords.TableCaption:
                    case Keywords.TableHeaderGroup:
                    case Keywords.TableFooterGroup:
                        return true;
                    case Keywords.TableRow:
                        if (RowHasSpanningCell(child)) return true;
                        break;
                    case Keywords.TableRowGroup:
                        foreach (var row in child.Boxes)
                        {
                            if (row.DerivedStyle.ActualDisplay == Keywords.TableRow && RowHasSpanningCell(row))
                                return true;
                        }
                        break;
                }
            }

            return false;
        }

        private static bool RowHasSpanningCell(CssBox row)
        {
            foreach (var cell in row.Boxes)
            {
                if (CssLayoutEngineTable.GetRowSpan(cell) > 1 || CssLayoutEngineTable.GetColSpan(cell) > 1)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="box"/> must be treated as an indivisible unit by its parent's own
        /// fragmentation, for any of the reasons this file tracks separately — <see cref="IsMonolithic"/>'s
        /// §2 set, <see cref="IsUnresumableOrthogonalFlow"/>'s PeachPDF-only one, or
        /// <see cref="IsUnresumableVerticalTable"/>'s. The combinator every "may this be sliced" call site
        /// should consult, so a future reason is added once here rather than at each of this predicate's
        /// own call sites individually.
        /// </summary>
        internal static bool IsMonolithicForFragmentation(CssBox box) =>
            IsMonolithic(box) || IsUnresumableOrthogonalFlow(box) || IsUnresumableVerticalTable(box);

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
