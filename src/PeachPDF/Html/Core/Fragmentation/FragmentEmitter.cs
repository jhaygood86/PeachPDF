using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// Layout's fragment output, collected as layout produces it: the driver
    /// (<c>HtmlContainerInt.LayoutDocument</c>) hands each fragmentainer pass's slots over as that pass
    /// ends, and <see cref="Finish"/> materializes the immutable <see cref="FragmentTree"/> everything
    /// downstream consumes (CSS Fragmentation Level 3 §2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Which slots exist is now a structural fact of the driver, not a geometric rediscovery.</b> The
    /// retired <c>FragmentTreeBuilder</c> walked the finished box tree once at the end and derived the slot
    /// list from <c>ActualSize.Height</c>, so the tree was reconstructed from layout's side effects rather
    /// than being layout's own output, and the two models could in principle disagree. A pass now states
    /// the slots it filled, and the geometric question survives in exactly one place: after the <i>final</i>
    /// pass, the highest band any geometry reaches — because a monolithic subtree laid out in one pass can
    /// cover bands past the one it started in without producing any break record at all.
    /// </para>
    /// <para>
    /// <b>Emission is per pass, not per box</b>, and deliberately so. Several mechanisms relocate a box
    /// after its own <c>PerformLayoutImp</c> has returned — the flex and grid engines translate placed
    /// items, the columns engine re-bands children, the table engine's post-check moves the whole table —
    /// so a fragment emitted from a box's own epilogue would capture geometry the box is about to leave.
    /// Every one of those movers is bounded within the pass that discovered it, which is what makes the end
    /// of a pass the earliest point its content can be frozen.
    /// </para>
    /// <para>
    /// <b>First/last comes from the break record where there is one.</b> At the end of a pass the outgoing
    /// token names exactly the boxes continuing into the next fragmentainer, and the incoming one exactly
    /// those continuing from the previous — the spec's own notion of a fragment's edges rather than an
    /// inference from rectangles. Monolithic subtrees appear in no record, so their span comes from the
    /// slots they were emitted in; the two sources are intersected rather than chosen between, so a box
    /// that both breaks and carries monolithic descendants cannot be told it is first in a fragmentainer it
    /// continued into. (These flags are informational — the edges a <c>box-decoration-break</c> value
    /// applies at live on <see cref="LineFragment.Slice"/>, per rectangle.)
    /// </para>
    /// </remarks>
    internal sealed class FragmentEmitter(HtmlContainerInt container)
    {
        /// <summary>
        /// Defensive backstop on the emitted slot range, behind <see cref="PageGeometryTable"/>'s own
        /// minimum-band clamp. Mirrors the cap the retired candidate-slot walk used.
        /// </summary>
        private const int MaxSlots = 100_000;

        /// <summary>
        /// Minimum overlap between a rectangle and a content band for the rectangle to be considered
        /// part of that band. Matches <c>CssBox.IsRectVisible</c>'s own epsilon, so the fragment tree
        /// contains exactly the rectangles the painter would have drawn — a rectangle that merely
        /// touches a band edge belongs to the neighbouring band alone, not to both.
        /// </summary>
        private const double BandOverlapEpsilon = 1e-6;

        /// <summary>
        /// Tolerance for deciding that a decoration rectangle's edge coincides with the unbroken box's own
        /// edge — that is, that it is a real box edge rather than a fragmentation break
        /// (<see cref="SliceGeometry.HasLeftEdge"/>). Deliberately <see cref="RRect"/>'s own equality
        /// tolerance rather than <see cref="BandOverlapEpsilon"/>'s overlap one: paint compares the strip to
        /// the rectangle with <c>==</c> to decide whether anything needs slicing at all, so a finer tolerance
        /// here could call an edge broken on a rectangle paint had already judged unbroken.
        /// </summary>
        private const double EdgeEpsilon = 0.001;

        /// <summary>
        /// Identifies a box within the emission walk. A repeating table header's source subtree is reached
        /// through a <see cref="CssProxyBox"/> and appears once per page at a different position each time,
        /// so the same <see cref="CssBox"/> can carry several unrelated spans — the owning proxy
        /// disambiguates them. <see cref="BoxFragment"/> itself does not record the proxy, which is why the
        /// association has to be carried here deliberately rather than recovered later.
        /// </summary>
        /// <remarks>
        /// <see cref="Instance"/> names which of a slot's nested fragmentainers the fragment belongs to, 0
        /// for the page itself. Two columns of one page are two fragmentainers of the same slot, so a box
        /// appearing in both produces two fragments that a (box, slot) pair alone could not tell apart.
        /// </remarks>
        private readonly record struct FragmentKey(CssBox Box, CssProxyBox? Owner, int Instance);

        /// <summary>
        /// One emitted pagination slot: its index and the document-space band layout paginated against.
        /// Slots are named by grid index so a per-page <c>@page</c> margin override can give each its own
        /// band top and height.
        /// </summary>
        private readonly record struct Slot(
            int Index, double BandTop, double BandBottom, PageBandGeometry Geometry, double LocalOriginY);

        /// <summary>
        /// The area of one fragmentainer that geometry is measured against — the question "does this
        /// rectangle belong here?", which is what separates one fragment of a box from another.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A page asks it of the block axis alone, and a column of both.</b> Every fragmentainer of the
        /// page grid differs from the last in the block axis only — CSS Fragmentation Level 3 §2 shares one
        /// inline size and position across a box's fragments — so <see cref="Left"/>/<see cref="Right"/> are
        /// null for a page and the test reduces exactly to the band-overlap rule the emitter has always
        /// used. A multi-column column is a fragmentainer that differs from its neighbours in the
        /// <i>inline</i> axis while sitting inside one page band, so for it the block axis cannot answer at
        /// all and both are needed.
        /// </para>
        /// <para>
        /// The block-axis rule is a minimum-overlap one, matching <c>CssBox.IsRectVisible</c>'s epsilon so
        /// the tree holds exactly the rectangles the painter would draw. The inline-axis rule adds a
        /// degenerate case the block axis never had to consider: a zero-width rectangle overlaps nothing, so
        /// it is placed by its own left edge instead. Columns are disjoint and content is laid out inside
        /// one, so no rectangle is claimed twice unless it genuinely spills across a column gap.
        /// </para>
        /// </remarks>
        private readonly record struct FragmentRegion(double Top, double Bottom, double? Left, double? Right)
        {
            internal bool Contains(RRect rect) =>
                Math.Min(rect.Bottom, Bottom) - Math.Max(rect.Top, Top) > BandOverlapEpsilon
                && ContainsInlineAxis(rect);

            private bool ContainsInlineAxis(RRect rect)
            {
                if (Left is not { } left || Right is not { } right) return true;

                return rect.Width > BandOverlapEpsilon
                    ? Math.Min(rect.Right, right) - Math.Max(rect.Left, left) > BandOverlapEpsilon
                    : rect.Left >= left - EdgeEpsilon && rect.Left <= right + EdgeEpsilon;
            }

            /// <summary>This region's block-axis extent, with <paramref name="rect"/>'s inline axis kept.</summary>
            internal RRect BlockCut(RRect rect, double topInset, double bottomInset)
            {
                var top = Math.Max(rect.Top, Top + topInset);
                var bottom = Math.Min(rect.Bottom, Bottom - bottomInset);

                return bottom > top ? new RRect(rect.X, top, rect.Width, bottom - top) : rect;
            }
        }

        /// <summary>
        /// One nested fragmentainer's geometry, handed over by the engine that filled it — a multi-column
        /// column (<see href="https://www.w3.org/TR/css-break-3/#fragmentainer">§2</see>: "a column in
        /// multi-column layout, or a page in paged media").
        /// </summary>
        /// <remarks>
        /// This is layout <i>telling</i> the emitter where the content it placed went, rather than the
        /// emitter reading where the boxes currently are. It has to be: a box continuing from one column
        /// into the next is laid out again at the next column's inline position, so by the end of the page
        /// pass its live geometry describes only its last fragment. The captured snapshot is the geometry
        /// as it stood when that column was filled, and <see cref="FragmentRegion"/> is what tells the
        /// column's own rectangles from the ones a neighbouring column contributed to the same box.
        /// </remarks>
        /// <remarks>
        /// <see cref="Continuing"/> holds the boxes that did not finish here and carry on into the next one.
        /// Their own height has not been applied at capture time — a box only reaches its epilogue on the pass
        /// that completes it — so their decoration area is the content they placed here rather than the
        /// zero-height box they still report. The page grid needs no equivalent: there a continuing box's
        /// bounds are cut to the band, which is exactly what cannot separate two fragmentainers sharing one.
        /// </remarks>
        /// <remarks>
        /// <see cref="ContinuedFrom"/> is the previous fragmentainer's <see cref="Continuing"/> — the boxes
        /// this one <i>resumes</i>. Kept here rather than re-derived because §6.2's block-axis edges are a
        /// break fact and this is where that fact lives for a nested context: a box that carries on past a
        /// column and one that ends there have the same block extent, so nothing downstream can tell them
        /// apart.
        /// </remarks>
        private readonly record struct NestedFragmentainer(
            FragmentRegion Region,
            BoxGeometrySnapshot Geometry,
            IReadOnlySet<CssBox> Continuing,
            IReadOnlySet<CssBox> ContinuedFrom);

        /// <summary>
        /// A fragment before its first/last flags are known — which cannot be until every slot has been
        /// emitted, so the tree is collected as mutable drafts and materialized once at the end.
        /// </summary>
        private sealed class Draft(
            FragmentKey key, CssBox box, Slot slot, FragmentRegion region, BoxGeometrySnapshot? snapshot, double originY)
        {
            internal FragmentKey Key { get; } = key;
            internal CssBox Box { get; } = box;
            internal Slot Slot { get; } = slot;

            /// <summary>
            /// The fragmentainer area this fragment's geometry belongs to — the page band, or the column
            /// band and inline span of the nested fragmentainer it was captured in.
            /// </summary>
            internal FragmentRegion Region { get; } = region;

            internal BoxGeometrySnapshot? Snapshot { get; } = snapshot;
            internal double OriginY { get; } = originY;

            internal bool IsFixed { get; set; }
            internal bool IsMonolithic { get; set; }

            /// <summary>
            /// Whether the box continues past this fragmentainer without having had its own height applied,
            /// so its decoration area runs to the bottom of what it placed here rather than to the bottom it
            /// currently reports. See <see cref="NestedFragmentainer.Continuing"/>.
            /// </summary>
            internal bool BoundsEndAtItsContent { get; set; }

            /// <summary>
            /// Whether the box carries on past this fragmentainer, and whether it resumes one it began in
            /// earlier — §6.2's block-axis edges (<see cref="SliceGeometry.HasTopEdge"/>). Read from the
            /// break record rather than from geometry, which cannot answer it inside a nested context.
            /// </summary>
            internal bool ContinuesIntoTheNext { get; set; }

            /// <inheritdoc cref="ContinuesIntoTheNext"/>
            internal bool ContinuedFromThePrevious { get; set; }

            /// <summary>
            /// Whether the box's own decoration area is its border box rather than a set of per-line
            /// rectangles — a block-level box, which is one rectangle. Resolved at materialization from the
            /// box's final bounds, because a box that continues into a later fragmentainer has not had its
            /// height applied yet on the pass that freezes this slot.
            /// </summary>
            internal bool UsesOwnBounds { get; set; }

            /// <summary>
            /// The box's own per-line decoration rectangles that landed in this slot, in document space.
            /// Kept raw because <see cref="SliceGeometry"/> is defined over <i>every</i> rectangle the box
            /// produces across every fragmentainer, and a later pass can still add one.
            /// </summary>
            internal List<(CssLineBox Line, RRect Rect)> Lines { get; } = [];

            internal List<TextFragment> Words { get; } = [];
            internal List<Draft> Children { get; } = [];
        }

        private readonly SortedDictionary<int, (Slot Slot, Draft Root, bool HasPrintableContent)> _emitted = [];
        private readonly Dictionary<FragmentKey, (int First, int Last)> _spans = [];

        /// <summary>
        /// Where each box's fragments begin and end in fill order, ignoring which nested fragmentainer they
        /// landed in — the question <see cref="_spans"/> cannot answer, since two columns of one slot are two
        /// keys. A break edge only exists where there is a fragment on the other side of it, and that is what
        /// this says.
        /// </summary>
        private readonly Dictionary<(CssBox Box, CssProxyBox? Owner),
            ((int Slot, int Instance) First, (int Slot, int Instance) Last)> _fragmentRange = [];

        /// <summary>
        /// Every fragment of a box, in fill order — what §6.2's <b>block-axis</b> strip is the concatenation
        /// of inside a nested fragmentainer. Kept as drafts rather than as extents so the measurement can be
        /// taken lazily: an extent is a question about a draft's whole subtree, and the subtree is complete
        /// only once every slot has been emitted.
        /// </summary>
        private readonly Dictionary<(CssBox Box, CssProxyBox? Owner), List<Draft>> _fragmentsOf = [];

        /// <summary>
        /// Memoized document-space extents and fragmentainer-local rectangles, one per draft. Both are
        /// computed over the <i>draft</i> tree rather than over materialized fragments, which is what makes
        /// the whole set available before the first fragment is built — the concatenated strip needs the
        /// extent of a box's other fragments, and those are materialized later in the same walk.
        /// </summary>
        private readonly Dictionary<Draft, RRect> _extents = new(ReferenceEqualityComparer.Instance);

        /// <inheritdoc cref="_extents"/>
        private readonly Dictionary<Draft, RRect> _rects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<(FragmentKey Key, int Slot)> _continuedFrom = [];
        private readonly HashSet<(FragmentKey Key, int Slot)> _continuesInto = [];
        private readonly Dictionary<FragmentKey, Dictionary<CssLineBox, RRect>> _rectangles = [];
        private readonly HashSet<CssBox> _frozen = new(ReferenceEqualityComparer.Instance);
        private readonly SortedSet<int> _stale = [];
        private readonly Dictionary<(CssBox Root, int Slot), List<NestedFragmentainer>> _nested = [];
        private int _lastEmittedSlot = -1;

        /// <summary>The empty box set the first nested fragmentainer of a slot resumes nothing from.</summary>
        private static readonly IReadOnlySet<CssBox> NoBoxes = new HashSet<CssBox>(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Hands over one nested fragmentainer <paramref name="contextRoot"/> has just finished filling
        /// inside pagination slot <paramref name="slot"/>.
        /// </summary>
        /// <param name="contextRoot">the box that owns the nested fragmentation context</param>
        /// <param name="slot">the pagination slot the nested fragmentainer sits in</param>
        /// <param name="band">its block-axis extent in document space</param>
        /// <param name="inline">its inline-axis extent in document space</param>
        /// <param name="geometry">
        /// the subtree's geometry as it stood when the fragmentainer was filled, which is the only point at
        /// which it can be recorded — content continuing into the next one is laid out again there.
        /// </param>
        /// <param name="continuing">the boxes that carry on into the next fragmentainer</param>
        internal void RecordNestedFragmentainer(
            CssBox contextRoot,
            int slot,
            (double Top, double Bottom) band,
            (double Left, double Right) inline,
            BoxGeometrySnapshot geometry,
            IReadOnlySet<CssBox> continuing)
        {
            if (!_nested.TryGetValue((contextRoot, slot), out var fragmentainers))
            {
                _nested[(contextRoot, slot)] = fragmentainers = [];
            }

            fragmentainers.Add(new NestedFragmentainer(
                new FragmentRegion(band.Top, band.Bottom, inline.Left, inline.Right),
                geometry,
                continuing,
                fragmentainers.Count > 0 ? fragmentainers[^1].Continuing : NoBoxes));
        }

        /// <summary>
        /// Discards the nested fragmentainers <paramref name="contextRoot"/> recorded in
        /// <paramref name="slot"/>, or — with no slot — in every slot, for a fill that is being attempted
        /// afresh.
        /// </summary>
        /// <remarks>
        /// Both forms are needed and they are not the same question. A container re-fills its columns
        /// several times inside one pass (<c>column-fill: balance</c> re-balances, and an under-shooting
        /// estimate is grown and re-run), which invalidates that slot alone. A container laid out again from
        /// the start — a §4.3 mover relocating it — may land in a different slot altogether, and the
        /// fragmentainers it recorded in the slot it has left describe geometry that no longer exists
        /// anywhere.
        /// </remarks>
        internal void ClearNestedFragmentainers(CssBox contextRoot, int? slot = null)
        {
            if (slot is { } only)
            {
                _nested.Remove((contextRoot, only));
                return;
            }

            foreach (var key in new List<(CssBox Root, int Slot)>(_nested.Keys))
            {
                if (ReferenceEquals(key.Root, contextRoot)) _nested.Remove(key);
            }
        }

        /// <summary>
        /// Freezes every slot the pass that has just ended filled, inclusive of both bounds.
        /// </summary>
        /// <param name="fromSlot">the slot the pass started filling</param>
        /// <param name="throughSlot">
        /// the last slot it reached — one below where the next pass resumes, or, for the final pass, the
        /// highest band any of the document's geometry touches. Both are questions only the driver can
        /// answer: a box placed far down the document, or a monolithic subtree covering several bands, means
        /// the next pass's slot is not <c>fromSlot + 1</c>.
        /// </param>
        /// <param name="incoming">the resumption record this pass was entered with, or null</param>
        /// <param name="outgoing">the record it stopped at, or null when the document finished</param>
        internal void EmitPass(int fromSlot, int throughSlot, BreakToken? incoming, BreakToken? outgoing)
        {
            for (var slot = fromSlot; slot <= throughSlot && slot < MaxSlots; slot++)
            {
                // A box in the incoming chain continued from before every slot this pass produced, and one in
                // the outgoing chain continues past every one of them - the pass is the unit either record
                // speaks about, so the whole range is marked rather than just its ends.
                RecordChain(incoming, slot, _continuedFrom);
                RecordChain(outgoing, slot, _continuesInto);

                EmitSlot(slot);
            }
        }

        /// <summary>
        /// Emits any slot a directional forced break reserved as deliberately blank that lies past every
        /// slot the passes reached — the trailing <c>break-after</c> case, which is settled only once the
        /// final document height is known and so cannot be stated by a pass.
        /// </summary>
        internal void EmitReservedBlankSlots()
        {
            if (container.MaxReservedBlankSlot is not { } reserved) return;

            for (var slot = _lastEmittedSlot + 1; slot <= reserved && slot < MaxSlots; slot++)
            {
                EmitSlot(slot);
            }
        }

        /// <summary>
        /// Whether any frozen fragmentainer holds a fragment for <paramref name="box"/> — asked before the
        /// slot a relocation would invalidate is worked out at all, since a box no frozen fragmentainer holds
        /// has nothing to un-freeze. That is the whole reason ordinary forward layout, where every box is
        /// placed for the first time, never re-emits anything.
        /// </summary>
        internal bool HoldsFragmentsFor(CssBox box) => _frozen.Contains(box);

        /// <summary>
        /// Drops every frozen slot from <paramref name="fromSlot"/> on, so it is emitted again once layout
        /// has settled.
        /// </summary>
        /// <remarks>
        /// The one thing per-pass emission cannot assume away: §4.3's retroactive movers are bounded within
        /// the pass that <i>discovers</i> them, but a box only reaches its epilogue on the pass that
        /// <i>completes</i> it - which for a box spanning several fragmentainers is a later pass than the one
        /// that placed it. So <c>break-inside: avoid</c> can relocate a box out of a fragmentainer already
        /// frozen, and the frozen copy would keep painting it there. Rather than forbid that, the driver
        /// un-freezes what moved (<see href="https://github.com/jhaygood86/PeachPDF/issues/355">#355</see>
        /// is the same property arrived at from the other direction). A pass never invalidates the slot it is
        /// itself filling or anything after it, since those are not frozen yet, so ordinary forward layout
        /// never re-emits anything.
        /// </remarks>
        internal void InvalidateFrom(int fromSlot)
        {
            if (fromSlot > _lastEmittedSlot) return;

            for (var slot = fromSlot; slot <= _lastEmittedSlot; slot++)
            {
                if (_emitted.Remove(slot)) _stale.Add(slot);
            }
        }

        /// <summary>
        /// Materializes the immutable tree. Returns an empty tree when there is nothing to paginate.
        /// </summary>
        internal FragmentTree Finish()
        {
            if (container.Root is null || container.ActualSize.Height <= 0)
                return new FragmentTree([]);

            // Slots a later pass's mover disturbed, emitted again now that layout has settled.
            foreach (var slot in new List<int>(_stale))
            {
                EmitSlot(slot);
            }

            if (_emitted.Count == 0) return new FragmentTree([]);

            // A box's span is the slots it was emitted in, which is only complete now. Recomputed from the
            // drafts rather than accumulated as they were built, so re-emitting a slot cannot leave a span
            // describing a fragment that no longer exists.
            _spans.Clear();
            _rectangles.Clear();
            _fragmentRange.Clear();
            _fragmentsOf.Clear();
            _extents.Clear();
            _rects.Clear();
            foreach (var (_, root, _) in _emitted.Values)
            {
                RecordSpansAndRectangles(root);
            }

            var fragmentainers = new List<FragmentainerFragment>(_emitted.Count);
            FragmentainerFragment? firstCandidate = null;

            foreach (var (slot, root, hasPrintableContent) in _emitted.Values)
            {
                var fragmentainer = new FragmentainerFragment(
                    new RRect(
                        container.MarginLeft,
                        container.MarginTop,
                        container.PageSize.Width + container.MarginRight,
                        slot.Geometry.BandHeight),
                    slot.Index,
                    slot.Geometry,
                    slot.LocalOriginY,
                    Materialize(root));

                firstCandidate ??= fragmentainer;

                // A slot a directional forced break deliberately stepped over is a real page that simply
                // has no content on it (css-break-3 §3.1's "one or two page breaks"), so it is
                // materialized even though CSS Paged Media Level 3 §3.2 would otherwise decline to build
                // a content-empty fragmentainer. It is an ordinary page in every other respect: it takes
                // its @page context's canvas background and margin boxes, and it counts toward
                // counter(page)/counter(pages).
                if (hasPrintableContent || container.IsReservedBlankSlot(slot.Index))
                    fragmentainers.Add(fragmentainer);
            }

            // Never emit a 0-page document for content that genuinely laid out to a non-zero height:
            // if nothing at all qualified as printable (e.g. every box is a whole-page canvas
            // background, or the printable-content rule is simply too conservative for some edge
            // case), fall back to the first candidate rather than producing nothing at all.
            if (fragmentainers.Count == 0 && firstCandidate is not null)
                fragmentainers.Add(firstCandidate);

            return new FragmentTree(fragmentainers);
        }

        /// <summary>
        /// Collects, across every emitted slot, the facts about a box that no single slot can see: the span of
        /// fragmentainers it appears in, and the whole set of decoration rectangles its §6.2 unbroken box is
        /// the sum of.
        /// </summary>
        /// <remarks>
        /// The rectangle set is the emitter's <i>own record</i> rather than a re-read of
        /// <c>CssBox.Rectangles</c>, and that is load-bearing twice over: a box laid out again at a new
        /// position has its rectangles reset, and the multi-column engine discards a virtual pass's
        /// rectangles wholesale — so a line box a frozen slot recorded need not still be in the live
        /// dictionary at all.
        /// </remarks>
        private void RecordSpansAndRectangles(Draft draft)
        {
            _spans[draft.Key] = _spans.TryGetValue(draft.Key, out var span)
                ? (Math.Min(span.First, draft.Slot.Index), Math.Max(span.Last, draft.Slot.Index))
                : (draft.Slot.Index, draft.Slot.Index);

            var boxKey = (draft.Key.Box, draft.Key.Owner);
            var position = (draft.Slot.Index, draft.Key.Instance);

            _fragmentRange[boxKey] = _fragmentRange.TryGetValue(boxKey, out var range)
                ? (IsBefore(position, range.First) ? position : range.First,
                   IsBefore(range.Last, position) ? position : range.Last)
                : (position, position);

            if (!_fragmentsOf.TryGetValue(boxKey, out var fragments))
            {
                _fragmentsOf[boxKey] = fragments = [];
            }

            // Slot order is the walk's own (_emitted is sorted), and a slot's nested fragmentainers yield
            // their children in fill order, so appending here is already fill order.
            fragments.Add(draft);

            foreach (var (line, rect) in draft.Lines)
            {
                if (!_rectangles.TryGetValue(draft.Key, out var rectangles))
                {
                    _rectangles[draft.Key] = rectangles = [];
                }

                rectangles[line] = rect;
            }

            foreach (var child in draft.Children)
            {
                RecordSpansAndRectangles(child);
            }
        }

        /// <summary>Fill order: pagination slot first, then which nested fragmentainer of it.</summary>
        private static bool IsBefore((int Slot, int Instance) a, (int Slot, int Instance) b) =>
            a.Slot != b.Slot ? a.Slot < b.Slot : a.Instance < b.Instance;

        private void EmitSlot(int index)
        {
            var bandTop = container.PageTopOf(index);

            var slot = new Slot(
                index,
                bandTop,
                container.PageBottomOf(index),
                container.PageGeometry.GetPage(index),
                bandTop - container.MarginTop);

            var root = container.Root!;
            var hasPrintableContent = false;
            var draft = BuildDraft(root, owner: null, snapshot: null, slot,
                            nested: null, instance: 0, ref hasPrintableContent)
                        ?? EmptyRootDraft(root, slot);

            // A slot can legitimately be emitted twice: the driver's no-progress backstop lays the
            // remainder out again monolithically, over the same slot the failed pass had already frozen,
            // and InvalidateFrom re-opens a slot whose content a later pass moved. The later emission is
            // the one that describes the layout being kept.
            _emitted[index] = (slot, draft, hasPrintableContent);
            _stale.Remove(index);
            if (index > _lastEmittedSlot) _lastEmittedSlot = index;
        }

        private static void RecordChain(BreakToken? token, int slot, HashSet<(FragmentKey, int)> into)
        {
            for (var link = token; link is not null;)
            {
                into.Add((new FragmentKey(link.Box, null, 0), slot));
                link = link is BlockBreakToken { ChildToken: { } child } ? child : null;
            }
        }

        private BoxFragment Materialize(Draft draft)
        {
            var children = new List<BoxFragment>(draft.Children.Count);

            foreach (var child in draft.Children)
            {
                children.Add(Materialize(child));
            }

            var span = _spans[draft.Key];
            var bounds = ExtentOf(draft);
            var lines = LinesOf(draft, bounds);

            return new BoxFragment(
                RectOf(draft),
                draft.Box,
                draft.Slot.Index,
                draft.OriginY,
                Localize(bounds, draft.OriginY),
                draft.IsFixed,
                draft.Slot.Index == span.First
                    && !_continuedFrom.Contains((draft.Key, draft.Slot.Index)),
                draft.Slot.Index == span.Last
                    && !_continuesInto.Contains((draft.Key, draft.Slot.Index)),
                draft.IsMonolithic,
                lines,
                draft.Words,
                children,
                OverflowClipOf(draft.Box, draft.Snapshot, draft.OriginY));
        }

        /// <summary>
        /// This fragment's decoration rectangles, each with the §6.2 <see cref="SliceGeometry"/> of the
        /// unbroken box it is a slice of.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deferred to materialization, and that is the whole reason drafts hold raw rectangles rather than
        /// finished <see cref="LineFragment"/>s: the unbroken box of an inline broken across lines is the box
        /// on one infinitely long line, so its width is the sum of <i>every</i> rectangle the box produces —
        /// including the ones a later fragmentainer pass has yet to add. Computed per pass, an inline crossing
        /// a page break described a strip a page short.
        /// </para>
        /// <para>
        /// A block-level box is deferred for a second, sharper reason: its decoration area is its own border
        /// box, and a box that continues into a later fragmentainer has not had its height applied yet on the
        /// pass that freezes this slot — its bounds are still zero-height there. Read at pass time, every
        /// intermediate fragment of a multi-page block lost its background and borders entirely.
        /// </para>
        /// </remarks>
        private List<LineFragment> LinesOf(Draft draft, RRect bounds)
        {
            var lines = new List<LineFragment>(draft.Lines.Count + 1);

            if (draft.UsesOwnBounds)
            {
                if (draft.Region.Contains(bounds))
                {
                    var local = Localize(bounds, draft.OriginY);

                    // One rectangle: nothing slices it in the inline axis, so the strip is as wide as the
                    // rectangle. Its block axis is the fragmented one, which is what the concatenated strip,
                    // the band-cut FragmentRect and the two block-axis edge flags carry between them.
                    lines.Add(new LineFragment(local, null,
                        new SliceGeometry(
                            Localize(UnbrokenBlockStripOf(draft, bounds), draft.OriginY),
                            Localize(BandCut(bounds, draft.Box, draft.Region), draft.OriginY),
                            HasLeftEdge: true, HasRightEdge: true,
                            HasTopEdge: !ResumesAnEarlierFragment(draft),
                            HasBottomEdge: !ContinuesIntoALaterFragment(draft))));
                }

                return lines;
            }

            if (draft.Lines.Count == 0) return lines;

            var slices = SliceGeometriesOf(
                draft.Box, _rectangles[draft.Key], draft.Region, draft.OriginY);

            foreach (var (line, rect) in draft.Lines)
            {
                lines.Add(new LineFragment(Localize(rect, draft.OriginY), line, slices[line]));
            }

            return lines;
        }

        /// <summary>
        /// Whether this fragment's top edge is a fragmentation break rather than the box's own —
        /// §6.2's question, answered by the break record <b>and</b> by whether anything is actually on the
        /// other side of it.
        /// </summary>
        /// <remarks>
        /// Both halves are load-bearing. The record alone cannot answer it, because a §4.3 mover can
        /// relocate a box <i>after</i> the pass that recorded it as continuing — a <c>break-inside: avoid</c>
        /// card whose first lines had already been placed is laid out again whole at the top of the next
        /// page, and the earlier fragment it is still recorded as resuming has been un-emitted
        /// (<see cref="InvalidateFrom"/>). Reading the record on its own opened such a box at its own top.
        /// And geometry alone cannot answer it either, which is the whole reason the record is consulted:
        /// two column fragments of one box occupy the same block-axis range.
        /// </remarks>
        private bool ResumesAnEarlierFragment(Draft draft) =>
            draft.ContinuedFromThePrevious && HasFragmentBeside(draft, before: true);

        /// <inheritdoc cref="ResumesAnEarlierFragment"/>
        private bool ContinuesIntoALaterFragment(Draft draft) =>
            draft.ContinuesIntoTheNext && HasFragmentBeside(draft, before: false);

        private bool HasFragmentBeside(Draft draft, bool before)
        {
            if (!_fragmentRange.TryGetValue((draft.Key.Box, draft.Key.Owner), out var range)) return false;

            var position = (draft.Slot.Index, draft.Key.Instance);

            return before ? IsBefore(range.First, position) : IsBefore(position, range.Last);
        }

        /// <summary>
        /// The root fragment of a fragmentainer whose content produced nothing at all — the structural
        /// counterpart of the never-emit-a-0-page-document fallback, so every materialized
        /// fragmentainer still has a root to paint into.
        /// </summary>
        private Draft EmptyRootDraft(CssBox root, Slot slot)
        {
            var draft = new Draft(
                new FragmentKey(root, null, 0), root, slot, PageRegionOf(isFixed: false, slot),
                snapshot: null, slot.LocalOriginY);

            draft.IsMonolithic = MonolithicContent.IsMonolithic(root);

            return draft;
        }

        private Draft? BuildDraft(
            CssBox box,
            CssProxyBox? owner,
            BoxGeometrySnapshot? snapshot,
            Slot slot,
            NestedFragmentainer? nested,
            int instance,
            ref bool hasPrintableContent)
        {
            // A display:none subtree paints nothing at all, so it produces no fragments either.
            if (box.Display == CssConstants.None) return null;

            // Fixed-position content ignores the page origin and repeats identically on every page, so
            // its fragments carry raw document coordinates (CSS Position 3: a fixed box's containing
            // block is the page box itself).
            var isFixed = box.IsFixed;
            var originY = isFixed ? 0 : slot.LocalOriginY;

            // A fixed box belongs to the page rather than to any nested fragmentainer inside it: it is
            // emitted in every fragmentainer at identical coordinates, so a column's own extent says
            // nothing about where it lands.
            var region = isFixed || nested is null ? PageRegionOf(isFixed, slot) : nested.Value.Region;

            List<(CssLineBox Line, RRect Rect)> lines = [];
            List<TextFragment> words = [];
            var usesOwnBounds = false;

            // A proxy carries no content of its own - it stands in for its source subtree, whose
            // styles it copied wholesale, so painting its own decoration would draw a repeated
            // header's background twice.
            if (box is not CssProxyBox)
            {
                var rectangles = RectanglesOf(box, snapshot);

                if (rectangles.Count > 0)
                {
                    foreach (var (line, rect) in rectangles)
                    {
                        if (region.Contains(rect)) lines.Add((line, rect));
                    }
                }
                else
                {
                    // Whether it lands in this band is a question about the box's *final* bounds, so it is
                    // asked at materialization rather than here.
                    usesOwnBounds = true;
                }

                for (var i = 0; i < box.Words.Count; i++)
                {
                    // A word on a line this pass discarded belongs to the next fragmentainer, whatever
                    // position it is still carrying from the attempt that was abandoned.
                    if (box.Words[i].AwaitsTheNextFragmentainer) continue;

                    if (!TryGetWordRect(box, i, snapshot, out var rect)) continue;

                    if (region.Contains(rect))
                        words.Add(new TextFragment(Localize(rect, originY), box.Words[i]));
                }
            }

            List<Draft> children = [];

            foreach (var (childBox, childOwner, childSnapshot, childNested, childInstance)
                     in ChildrenOf(box, owner, snapshot, slot, nested, instance))
            {
                var childDraft = BuildDraft(
                    childBox, childOwner, childSnapshot, slot, childNested, childInstance, ref hasPrintableContent);

                if (childDraft is not null)
                    children.Add(childDraft);
            }

            // A box with no per-line rectangles still needs its bounds tested somewhere, and the draft has to
            // exist for that test to be run at all. Only its *current* bounds are available here, which is
            // exact for every box whose height is settled by the pass that freezes this slot - and a box whose
            // height is not settled is one that continues into a later fragmentainer, which by construction has
            // content of its own in this one.
            if (lines.Count == 0 && words.Count == 0 && children.Count == 0
                && !(usesOwnBounds && region.Contains(BoundsOf(box, snapshot))))
            {
                return null;
            }

            if (!hasPrintableContent && IsPrintableContentIn(box, snapshot, isFixed, slot))
                hasPrintableContent = true;

            _frozen.Add(box);

            var draft = new Draft(new FragmentKey(box, owner, instance), box, slot, region, snapshot, originY);

            draft.Lines.AddRange(lines);
            draft.Words.AddRange(words);
            draft.Children.AddRange(children);
            draft.IsFixed = isFixed;
            draft.IsMonolithic = MonolithicContent.IsMonolithic(box);
            draft.UsesOwnBounds = usesOwnBounds;
            draft.BoundsEndAtItsContent = nested is { } fragmentainer && fragmentainer.Continuing.Contains(box);

            // Which of the box's block-axis edges are its own, from the two records that state it: the pass's
            // own resumption chain for the page grid, and the nested fragmentainer's carry sets for a column.
            // Both are consulted for a nested fragment, because a box can resume a *page* into a column - the
            // first column of a slot has no previous column to have carried it.
            var passKey = new FragmentKey(box, null, 0);

            draft.ContinuedFromThePrevious = _continuedFrom.Contains((passKey, slot.Index))
                || (nested?.ContinuedFrom.Contains(box) ?? false);
            draft.ContinuesIntoTheNext = _continuesInto.Contains((passKey, slot.Index))
                || (nested?.Continuing.Contains(box) ?? false);

            return draft;
        }

        /// <summary>
        /// The clip an <c>overflow: hidden</c> ancestor imposes on <paramref name="box"/>, in this
        /// fragment's local space, or null when nothing on its containing-block chain clips.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The chain is walked here rather than at paint time for one reason: a box can be shown at more
        /// than one place in a document. A repeated table header is one source subtree standing in for
        /// every page's <see cref="CssProxyBox"/>, so its live boxes carry only whichever page positioned
        /// them last, and a clip read off them lands a page away and culls the whole repeated row. This
        /// walk holds that page's own <see cref="BoxGeometrySnapshot"/>, so it can resolve the clip
        /// against the position this fragment was actually built at.
        /// </para>
        /// <para>
        /// A box outside the snapshot is read live, which is correct: the only boxes whose live geometry
        /// is stale are the ones inside a proxied subtree, and those are exactly the ones the snapshot
        /// holds. That is also all the walk can reach — <c>CssLayoutEngineTable.RemoveHeaderFooterFromTree</c>
        /// detaches the source row-group, so the chain ends there and never leaves the subtree.
        /// </para>
        /// </remarks>
        private static RRect? OverflowClipOf(CssBox box, BoxGeometrySnapshot? snapshot, double originY)
        {
            var containingBlock = box.ContainingBlock;

            while (true)
            {
                if (containingBlock.Overflow == CssConstants.Hidden)
                    return Localize(PaddingEdgeOf(containingBlock, snapshot), originY);

                var next = containingBlock.ContainingBlock;
                if (ReferenceEquals(next, containingBlock)) return null;
                containingBlock = next;
            }
        }

        /// <summary>
        /// A box's padding-edge rectangle at the position <paramref name="snapshot"/> recorded for it, or
        /// its live one. Border widths are page-invariant, so only the box's extent comes from the
        /// snapshot — the geometry itself is <see cref="RenderUtils.PaddingEdgeOf"/>, shared with the one
        /// remaining box-based clip caller so the two cannot drift apart.
        /// </summary>
        private static RRect PaddingEdgeOf(CssBox box, BoxGeometrySnapshot? snapshot) =>
            RenderUtils.PaddingEdgeOf(box, BoundsOf(box, snapshot));

        /// <summary>
        /// A box's children for fragment-building purposes. This is <see cref="CssBox.Boxes"/> for
        /// every box except a <see cref="CssProxyBox"/>, whose real content is its
        /// <see cref="CssProxyBox.SourceBox"/> — deliberately kept out of the live tree so one source
        /// subtree can be repeated on many pages. Descending into it through the proxy's own captured
        /// geometry is what puts a repeating table header into the fragment tree.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A box that owns nested fragmentainers yields its children once per fragmentainer</b>, each with
        /// that one's captured geometry and its own <see cref="FragmentRegion"/>. This is what lets a box
        /// split across two multi-column columns produce two fragments: the two differ in the inline axis,
        /// which the box's own single <c>Location</c> cannot express and the captures can.
        /// </para>
        /// <para>
        /// Nested contexts are consulted at one level only — a container already inside a nested
        /// fragmentainer reads through the enclosing capture instead, so a multi-column container inside
        /// another one splits at the outer level alone. The reason is that a box's captures are keyed by
        /// pagination slot, and the inner container's columns are re-filled once per <i>outer</i> column, so
        /// two outer columns' worth of inner captures would be indistinguishable from each other.
        /// </para>
        /// </remarks>
        private IEnumerable<(CssBox Box, CssProxyBox? Owner, BoxGeometrySnapshot? Snapshot,
            NestedFragmentainer? Nested, int Instance)> ChildrenOf(
            CssBox box, CssProxyBox? owner, BoxGeometrySnapshot? snapshot, Slot slot,
            NestedFragmentainer? nested, int instance)
        {
            if (box is CssProxyBox proxy)
            {
                if (proxy.SourceGeometry is { } proxyGeometry)
                    yield return (proxy.SourceBox, proxy, proxyGeometry, nested, instance);

                yield break;
            }

            // A rowspan placeholder shows the cell that spans into it, which lives in an earlier row.
            // That cell therefore appears in the tree once per row it spans - the fragments are
            // distinct objects, so each is painted in its own place.
            if (box is CssSpacingBox spacing)
                yield return (spacing.ExtendedBox, owner, snapshot, nested, instance);

            if (nested is null
                && _nested.TryGetValue((box, slot.Index), out var fragmentainers)
                && fragmentainers.Count > 0)
            {
                for (var i = 0; i < fragmentainers.Count; i++)
                {
                    var fragmentainer = fragmentainers[i];

                    foreach (var childBox in box.Boxes)
                    {
                        if (fragmentainer.Geometry.Holds(childBox))
                            yield return (childBox, owner, fragmentainer.Geometry, fragmentainer, i + 1);
                    }
                }

                // A child no fragmentainer holds was not placed into one — an out-of-flow child, which
                // css-multicol resolves against the container rather than a column, and which the columns
                // engine lays out once at the end. It is read live and belongs to the page, exactly as it
                // did before nested fragmentainers existed.
                foreach (var childBox in box.Boxes)
                {
                    if (!HeldByAny(fragmentainers, childBox))
                        yield return (childBox, owner, snapshot, null, instance);
                }

                yield break;
            }

            foreach (var childBox in box.Boxes)
            {
                yield return (childBox, owner, snapshot, nested, instance);
            }
        }

        /// <summary>
        /// Whether this box contributes "real" printable content to <paramref name="slot"/>, per
        /// <see href="https://www.w3.org/TR/css-page-3/#renderer-defaults">CSS Paged Media Level 3
        /// §3.2</see>'s definition of a content-empty page ("a page box whose page area contains no
        /// printable content other than backgrounds and/or borders"). Fixed content is excluded: it
        /// repeats identically on every page, so counting it would make every slot — including the
        /// large decorative gaps this test exists to detect — look non-empty.
        /// </summary>
        private static bool IsPrintableContentIn(CssBox box, BoxGeometrySnapshot? snapshot, bool isFixed, Slot slot)
        {
            if (isFixed || !DomUtils.HasOwnPrintableContent(box)) return false;

            // A box's own Location/ActualBottom only reflect true page-relative geometry for
            // block-level boxes - an inline box's real per-line position lives entirely in its
            // Rectangles, while its Location/ActualBottom stay at whatever line-local value layout
            // left them at.
            var rectangles = RectanglesOf(box, snapshot);

            if (rectangles.Count > 0)
            {
                foreach (var rect in rectangles.Values)
                {
                    if (rect.Bottom >= slot.BandTop && rect.Top < slot.BandBottom) return true;
                }

                return false;
            }

            var bounds = BoundsOf(box, snapshot);
            return bounds.Bottom >= slot.BandTop && bounds.Top < slot.BandBottom;
        }


        /// <summary>
        /// <paramref name="slot"/>'s content band in document space. A fixed rectangle is measured against
        /// the band at its own unshifted position, since it does not move with the page.
        /// </summary>
        private FragmentRegion PageRegionOf(bool isFixed, Slot slot)
        {
            var top = isFixed ? container.MarginTop : slot.BandTop;

            // Left/Right null: the page grid's fragmentainers differ in the block axis only, so the inline
            // axis is not a membership question there (§2 shares one inline size across a box's fragments).
            return new FragmentRegion(top, top + slot.Geometry.BandHeight, null, null);
        }

        /// <summary>
        /// A document-space rectangle cut to <paramref name="region"/>'s content band — the box fragment
        /// <c>box-decoration-break: clone</c> wraps with its own border and padding
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">§6.2</see>). Rectangles that
        /// fit inside the band come back unchanged, which is every rectangle of an unfragmented box.
        /// </summary>
        /// <remarks>
        /// A cloning <b>ancestor</b> re-opens with its own border and padding at the band edge, and each
        /// fragment is wrapped independently — so a nested box's fragment starts inside its ancestors' and the
        /// band it may occupy is inset by them. Without that inset every level of a nested cloning stack would
        /// close on the same line, drawing each border over the last, while layout (which reserves the whole
        /// nested sum) left a gap where the inner borders should have been.
        /// </remarks>
        private RRect BandCut(RRect rect, CssBox box, FragmentRegion region) =>
            container.HasCloneDecorations
                ? region.BlockCut(rect, DomUtils.ClonedBlockStart(box.ParentBox, stopAt: null), DomUtils.ClonedBlockEnd(box.ParentBox))
                : region.BlockCut(rect, 0, 0);

        /// <summary>
        /// The <see cref="SliceGeometry"/> of each of a box's decoration rectangles — the whole unbroken
        /// box each one is a slice of, and which of its inline-axis edges are real box edges.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The unbroken box of an inline broken across lines is the box on one infinitely long line, so its
        /// width is the sum of the rectangles' widths — which is exactly right because
        /// <see cref="CssLineBox.UpdateRectangle"/> adds the leading spacing only on the box's first
        /// hosting line and the trailing spacing only on its last, counting each border and padding once.
        /// Each rectangle then carries that strip positioned so its own slice of it lines up (see
        /// <see cref="SliceGeometry"/>).
        /// </para>
        /// <para>
        /// Rectangles are ordered by position rather than by line-box index: a box contributes at most one
        /// rectangle per line box, so no two can tie, and this needs no lookup into the owning block's line
        /// list. A right-to-left block reverses the inline progression, so the strip extends rightwards
        /// from each rectangle instead of leftwards.
        /// </para>
        /// <para>
        /// <b>Only a box broken by <i>someone else's</i> line breaks gets a strip.</b> A box that owns the line
        /// boxes its rectangles are keyed by holds its own text — a <c>display: block</c> or
        /// <c>inline-block</c> <c>::before</c>, say — so those rectangles are stacked down the block axis
        /// rather than being slices along the inline one, and concatenating their widths would describe a box
        /// that does not exist. Each is its own decoration area instead.
        /// </para>
        /// <para>
        /// Every rectangle here keeps both <b>block-axis</b> edges. A box broken across lines is not sliced in
        /// that axis at all — its top and bottom borders belong to each line it appears on, which is what a
        /// wrapping inline looks like in every UA — so the block pair only ever answers for the single
        /// whole-box rectangle a block-level box produces.
        /// </para>
        /// </remarks>
        private Dictionary<CssLineBox, SliceGeometry> SliceGeometriesOf(
            CssBox box,
            IReadOnlyDictionary<CssLineBox, RRect> rectangles,
            FragmentRegion region,
            double originY)
        {
            var slices = new Dictionary<CssLineBox, SliceGeometry>(rectangles.Count);

            RRect FragmentRectOf(RRect rect) => Localize(BandCut(rect, box, region), originY);

            var ownsItsLines = false;
            foreach (var line in rectangles.Keys)
            {
                ownsItsLines = ReferenceEquals(line.OwnerBox, box);
                break;
            }

            if (rectangles.Count == 1 || ownsItsLines)
            {
                foreach (var (line, rect) in rectangles)
                {
                    var local = Localize(rect, originY);
                    slices[line] = new SliceGeometry(local, FragmentRectOf(rect), HasLeftEdge: true, HasRightEdge: true);
                }

                return slices;
            }

            var ordered = new List<KeyValuePair<CssLineBox, RRect>>(rectangles);
            ordered.Sort(static (a, b) =>
            {
                var byTop = a.Value.Y.CompareTo(b.Value.Y);
                return byTop != 0 ? byTop : a.Value.X.CompareTo(b.Value.X);
            });

            var total = 0d;
            foreach (var (_, rect) in ordered) total += rect.Width;

            var rtl = ordered[0].Key.OwnerBox.Direction == CssConstants.Rtl;

            var preceding = 0d;

            foreach (var (line, rect) in ordered)
            {
                var following = total - preceding - rect.Width;
                var strip = new RRect(rect.X - (rtl ? following : preceding), rect.Y, total, rect.Height);

                slices[line] = new SliceGeometry(
                    Localize(strip, originY),
                    FragmentRectOf(rect),
                    HasLeftEdge: Math.Abs(rect.Left - strip.Left) <= EdgeEpsilon,
                    HasRightEdge: Math.Abs(rect.Right - strip.Right) <= EdgeEpsilon);

                preceding += rect.Width;
            }

            return slices;
        }

        private static bool HeldByAny(List<NestedFragmentainer> fragmentainers, CssBox box)
        {
            foreach (var fragmentainer in fragmentainers)
            {
                if (fragmentainer.Geometry.Holds(box)) return true;
            }

            return false;
        }

        /// <summary>
        /// The whole unbroken box this fragment is a block-axis slice of — §6.2's <c>slice</c> rendering
        /// "with no breaks present", measured in the axis the break actually falls in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The page grid and a nested fragmentainer need different inputs, and one rule cannot serve
        /// both.</b> On the page grid every fragment of a box already reports the box's <i>whole</i> bounds —
        /// nothing cuts them until paint, where the page clip does it — so the strip is the bounds and summing
        /// the fragments would describe a box N pages tall. In a nested fragmentainer each fragment reports
        /// <i>its own</i> extent, because two columns of one page share a band and the extent is the only
        /// thing that tells them apart, so there the strip is exactly the concatenation.
        /// </para>
        /// <para>
        /// Positioned per fragment, the transpose of the inline rule (<see cref="SliceGeometriesOf"/>): with
        /// <c>strip.Y = rect.Y - (height of the fragments before this one)</c>, the strip's top edge coincides
        /// with the first fragment's top and its bottom edge with the last fragment's bottom, so a
        /// <c>border-radius</c>, a background layer or a <c>box-shadow</c> resolved against the strip and
        /// clipped to the fragment rounds, positions and casts at the box's true ends only.
        /// </para>
        /// </remarks>
        private RRect UnbrokenBlockStripOf(Draft draft, RRect bounds)
        {
            if (draft.Region.Left is null) return bounds;

            if (!_fragmentsOf.TryGetValue((draft.Key.Box, draft.Key.Owner), out var fragments)
                || fragments.Count < 2)
            {
                return bounds;
            }

            var total = 0d;
            var preceding = 0d;
            var reached = false;

            foreach (var fragment in fragments)
            {
                // A box with fragments on both the page grid and inside a nested fragmentainer would be
                // mixing the two rules above; layout places a box into one or the other, so this is a
                // guard rather than a case.
                if (fragment.Region.Left is null) return bounds;

                var height = ExtentOf(fragment).Height;

                if (ReferenceEquals(fragment, draft)) reached = true;
                else if (!reached) preceding += height;

                total += height;
            }

            return new RRect(bounds.X, bounds.Y - preceding, bounds.Width, total);
        }

        /// <summary>
        /// This fragment's own border box, in document space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A box that continues past this fragmentainer has not had its height applied — the epilogue that
        /// resolves it runs only on the pass that <i>completes</i> the box — so what it reports here is a
        /// zero-height box at its own top. On the page grid that is harmless: the bounds are cut to the band,
        /// and the box's later fragments are separated by the band anyway. In a nested fragmentainer, whose
        /// neighbours share one band, the block extent is all there is, so it has to come from the content
        /// this fragment actually holds.
        /// </para>
        /// <para>
        /// Measured over the <i>draft</i> tree rather than over materialized fragments, and memoized. Both
        /// follow from <see cref="UnbrokenBlockStripOf"/>: it needs every fragment's extent before the first
        /// of them is built, which a measurement taken during materialization cannot offer.
        /// </para>
        /// </remarks>
        private RRect ExtentOf(Draft draft)
        {
            if (_extents.TryGetValue(draft, out var cached)) return cached;

            var bounds = BoundsOf(draft.Box, draft.Snapshot);

            if (draft.BoundsEndAtItsContent)
            {
                var bottom = bounds.Bottom;

                foreach (var word in draft.Words)
                {
                    bottom = Math.Max(bottom, word.Rect.Bottom + draft.OriginY);
                }

                foreach (var child in draft.Children)
                {
                    bottom = Math.Max(bottom, RectOf(child).Bottom + child.OriginY);
                }

                // Under `clone` this fragment closes with its own bottom border and padding (§6.2), and
                // layout has already stopped its content short of the fragmentainer edge by that much
                // (CssRect.WouldStraddleFragmentainer). Measured from the content alone the fragment would
                // end at the last line, and the closing border would be drawn over it rather than in the
                // room reserved for it. Only this box's own share: each level of a nested cloning stack
                // closes inside its ancestors', whose own fragments are measured from this one.
                if (container.HasCloneDecorations) bottom += DomUtils.OwnClonedBlockEnd(draft.Box);

                if (bottom > bounds.Bottom)
                    bounds = RRect.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bottom);
            }

            _extents[draft] = bounds;
            return bounds;
        }

        /// <summary>
        /// The union of this fragment's own painted geometry — its decoration rectangles, else its
        /// children's — in fragmentainer-local space. The value <see cref="Fragment.Rect"/> carries,
        /// computed from the drafts so <see cref="ExtentOf"/> can ask it before anything is materialized.
        /// </summary>
        private RRect RectOf(Draft draft)
        {
            if (_rects.TryGetValue(draft, out var cached)) return cached;

            RRect? union = null;

            if (draft.UsesOwnBounds)
            {
                var bounds = ExtentOf(draft);

                if (draft.Region.Contains(bounds)) union = Localize(bounds, draft.OriginY);
            }
            else
            {
                foreach (var (_, rect) in draft.Lines)
                {
                    var local = Localize(rect, draft.OriginY);
                    union = union is null ? local : RRect.Union(union.Value, local);
                }
            }

            if (union is null)
            {
                foreach (var child in draft.Children)
                {
                    var childRect = RectOf(child);
                    union = union is null ? childRect : RRect.Union(union.Value, childRect);
                }
            }

            var fragmentRect = union ?? RRect.Empty;
            _rects[draft] = fragmentRect;

            return fragmentRect;
        }

        private static RRect BoundsOf(CssBox box, BoxGeometrySnapshot? snapshot) =>
            snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) ? geometry.Bounds : box.Bounds;

        private static IReadOnlyDictionary<CssLineBox, RRect> RectanglesOf(CssBox box, BoxGeometrySnapshot? snapshot) =>
            snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) ? geometry.Rectangles : box.Rectangles;

        /// <summary>
        /// Where a word sits in this fragmentainer, or false when it belongs to a later one.
        /// </summary>
        private static bool TryGetWordRect(CssBox box, int index, BoxGeometrySnapshot? snapshot, out RRect rect)
        {
            var word = box.Words[index];

            // A snapshot records only where a word sits, not how big it is - a repeated header's words
            // are the very same CssRect objects, measured once.
            if (snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) && index < geometry.WordOrigins.Count)
            {
                if (geometry.WordOrigins[index] is not { } origin)
                {
                    rect = RRect.Empty;
                    return false;
                }

                rect = new RRect(origin.X, origin.Y, word.Width, word.Height);
                return true;
            }

            rect = word.Rectangle;
            return true;
        }

        private static RRect Localize(RRect rect, double originY) =>
            new(rect.X, rect.Y - originY, rect.Width, rect.Height);
    }
}
