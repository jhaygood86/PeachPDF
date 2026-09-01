using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
            /// Whether the box's decoration area runs to the bottom of what it actually placed here rather
            /// than to the bottom it currently reports, for either of two reasons: it continues past a
            /// nested fragmentainer without having had its own height applied yet (see
            /// <see cref="NestedFragmentainer.Continuing"/>), or - on the page grid - its own declared bounds
            /// were pinned by an item-content commit pass before the content that overflows past them was
            /// known, so they simply do not reach the region its later content genuinely landed in (issue
            /// <see href="https://github.com/jhaygood86/PeachPDF/issues/569">#569</see>).
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
            /// The geometry layout <i>stated</i> for a fragment that holds none of the box's content, or
            /// null for an ordinary fragment. Read in place of the box's own bounds wherever this
            /// fragment's extent is asked for, because the box's bounds describe a different
            /// fragmentainer entirely. See <see cref="_continuationShells"/>.
            /// </summary>
            internal RRect? ShellRect { get; set; }

            /// <summary>
            /// The band a displaced fragment is confined to, in document space, or null for an ordinary
            /// fragment — see <see cref="RecordFragmentDisplacement"/>. Intersected into the fragment's
            /// <see cref="BoxFragment.OverflowClip"/>, because a displaced box's rectangle still spans its
            /// whole height and would otherwise redraw, under the repeated header, the strip an earlier
            /// band already showed.
            /// </summary>
            internal RRect? ConfinedTo { get; set; }

            /// <summary>
            /// How far lower than its own geometry this fragment draws — see
            /// <see cref="RecordFragmentDisplacement"/>, and 0 for an ordinary fragment.
            /// </summary>
            /// <remarks>
            /// Kept on the draft because the whole-box questions are resolved at <i>materialization</i>
            /// (see the invariant "everything defined over the whole box is resolved at materialization"),
            /// and every one of them is a membership question that has to be asked of where the geometry
            /// <b>lands</b>. With the shift reachable only inside <c>BuildDraft</c>, a displaced box whose
            /// last strip was shorter than the gaps above it tested as being in no band at all and lost
            /// its background and borders on that page entirely.
            /// </remarks>
            internal double Shift { get; set; }

            /// <summary>
            /// The box whose run this fragment is a slice of, or null for an ordinary fragment — see
            /// <see cref="ClipIsInsideTheDisplacedRun"/>, its only reader.
            /// </summary>
            internal CssBox? DisplacementRoot { get; set; }

            /// <summary>
            /// How far this fragment's <c>position: fixed</c> subtree draws from its own (single,
            /// globally-resolved) <see cref="CssBox.Location"/>, on THIS slot specifically — 0 for an
            /// ordinary fragment, and 0 for a fixed one too whenever its percentage <c>left</c>/<c>top</c>
            /// (if any) resolves to the same value on every page. A fixed box's own <c>Location</c> is
            /// computed once, against the document's base page area (<c>CommitBlockChildOffset</c>); a
            /// percentage offset is spec-required to resolve against EACH page's own area instead
            /// (CSS2.1 §10.1 / CSS Position 3), so a mixed-page-size document needs this per-slot
            /// correction on top of the single shared geometry every other slot's copy of the same fixed
            /// subtree also reads. Applied directly to document-space rects (not through
            /// <see cref="Localize"/>/<see cref="Displaced"/>): a fixed fragment's own <c>Shift</c> and
            /// <c>OriginY</c> are always 0, so document space already IS this fragment's local space.
            /// </summary>
            internal double FixedOffsetX { get; set; }

            /// <inheritdoc cref="FixedOffsetX"/>
            internal double FixedOffsetY { get; set; }

            /// <summary>
            /// How far wider/taller this fragment's <c>position: fixed</c> subtree's own extent
            /// (<see cref="FragmentEmitter.ExtentOf"/>) is on THIS slot than the single, globally-resolved
            /// size <see cref="CssBox.ActualWidth"/>/<see cref="CssBox.ActualHeight"/> already carries — 0
            /// for an ordinary fragment, and 0 for a fixed one too whenever its percentage
            /// <c>width</c>/<c>height</c> (if any) resolves to the same value on every page. Mirrors
            /// <see cref="FixedOffsetX"/>/<see cref="FixedOffsetY"/> exactly, for the box's SIZE rather
            /// than its position: a percentage width/height on a fixed box is spec-required to resolve
            /// against EACH page's own area (CSS2.1 §10.1 / CSS Position 3), same as a percentage offset,
            /// but the box's own content (lines/words, an internal wrapping algorithm) is laid out once and
            /// is NOT re-flowed to the new size — only its own outer extent is, which is exactly what feeds
            /// its background/border/clip/replaced-content painting. A non-replaced fixed box with
            /// percentage sizing and its own text/child content therefore gets a correctly resized frame
            /// with content that does not re-wrap to fit it (an accepted gap - see
            /// docs/html-css-support.md's `position: fixed` per-page notes).
            /// </summary>
            internal double FixedSizeDeltaWidth { get; set; }

            /// <inheritdoc cref="FixedSizeDeltaWidth"/>
            internal double FixedSizeDeltaHeight { get; set; }

            /// <summary>
            /// How much wider this fragment's own outer frame is on THIS slot than the single,
            /// globally-resolved <see cref="CssBox.Size"/> already carries — 0 outside a mixed-page-size
            /// document, and 0 for a box that isn't eligible at all (issue #876). Unlike
            /// <see cref="FixedSizeDeltaWidth"/> (a fixed box's own content is never re-flowed to its
            /// resized frame), this box's own text/inline content ALREADY re-wraps per fragment
            /// (<c>CssLayoutEngine.LineContentRightOf</c>, issue #143's own line-level layer) - only its
            /// outer border/background box was still pinned to whichever page originally placed it. This
            /// field is what catches the frame up to content that was already correct, computed by
            /// <see cref="ComputeInlineExtentDelta"/> using the exact same eligibility test
            /// (<c>CssLayoutEngine.IsUnconstrainedMainColumn</c>) and formula (<c>CssLayoutEngine.GetBoxWidth</c>'s
            /// own auto-width branch) the box's single global width was originally resolved with, just
            /// substituting THIS slot's own content-right edge for the document-wide one. There is no
            /// block-axis (height) counterpart: a box's height is already free to vary per fragment via
            /// <see cref="BoundsEndAtItsContent"/> - only the inline axis lacked a per-fragment seam.
            /// </summary>
            internal double InlineExtentDeltaWidth { get; set; }

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

        /// <summary>
        /// Where a box's <i>content-free</i> continuation sits — the geometry of a fragment holding none of
        /// the box's own content, in document space, stated by layout because nothing can read it off the
        /// box. <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>'s
        /// finished table cell is the case this exists for: the cells of one row are §2.1 parallel flows, so
        /// a row can continue with one of its cells already complete, and that cell's box continues with the
        /// row while its single <see cref="CssBox.Location"/> keeps describing the fragmentainer that placed
        /// it.
        /// </summary>
        /// <remarks>
        /// The slot key is what <see cref="ClearContinuationShells"/> sweeps by, and is <b>never</b> used to
        /// decide which fragmentainer a shell belongs to: the table row loop's own band counter is stale by
        /// construction (see <see cref="TableRowCursor"/>'s remarks), so membership is asked of the
        /// rectangle through <see cref="FragmentRegion.Contains"/> exactly as it is for every other piece of
        /// geometry here. Staleness in the key is harmless in the direction it errs — a low counter clears
        /// more than it had to, and the pass doing the clearing re-records.
        /// </remarks>
        private readonly Dictionary<CssBox, SortedDictionary<int, RRect>> _continuationShells =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// How far a box's own geometry is displaced in a given fragmentainer, and the content band that
        /// fragment is confined to — <see cref="RecordFragmentDisplacement"/>.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="_continuationShells"/>, the slot key here <i>is</i> membership: a displacement
        /// says where a box draws in one named fragmentainer, and the whole point is that the same
        /// rectangle draws somewhere different in the next one. There is nothing for
        /// <see cref="FragmentRegion.Contains"/> to decide from, because the un-displaced geometry is the
        /// same in every band.
        /// </remarks>
        private readonly Dictionary<CssBox, Dictionary<int, (CssBox Root, double Shift, RRect Band)>> _displacements =
            new(ReferenceEqualityComparer.Instance);

        private int _lastEmittedSlot = -1;

        /// <summary>
        /// Differential self-check: build every slot's draft tree <i>twice</i> — once with pruning
        /// allowed and once with it forced off — and throw unless the two are identical. Off unless
        /// <c>PEACHPDF_VERIFY_FRAGMENT_PRUNING=1</c> is set in the environment.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A static read once from the environment rather than the per-instance "Test-only override"
        /// idiom <see cref="HtmlContainerInt.MaxFragmentainersOverride"/> uses, and deliberately so:
        /// the point is to run the <i>whole</i> existing suite under it, and every test builds its own
        /// container, so a per-instance switch would have to be threaded through every construction
        /// site to be useful.
        /// </para>
        /// <para>
        /// Pruning is an optimization that must be invisible: it may only ever decline to walk a
        /// subtree that would have produced nothing. That is a property no assertion about a
        /// <i>particular</i> document can establish, but one this check establishes over every fixture
        /// the suite already has.
        /// </para>
        /// </remarks>
        internal static readonly bool VerifyPruningAgainstFullWalk =
            Environment.GetEnvironmentVariable("PEACHPDF_VERIFY_FRAGMENT_PRUNING") == "1";

        /// <summary>
        /// Whether this render checks itself — the environment-wide switch, or one container's own
        /// <see cref="HtmlContainerInt.VerifyFragmentPruningOverride"/>.
        /// </summary>
        private bool VerifiesPruning =>
            container.VerifyFragmentPruningOverride ?? VerifyPruningAgainstFullWalk;

        /// <summary>
        /// Set while the verification build above is running, and by the emission paths that may not
        /// write at all (<see cref="EmitReservedBlankSlots"/>, <see cref="Finish"/>'s stale-slot
        /// replay), to make <see cref="BuildDraft"/> withhold new "emitted nothing" observations for the
        /// slot being built.
        /// </summary>
        /// <remarks>
        /// Withholding <i>new</i> observations is the only thing every one of those callers actually
        /// needs. <see cref="CssBox.EmittedNothingAtOrBefore"/> only ever lets a mark recorded while
        /// processing slot <c>S</c> suppress a later query at a slot <c>&gt;= S</c> — a direction that
        /// makes writing safe within a single out-of-order sweep only in ascending order (an earlier,
        /// low-slot mark can help a later, high-slot query in the same sweep), which is exactly the
        /// order <see cref="Finish"/>'s replay was found unsafe to write in (see its own remarks; a
        /// descending reordering removes that risk but was measured to help nothing in return, since a
        /// mark made late in a descending sweep has nothing lower left to suppress). Reading an
        /// <i>existing</i>, already-validated observation (see <see cref="InvalidationHistory"/>) is
        /// gated separately, by <see cref="_forcingUnprunedReferenceWalk"/>: it describes ground behind
        /// every slot any of these callers could still be touching, which is sound out of order as much
        /// as in it.
        /// </remarks>
        private bool _pruningSuspended;

        /// <summary>
        /// Set only while <see cref="VerifyAgainstTheFullWalk"/>'s own reference <see cref="BuildDraft"/>
        /// call is running, to force it to walk every subtree regardless of any existing observation —
        /// the comparison is worthless if the "full" build is allowed to reach the same conclusions the
        /// pruned build already did.
        /// </summary>
        private bool _forcingUnprunedReferenceWalk;

        /// <summary>
        /// <see cref="_frozen"/> as it stood before the slot currently being emitted began, so the
        /// verification build can be run against the same starting state the pruned one saw. Only
        /// maintained when <see cref="VerifyPruningAgainstFullWalk"/> is on.
        /// </summary>
        private readonly HashSet<CssBox> _frozenBeforeSlot = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Boxes the slot being walked found nothing for, and boxes it found something for. Both are
        /// per slot, and the observation kept is the difference — see <see cref="RecordEmptyObservations"/>.
        /// </summary>
        private readonly HashSet<CssBox> _emptyHereThisSlot = new(ReferenceEqualityComparer.Instance);

        /// <inheritdoc cref="_emptyHereThisSlot"/>
        private readonly HashSet<CssBox> _producedSomethingThisSlot = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Every reopening <see cref="InvalidateFrom"/> has recorded, so an observation can be checked
        /// against only the reopenings that could actually have affected it.
        /// </summary>
        private readonly InvalidationHistory _invalidationHistory = new();

        /// <summary>
        /// Whether an "emitted nothing here" observation about <paramref name="box"/> could be relied on
        /// in a later slot at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The observation says "this subtree's fragments are all behind us". Four things break that,
        /// and each is excluded here rather than hedged against later:
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// <b>Out-of-flow and fixed content</b> does not sit in its DOM ancestor's run at all — an
        /// absolutely-positioned descendant can be pages away from the box that contains it, and a fixed
        /// one repeats on every page — so an ancestor's fragments are not a contiguous span.
        /// </item>
        /// <item>
        /// <b>A box reached through a proxy</b> (<paramref name="owner"/> non-null) is one subtree
        /// standing in for a repeated header on <i>every</i> page, at a different position each time.
        /// Its runs are per proxy, not per box, and its geometry lives in a captured snapshot that moves
        /// without any write to the box.
        /// </item>
        /// <item>
        /// <b>A box inside a nested fragmentainer</b> (<paramref name="nested"/> non-null) is visited
        /// once per column of the same slot, against a different snapshot and a different inline extent
        /// each time.
        /// </item>
        /// <item>
        /// <b>A displaced box</b> draws somewhere its own geometry does not say, decided per slot.
        /// </item>
        /// </list>
        /// <para>
        /// <see cref="CssBoxMarker"/> is excluded as well: it is laid out by one explicit call rather
        /// than through the block-children loop, so it is the one ordinary box kind whose visits do not
        /// line up with the walk that would observe it.
        /// </para>
        /// </remarks>
        private static bool MayBeObservedEmpty(
            CssBox box, CssProxyBox? owner, NestedFragmentainer? nested, bool isFixed,
            (CssBox Root, double Shift, RRect Band)? displacement) =>
            owner is null
            && nested is null
            && displacement is null
            && !box.IsRoot
            && ContentStaysInOneRun(box, isFixed);

        /// <summary>
        /// Whether <paramref name="box"/>'s own content occupies one contiguous run of fragmentainers,
        /// which is what makes "there is none of it here" say anything about the fragmentainers after
        /// this one.
        /// </summary>
        /// <remarks>
        /// Unlike the per-visit conditions in <see cref="MayBeObservedEmpty"/>, this is a fact about the
        /// box itself, so it — and only it — is what propagates to ancestors. The distinction matters:
        /// the children of a multi-column container are each visited once per column and so can never
        /// be observed individually, but that says nothing about whether the <i>container</i>'s own
        /// content is contiguous, and the container is exactly the box worth skipping. Letting the
        /// per-visit exclusion propagate made every multi-column container unprunable — which on a
        /// document that is mostly multi-column is the entire optimization.
        /// </remarks>
        private static bool ContentStaysInOneRun(CssBox box, bool isFixed) =>
            !isFixed
            && !box.IsOutOfFlow
            && box is not CssProxyBox and not CssSpacingBox and not CssBoxMarker;

        /// <summary>
        /// Keeps, as an observation on each box, the part of this slot's walk that found nothing —
        /// everything the walk saw empty and never afterwards saw hold anything.
        /// </summary>
        /// <param name="slotIndex">the slot just walked</param>
        /// <param name="frontier">
        /// whether this slot is the furthest one layout has reached. Only there may an empty walk be
        /// concluded from: <see cref="EmitPass"/> freezes a whole range of slots in one go, after the
        /// pass that filled them has already flowed content into every one, so at any slot below the
        /// frontier "found nothing" also describes content that is simply further down — and no write
        /// will ever come along to correct it.
        /// </param>
        private void RecordEmptyObservations(int slotIndex, bool frontier)
        {
            if (frontier)
            {
                foreach (var box in _emptyHereThisSlot)
                {
                    if (!_producedSomethingThisSlot.Contains(box)) box.RecordEmittedNothingAt(slotIndex, _invalidationHistory.Count);
                }
            }

            _emptyHereThisSlot.Clear();
            _producedSomethingThisSlot.Clear();
        }

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

            // Which children this container yields, how many times, and against which geometry, are all
            // decided by the set of fragmentainers recorded for it - so an earlier "emitted nothing
            // here" observation about the container no longer describes the same walk. Its descendants
            // need no equivalent: they only ever gained content by being laid out, which discards
            // theirs already.
            contextRoot.DiscardEmittedNothing();
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
                // Removing a fragmentainer changes the walk exactly as recording one does - but only
                // when there was one to remove. A fresh slot that never recorded anything (the common
                // case: this runs once per page for a resumed container, before that page has filled a
                // single column) must not spend an observation it never invalidated.
                if (_nested.Remove((contextRoot, only))) contextRoot.DiscardEmittedNothing();
                return;
            }

            var removedAny = false;

            foreach (var key in new List<(CssBox Root, int Slot)>(_nested.Keys))
            {
                if (ReferenceEquals(key.Root, contextRoot) && _nested.Remove(key)) removedAny = true;
            }

            if (removedAny) contextRoot.DiscardEmittedNothing();
        }

        /// <summary>
        /// Discards only the nested fragmentainers <paramref name="contextRoot"/> recorded in
        /// <paramref name="slot"/> from index <paramref name="keepFirst"/> onward, leaving the ones
        /// before it untouched.
        /// </summary>
        /// <remarks>
        /// <see cref="ClearNestedFragmentainers"/>'s single-slot form wipes the whole list, which is right
        /// only while one run of columns ever occupies a slot. A <c>column-span: all</c> element splits a
        /// multi-column container's content into independent runs that share the same
        /// <paramref name="slot"/> — an earlier run's columns are already finished and recorded by the
        /// time a later run's own balance retry needs to discard <i>its</i> abandoned attempt, and that
        /// discard must not erase the earlier run's geometry along with it.
        /// </remarks>
        internal void ClearNestedFragmentainersFrom(CssBox contextRoot, int slot, int keepFirst)
        {
            if (!_nested.TryGetValue((contextRoot, slot), out var fragmentainers)) return;
            if (fragmentainers.Count <= keepFirst) return;

            fragmentainers.RemoveRange(keepFirst, fragmentainers.Count - keepFirst);
            contextRoot.DiscardEmittedNothing();
        }

        /// <summary>
        /// States that <paramref name="box"/> occupies <paramref name="rect"/> in the fragmentainer that
        /// rectangle falls in, while holding none of its content there —
        /// <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>'s cell that
        /// finished in an earlier fragmentainer, whose box continues with its row's.
        /// </summary>
        /// <remarks>
        /// The second place layout <i>states</i> geometry rather than leaving it to be read off the boxes,
        /// and for the sharper of the two reasons. <see cref="RecordNestedFragmentainer"/> exists because a
        /// box's live geometry describes only its last column; this exists because the box has no geometry
        /// here <i>at all</i> — a continuation deliberately leaves its one <see cref="CssBox.Location"/>
        /// naming the fragmentainer that placed it, and giving it a second retracts the earlier fragment.
        /// Nothing downstream could re-derive this: a cell that finished and a cell no pass ever entered are
        /// indistinguishable from geometry alone, which is the whole reason
        /// <see cref="TableBreakToken.FinishedCells"/> exists.
        /// </remarks>
        /// <param name="box">the box whose continuation this is</param>
        /// <param name="slot">
        /// the pagination slot layout believed it was filling — bookkeeping for
        /// <see cref="ClearContinuationShells"/> only; see <see cref="_continuationShells"/> for why
        /// membership is never decided from it.
        /// </param>
        /// <param name="rect">the box's border box in that fragmentainer, in document space</param>
        internal void RecordContinuationShell(CssBox box, int slot, RRect rect)
        {
            if (!_continuationShells.TryGetValue(box, out var shells))
            {
                _continuationShells[box] = shells = [];
            }

            shells[slot] = rect;

            // A shell is content this box has in a fragmentainer it holds nothing else in, and it is
            // stated by the pass that fills that fragmentainer - which runs AFTER the slot before it was
            // emitted. So an observation made earlier cannot have accounted for it. Ancestors go too:
            // the walk only reaches this box by descending through them.
            box.DiscardEmittedNothing();
        }

        /// <summary>
        /// Discards what <paramref name="box"/> stated from <paramref name="fromSlot"/> on — or, with no
        /// slot, in every slot — because the pass about to run decides it again.
        /// </summary>
        /// <remarks>
        /// Both forms are needed, and they are <see cref="ClearNestedFragmentainers"/>' two forms for the
        /// same two reasons. A run continuing an earlier pass re-decides only the slots from the one it
        /// resumes in onward; the earlier ones were settled by the passes that filled them and are still
        /// true, which is what lets a row spanning three fragmentainers keep a shell in each. A run laid out
        /// afresh — three of the four reasons a table is laid out again — re-decides all of them.
        /// </remarks>
        internal void ClearContinuationShells(CssBox box, int? fromSlot = null)
        {
            if (fromSlot is not { } from)
            {
                if (_continuationShells.Remove(box)) box.DiscardEmittedNothing();
                return;
            }

            if (!_continuationShells.TryGetValue(box, out var shells)) return;

            var removedAny = false;

            foreach (var slot in new List<int>(shells.Keys))
            {
                if (slot >= from && shells.Remove(slot)) removedAny = true;
            }

            if (shells.Count == 0) _continuationShells.Remove(box);

            if (removedAny) box.DiscardEmittedNothing();
        }

        /// <summary>
        /// States that <paramref name="box"/> and everything under it draws <paramref name="shift"/> lower
        /// in fragmentainer <paramref name="slot"/> than its own geometry says, confined to
        /// <paramref name="band"/> — the strip that band leaves once the groups its table repeats have
        /// taken theirs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.3</see>'s "fragment
        /// the contents of monolithic elements by slicing the element's graphical representation", made
        /// answerable per band. Content taller than a band is drawn once, from one
        /// <see cref="CssBox.Location"/>, and each fragmentainer shows the strip of it that falls
        /// in that band — which is why a 620pt block already slices correctly across three pages with no
        /// machinery at all. What it cannot do unaided is <i>resume below</i> a repeated
        /// <c>&lt;thead&gt;</c>, because that means the strips are no longer contiguous in document space.
        /// A displacement is exactly that discontinuity: the box keeps its size, and each band draws it
        /// from a different origin so the strips still meet edge to edge.
        /// </para>
        /// <para>
        /// <b>It applies to the subtree, not to the box.</b> The run being sliced is a row's whole content —
        /// the cell's border and tint as much as the block inside it — and all of it has to move together
        /// or the strips disagree. <c>BuildDraft</c> therefore carries the displacement down its recursion
        /// rather than looking it up per box.
        /// </para>
        /// <para>
        /// <b>The band is not decoration.</b> A displaced box's rectangle still covers its whole height, so
        /// on any band but the first it reaches up into the room the repeated header is drawn in. Without
        /// the confinement the strip the earlier band already showed is drawn a second time, underneath the
        /// header — every word still claimed by exactly one fragmentainer, which is the class of defect the
        /// per-word census cannot see.
        /// </para>
        /// </remarks>
        /// <param name="box">the root of the run being sliced</param>
        /// <param name="slot">the fragmentainer this displacement applies to</param>
        /// <param name="shift">how far lower the box draws there</param>
        /// <param name="band">the content band the fragment is confined to, in document space</param>
        internal void RecordFragmentDisplacement(CssBox box, int slot, double shift, RRect band)
        {
            if (!_displacements.TryGetValue(box, out var bySlot))
            {
                _displacements[box] = bySlot = [];
            }

            bySlot[slot] = (box, shift, band);

            // A displacement moves where a whole subtree DRAWS without writing to any box in it, so it
            // is the one change no per-box write hook can catch - every descendant's observation has to
            // go with it.
            box.DiscardEmittedNothingIncludingDescendants();
        }

        /// <summary>
        /// Discards what <paramref name="box"/> stated from <paramref name="fromSlot"/> on — or, with no
        /// slot, in every slot — for the same two reasons as <see cref="ClearContinuationShells"/>.
        /// </summary>
        internal void ClearFragmentDisplacements(CssBox box, int? fromSlot = null)
        {
            if (fromSlot is not { } from)
            {
                if (_displacements.Remove(box)) box.DiscardEmittedNothingIncludingDescendants();
                return;
            }

            if (!_displacements.TryGetValue(box, out var bySlot)) return;

            var removedAny = false;

            foreach (var slot in new List<int>(bySlot.Keys))
            {
                if (slot >= from && bySlot.Remove(slot)) removedAny = true;
            }

            if (bySlot.Count == 0) _displacements.Remove(box);

            if (removedAny) box.DiscardEmittedNothingIncludingDescendants();
        }

        /// <summary>
        /// What <paramref name="box"/> is displaced by in <paramref name="slot"/>, or null where it states
        /// nothing there.
        /// </summary>
        private (CssBox Root, double Shift, RRect Band)? DisplacementIn(CssBox box, int slot) =>
            _displacements.TryGetValue(box, out var bySlot) && bySlot.TryGetValue(slot, out var stated)
                ? stated
                : null;

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

                // The one path that verifies against the full walk: these are the slots the pass that has
                // just ended filled, frozen while the geometry it produced is still what the box tree says.
                //
                // Only the last slot of the range, and only if the range genuinely reaches past
                // everything emitted so far, may an observation be drawn from - see
                // RecordEmptyObservations. Every earlier slot of the range still USES observations
                // already made; it just may not make new ones.
                EmitSlot(slot, mayWrite: true, mayVerify: true,
                    frontier: slot == throughSlot && slot >= _lastEmittedSlot);
            }
        }

        /// <summary>
        /// Emits any slot a directional forced break reserved as deliberately blank that lies past every
        /// slot the passes reached — the trailing <c>break-after</c> case, which is settled only once the
        /// final document height is known and so cannot be stated by a pass.
        /// </summary>
        /// <remarks>
        /// Stays conservative (<c>mayWrite: false</c>): these slots are categorically past everywhere any
        /// real content was ever bounded to reach, which would make writing here safe by a different
        /// argument than <see cref="Finish"/>'s (ordering-based, and found not to pay off there anyway —
        /// see its own remarks) — but that boundary isn't backed by an invariant anywhere today, and this
        /// pass only ever covers 0-1 slots per document (a directional break's own reserved page), so the
        /// payoff doesn't justify the verification burden. Revisit only if that changes.
        /// </remarks>
        internal void EmitReservedBlankSlots()
        {
            if (container.MaxReservedBlankSlot is not { } reserved) return;

            for (var slot = _lastEmittedSlot + 1; slot <= reserved && slot < MaxSlots; slot++)
            {
                // Runs once every pass is over, so a subtree observed empty here could never be
                // un-observed by a later layout - nothing may be concluded from it.
                EmitSlot(slot, mayWrite: false);
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
            // Deliberately after the early return, not before it: this method is reached on every
            // block-axis reposition of a box that holds fragments, which during a pass is constant, and
            // only the calls that actually re-open a frozen fragmentainer mean anything has been
            // superseded. Bumping on the rest retired every observation as fast as they were made.
            if (fromSlot > _lastEmittedSlot) return;

            // Every "emitted nothing here" observation naming a slot at or after fromSlot is void from
            // here on: re-opening a frozen fragmentainer means the driver is about to lay content out
            // again over ground at and after fromSlot, so only an observation about that ground - not
            // the whole document - could describe a layout that no longer exists. See
            // InvalidationHistory for why a suffix-minimum over every reopening's own fromSlot answers
            // this without enumerating boxes.
            _invalidationHistory.Record(fromSlot);

            for (var slot = fromSlot; slot <= _lastEmittedSlot; slot++)
            {
                if (_emitted.Remove(slot)) _stale.Add(slot);
            }
        }

        /// <summary>
        /// Re-freezes every stale slot behind <paramref name="slot"/> right now, instead of leaving it for
        /// <see cref="Finish"/>'s own replay to rediscover once the whole document is done.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="InvalidateFrom"/> is reached from several places that reopen exactly one already-frozen
        /// slot without ever moving the driver's own resumption point back to it — a pure block-axis
        /// translation (<c>CssBox</c>'s <c>Location</c> setter, via <c>OnBlockAxisRelocated</c>), a multi-
        /// column refill, a rectangle reset — none of them a pass re-entry, so none of them has a token to
        /// hand the driver for a slot it is not about to resume into. Left alone, such a slot sits in
        /// <see cref="_stale"/> until <see cref="Finish"/>, whose own replay is a full, unpruned walk from the
        /// document root for every one of them (~145,000 <c>BuildDraft</c> calls apiece on the css4.pub
        /// Icelandic Dictionary) precisely because writing a new pruning mark mid-replay cannot tell "this
        /// box's content is behind us for good" apart from "behind us only until the next stale slot in this
        /// same batch" — see <see cref="Finish"/>'s own remarks. Called here, one slot behind the pass the
        /// driver is *about* to run, that ambiguity does not exist: everything at or after <paramref name="slot"/>
        /// has not been touched by this layout yet, so nothing still to come can retroactively give a lower,
        /// already-stale slot more content. That is the same fact <see cref="EmitPass"/>'s own <c>frontier</c>
        /// argument rests on for an ordinary forward slot; a slot caught up here just reaches that state on a
        /// later call than the one that first opened it, rather than the one right after.
        /// </para>
        /// <para>
        /// Plain <see cref="EmitSlot"/>, not <see cref="EmitPass"/>: there is no meaningful incoming/outgoing
        /// resumption record to hand a slot nothing here re-enters as a pass, and none is needed —
        /// <see cref="Finish"/>'s own replay re-freezes every stale slot the identical way, without one, and
        /// no test has ever depended on continuation bookkeeping <see cref="Finish"/> itself never
        /// re-establishes for a slot it heals.
        /// </para>
        /// </remarks>
        internal void CatchUpStaleSlotsBehind(int slot)
        {
            if (_stale.Count == 0) return;

            foreach (var stale in new List<int>(_stale))
            {
                if (stale < slot) EmitSlot(stale, mayWrite: true, frontier: stale >= _lastEmittedSlot);
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
            //
            // Never writes, and the reason is the sharp one: a mark recorded while processing slot S
            // only ever suppresses a FUTURE query at a slot >= S (CssBox.EmittedNothingAtOrBefore), so
            // within a single sweep over this batch, only a mark written EARLY (at a low slot) could
            // help a query LATER in the same sweep (at a higher slot) - the natural direction is
            // ascending, not descending. But ascending order is exactly what makes writing unsafe here:
            // the lowest stale slot would run before the higher slots holding its own subtree's real
            // content have been walked at all, so a box could be wrongly marked "empty from here on" when
            // its actual content is still ahead, undiscovered, in the same batch. (Reordering to
            // descending order removes that specific risk but was tried and measured to help nothing in
            // return: marks written late in a descending sweep are only usable by slots lower than where
            // they were made, and there is nothing left to visit below them by construction - see
            // .claude/recent-fixes/ for the measurement.) Reading an existing, already-validated
            // observation remains safe and unrestricted here regardless - see
            // FragmentEmitter._forcingUnprunedReferenceWalk.
            foreach (var slot in new List<int>(_stale))
            {
                EmitSlot(slot, mayWrite: false);
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
                    Materialize(root),
                    MarginBoxes: []);

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

        /// <param name="index">the pagination slot to freeze</param>
        /// <param name="mayWrite">
        /// whether <see cref="BuildDraft"/> may collect new "emitted nothing"/"produced something"
        /// observations for this slot at all. False only for <see cref="EmitReservedBlankSlots"/>, which
        /// deliberately stays as conservative as before — see its own remarks.
        /// </param>
        /// <param name="mayVerify">
        /// whether the differential-verification oracle (<see cref="VerifyPruningAgainstFullWalk"/>) may
        /// run for this slot. Kept independent of <paramref name="mayWrite"/>: only <see cref="EmitPass"/>
        /// passes true here — <see cref="VerifyAgainstTheFullWalk"/>'s before/after frozen-state
        /// comparison assumes an ordinary, in-order pass.
        /// </param>
        /// <param name="frontier">
        /// whether this is the furthest slot layout has reached — see <see cref="RecordEmptyObservations"/>.
        /// </param>
        private void EmitSlot(int index, bool mayWrite, bool mayVerify = false, bool frontier = false)
        {
            var bandTop = container.PageTopOf(index);

            var slot = new Slot(
                index,
                bandTop,
                container.PageBottomOf(index),
                container.PageGeometry.GetPage(index),
                bandTop - container.MarginTop);

            var root = container.Root!;

            var wasSuspended = _pruningSuspended;
            var verifying = VerifiesPruning && mayVerify && !wasSuspended;

            if (verifying)
            {
                _frozenBeforeSlot.Clear();
                _frozenBeforeSlot.UnionWith(_frozen);
            }

            _pruningSuspended = wasSuspended || !mayWrite;

            var hasPrintableContent = false;
            var prunable = true;
            Draft? built;

            try
            {
                built = BuildDraft(root, owner: null, snapshot: null, slot,
                    nested: null, instance: 0, ref hasPrintableContent, ref prunable);
            }
            finally
            {
                _pruningSuspended = wasSuspended;
            }

            RecordEmptyObservations(index, frontier && mayWrite && !wasSuspended);

            if (verifying)
            {
                VerifyAgainstTheFullWalk(root, slot, built, hasPrintableContent);
            }

            var draft = built ?? EmptyRootDraft(root, slot);

            // A slot can legitimately be emitted twice: the driver's no-progress backstop lays the
            // remainder out again monolithically, over the same slot the failed pass had already frozen,
            // and InvalidateFrom re-opens a slot whose content a later pass moved. The later emission is
            // the one that describes the layout being kept.
            _emitted[index] = (slot, draft, hasPrintableContent);
            _stale.Remove(index);
            if (index > _lastEmittedSlot) _lastEmittedSlot = index;
        }

        /// <summary>
        /// Builds <paramref name="slot"/> again with pruning forced off and throws unless the result is
        /// indistinguishable from <paramref name="pruned"/> — see
        /// <see cref="VerifyPruningAgainstFullWalk"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The <see cref="_frozen"/> delta is checked as carefully as the tree itself</b>, and that is
        /// the point rather than a detail. Which boxes hold fragments is the <i>only</i> gate on whether
        /// an already-frozen slot is emitted a second time (<c>HoldsFragmentsFor</c> guards
        /// <c>HtmlContainerInt.InvalidateEmittedFragmentsFor</c>), so a pruning bug can leave every draft
        /// identical and still change the whole document's emission order. That exact failure once got
        /// past the entire suite and was caught only by a showcase pixel diff — see
        /// <c>.claude/invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md</c>.
        /// </para>
        /// <para>
        /// Rolling <see cref="_frozen"/> back between the two builds is safe: the only box that reads it
        /// during a walk is one that produced nothing (the shell gate in <see cref="BuildDraft"/>), which
        /// is by construction a box this walk never added.
        /// </para>
        /// </remarks>
        // The self-check below never runs in production - it is gated on an environment variable and a
        // test-only per-container override - and every branch of it that a coverage run could still miss
        // is a divergence report, i.e. a path reachable only when the pruning it guards is already wrong.
        // Excluded from the metric for the same reason the platform-gated lookups in MimeTypeResolver are:
        // unreachable-by-construction code should not drag the diff-coverage gate down. That it works is
        // evidenced by it having caught 22 real divergences while the pruning was being written.
        [ExcludeFromCodeCoverage]
        private void VerifyAgainstTheFullWalk(CssBox root, Slot slot, Draft? pruned, bool prunedHadPrintableContent)
        {
            var frozenAfterPruned = new HashSet<CssBox>(_frozen, ReferenceEqualityComparer.Instance);

            _frozen.Clear();
            _frozen.UnionWith(_frozenBeforeSlot);

            var fullHadPrintableContent = false;
            var fullPrunable = true;
            Draft? full;

            // Suspends reading observations AND making them, so the reference build neither benefits
            // from the pruned build's conclusions nor leaves conclusions of its own behind.
            var wasSuspended = _pruningSuspended;
            _pruningSuspended = true;
            _forcingUnprunedReferenceWalk = true;

            try
            {
                full = BuildDraft(root, owner: null, snapshot: null, slot,
                    nested: null, instance: 0, ref fullHadPrintableContent, ref fullPrunable);
            }
            finally
            {
                _pruningSuspended = wasSuspended;
                _forcingUnprunedReferenceWalk = false;
            }

            // Asked of BuildDraft's own answer, before EmitSlot's `?? EmptyRootDraft(...)` fallback -
            // that fallback would make "the root itself was pruned away" and "the document is empty"
            // indistinguishable.
            if ((pruned is null) != (full is null))
                throw PruningDiverged(slot, "the root draft", pruned is null ? "null" : "a draft", full is null ? "null" : "a draft");

            // The tree is compared first even though hasPrintableContent is the cheaper check: a
            // difference in the flag is always a consequence of some box's fragment going missing, and
            // the tree comparison names that box while the flag only says a page changed.
            if (pruned is not null && full is not null)
                AssertSameDraft(pruned, full, slot);

            if (prunedHadPrintableContent != fullHadPrintableContent)
                throw PruningDiverged(slot, "hasPrintableContent",
                    prunedHadPrintableContent.ToString(), fullHadPrintableContent.ToString());

            if (!frozenAfterPruned.SetEquals(_frozen))
                throw PruningDiverged(slot, "the set of boxes holding fragments",
                    $"{frozenAfterPruned.Count} boxes", $"{_frozen.Count} boxes");
        }

        /// <summary>
        /// Every member of <paramref name="pruned"/> that materialization or paint can read, against the
        /// same member of <paramref name="full"/>.
        /// </summary>
        /// <remarks>
        /// Deliberately exhaustive rather than "the rectangles look right": <see cref="Draft.UsesOwnBounds"/>,
        /// <see cref="Draft.BoundsEndAtItsContent"/>, <see cref="Draft.ShellRect"/>, <see cref="Draft.Shift"/>,
        /// <see cref="Draft.ConfinedTo"/> and <see cref="Draft.DisplacementRoot"/> are read only at
        /// materialization, so a comparison of fragment rectangles alone would pass while the decoration,
        /// the clip or the band a fragment is confined to had silently changed.
        /// </remarks>
        // The self-check below never runs in production - it is gated on an environment variable and a
        // test-only per-container override - and every branch of it that a coverage run could still miss
        // is a divergence report, i.e. a path reachable only when the pruning it guards is already wrong.
        // Excluded from the metric for the same reason the platform-gated lookups in MimeTypeResolver are:
        // unreachable-by-construction code should not drag the diff-coverage gate down. That it works is
        // evidenced by it having caught 22 real divergences while the pruning was being written.
        [ExcludeFromCodeCoverage]
        private static void AssertSameDraft(Draft pruned, Draft full, Slot slot)
        {
            if (!ReferenceEquals(pruned.Box, full.Box))
                throw PruningDiverged(slot, "the box a draft is for", pruned.Box.ToString(), full.Box.ToString());

            var box = pruned.Box;

            Same(pruned.Key, full.Key, "Key");
            Same(pruned.Region, full.Region, "Region");
            Same(pruned.OriginY, full.OriginY, "OriginY");
            Same(pruned.IsFixed, full.IsFixed, "IsFixed");
            Same(pruned.IsMonolithic, full.IsMonolithic, "IsMonolithic");
            Same(pruned.BoundsEndAtItsContent, full.BoundsEndAtItsContent, "BoundsEndAtItsContent");
            Same(pruned.ContinuesIntoTheNext, full.ContinuesIntoTheNext, "ContinuesIntoTheNext");
            Same(pruned.ContinuedFromThePrevious, full.ContinuedFromThePrevious, "ContinuedFromThePrevious");
            Same(pruned.UsesOwnBounds, full.UsesOwnBounds, "UsesOwnBounds");
            Same(pruned.ShellRect, full.ShellRect, "ShellRect");
            Same(pruned.ConfinedTo, full.ConfinedTo, "ConfinedTo");
            Same(pruned.Shift, full.Shift, "Shift");

            if (!ReferenceEquals(pruned.Snapshot, full.Snapshot))
                throw PruningDiverged(slot, $"the captured geometry of {box}", "one snapshot", "another");

            if (!ReferenceEquals(pruned.DisplacementRoot, full.DisplacementRoot))
                throw PruningDiverged(slot, $"the displacement root of {box}",
                    pruned.DisplacementRoot?.ToString() ?? "none", full.DisplacementRoot?.ToString() ?? "none");

            Same(pruned.Lines.Count, full.Lines.Count, "the number of decoration rectangles");

            for (var i = 0; i < pruned.Lines.Count; i++)
            {
                if (!ReferenceEquals(pruned.Lines[i].Line, full.Lines[i].Line) || pruned.Lines[i].Rect != full.Lines[i].Rect)
                    throw PruningDiverged(slot, $"decoration rectangle {i} of {box}",
                        pruned.Lines[i].Rect.ToString(), full.Lines[i].Rect.ToString());
            }

            Same(pruned.Words.Count, full.Words.Count, "the number of words");

            for (var i = 0; i < pruned.Words.Count; i++)
            {
                if (pruned.Words[i] != full.Words[i])
                    throw PruningDiverged(slot, $"word {i} of {box}",
                        pruned.Words[i].ToString(), full.Words[i].ToString());
            }

            Same(pruned.Children.Count, full.Children.Count, "the number of child fragments");

            for (var i = 0; i < pruned.Children.Count; i++)
            {
                AssertSameDraft(pruned.Children[i], full.Children[i], slot);
            }

            void Same<T>(T a, T b, string what)
            {
                if (!EqualityComparer<T>.Default.Equals(a, b))
                    throw PruningDiverged(slot, $"{what} of {box}", a?.ToString() ?? "null", b?.ToString() ?? "null");
            }
        }

        // The self-check below never runs in production - it is gated on an environment variable and a
        // test-only per-container override - and every branch of it that a coverage run could still miss
        // is a divergence report, i.e. a path reachable only when the pruning it guards is already wrong.
        // Excluded from the metric for the same reason the platform-gated lookups in MimeTypeResolver are:
        // unreachable-by-construction code should not drag the diff-coverage gate down. That it works is
        // evidenced by it having caught 22 real divergences while the pruning was being written.
        [ExcludeFromCodeCoverage]
        private static InvalidOperationException PruningDiverged(Slot slot, string what, string pruned, string full) =>
            new($"Fragment pruning changed the output of slot {slot.Index}: {what} is '{pruned}' with pruning " +
                $"and '{full}' without it. Pruning must only ever decline to walk a subtree that would " +
                $"have produced nothing.");

        /// <summary>
        /// Walks a break-token chain, marking every box it names as continuing in <paramref name="slot"/>.
        /// A <see cref="BlockBreakToken"/> chain is linear (box → its one continuing child → ...), but a
        /// fan-out token (<see cref="TableBreakToken"/> and its <see cref="BreakToken.FanOutContinuations"/>
        /// siblings) names several §2.1 parallel flows at once — each is walked in turn, recursively, since
        /// a cell/item's own continuation can itself be a linear chain or a further fan-out (a table nested
        /// in a table cell, say).
        /// </summary>
        private static void RecordChain(BreakToken? token, int slot, HashSet<(FragmentKey, int)> into)
        {
            for (var link = token; link is not null;)
            {
                into.Add((new FragmentKey(link.Box, null, 0), slot));

                if (link is BlockBreakToken { ChildToken: { } child })
                {
                    link = child;
                    continue;
                }

                foreach (var continuation in link.FanOutContinuations)
                {
                    RecordChain(continuation, slot, into);
                }

                link = null;
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
            var (overflowClip, overflowClipCurve) = ClipOf(draft);

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
                overflowClip,
                overflowClipCurve);
        }

        /// <summary>
        /// What this fragment is clipped to in its own local space: the nearest <c>overflow: hidden</c>
        /// ancestor's padding edge (plus its rounded-corner curve, if any), the band a displaced fragment
        /// is confined to, or the tighter of the rectangular two where a displaced run also sits inside a
        /// clipping ancestor.
        /// </summary>
        private static (RRect? Clip, OverflowClipCurve? Curve) ClipOf(Draft draft)
        {
            // Which origin the ancestor's clip is localized against depends on whether that ancestor moves
            // with this fragment. One inside the displaced run does, so it is already in the box's own
            // displaced space; one outside it - the ordinary case, since the run being sliced is a table
            // row and the chain leaves it almost at once - does not, and localizing its clip against the
            // displaced origin pushes it down by the shift. Measured as content drawn a repeated footer's
            // worth past the bottom edge of an `overflow: hidden` div wrapping the table.
            // Default to the draft's own origin, and substitute the slot's *only* for a fragment that is
            // actually displaced. The two are equal for an undisplaced ordinary box, but not for a fixed
            // one - its origin is 0, so reaching for the slot's unconditionally moved every
            // `overflow: hidden` clip on fixed content by a page origin. Invisible in the suite and in
            // both rasterizers; caught by acid2's content stream, whose fixed bars are clipped.
            var displacedPastItsClip = draft.Shift != 0 && !ClipIsInsideTheDisplacedRun(draft);

            var overflow = OverflowClipOf(
                draft.Box, draft.Snapshot, displacedPastItsClip ? draft.Slot.LocalOriginY : draft.OriginY);

            RRect? overflowRect = null;
            OverflowClipCurve? curve = null;
            if (overflow is { } o)
            {
                overflowRect = o.Rect;
                if (o.Radii is { } radii) curve = new OverflowClipCurve(o.Rect, radii);
            }

            if (draft.ConfinedTo is not { } band) return (overflowRect, curve);

            // The band is stated in document space and, unlike everything else on a displaced draft, is a
            // fact about the *fragmentainer* rather than about the box - so it is localized against the
            // slot's own origin, not the displaced one the box draws from. It never touches the curve:
            // the curve belongs to the clipping ancestor's own corners, and stays a subset of its
            // (unconfined) Rect regardless of how this rectangular band narrows the fragment's clip.
            var confinement = Localize(band, draft.Slot.LocalOriginY);

            var clip = overflowRect is { } r ? RRect.Intersect(r, confinement) : confinement;
            return (clip, curve);
        }

        /// <summary>
        /// Whether the nearest <c>overflow: hidden</c> box on this fragment's containing-block chain lies
        /// inside the run being sliced, and so is displaced along with it.
        /// </summary>
        private static bool ClipIsInsideTheDisplacedRun(Draft draft)
        {
            if (draft.DisplacementRoot is not { } root) return false;

            var containingBlock = draft.Box.ContainingBlock;

            while (true)
            {
                if (containingBlock.Overflow.Value == Overflow.Hidden)
                    return IsSelfOrAncestor(root, containingBlock);

                var next = containingBlock.ContainingBlock;
                if (ReferenceEquals(next, containingBlock)) return false;
                containingBlock = next;
            }
        }

        /// <summary>Whether <paramref name="box"/> is <paramref name="root"/> or sits beneath it.</summary>
        private static bool IsSelfOrAncestor(CssBox root, CssBox box)
        {
            for (var walk = box; walk is not null; walk = walk.ParentBox)
            {
                if (ReferenceEquals(walk, root)) return true;
            }

            return false;
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
                if (draft.Region.Contains(Displaced(bounds, draft.Shift)))
                {
                    var local = Localize(bounds, draft.OriginY);

                    // One rectangle: nothing slices it in the inline axis, so the strip is as wide as the
                    // rectangle. Its block axis is the fragmented one, which is what the concatenated strip,
                    // the band-cut FragmentRect and the two block-axis edge flags carry between them.
                    lines.Add(new LineFragment(local, null,
                        new SliceGeometry(
                            Localize(UnbrokenBlockStripOf(draft, bounds), draft.OriginY),
                            Localize(BandCut(bounds, draft.Box, draft.Region, draft.Shift), draft.OriginY),
                            HasLeftEdge: true, HasRightEdge: true,
                            HasTopEdge: !ResumesAnEarlierFragment(draft),
                            HasBottomEdge: !ContinuesIntoALaterFragment(draft))));
                }

                return lines;
            }

            if (draft.Lines.Count == 0) return lines;

            var slices = SliceGeometriesOf(
                draft.Box, _rectangles[draft.Key], draft.Region, draft.OriginY, draft.Shift);

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
            (draft.ContinuedFromThePrevious || draft.ShellRect is not null)
            && HasFragmentBeside(draft, before: true);

        /// <inheritdoc cref="ResumesAnEarlierFragment"/>
        private bool ContinuesIntoALaterFragment(Draft draft) =>
            (draft.ContinuesIntoTheNext || HasShellBeyond(draft))
            && HasFragmentBeside(draft, before: false);

        /// <summary>
        /// The rectangle stated for <paramref name="box"/> in <paramref name="region"/>, or null when
        /// nothing was stated there.
        /// </summary>
        /// <remarks>
        /// Asked of the rectangle rather than of the slot the caller recorded against, so a stale band
        /// counter cannot make a stated fragment land nowhere. See <see cref="_continuationShells"/>.
        /// </remarks>
        private RRect? ShellIn(CssBox box, FragmentRegion region)
        {
            if (!_continuationShells.TryGetValue(box, out var shells)) return null;

            foreach (var rect in shells.Values)
            {
                if (region.Contains(rect)) return rect;
            }

            return null;
        }

        /// <summary>
        /// Whether a fragment was stated for this draft's box in a <i>later</i> fragmentainer, which is
        /// §6.2's block-end break for a box whose continuation layout states rather than places.
        /// </summary>
        /// <remarks>
        /// Asked here rather than recorded on the draft, because it cannot be known when the draft is
        /// built: the slot a box continues into is filled by a <i>later</i> pass than the one that freezes
        /// this slot, so the statement does not exist yet. That is the general rule that everything defined
        /// over the whole box is resolved at materialization. Expressed as a coordinate comparison because
        /// bands are contiguous, so "a later fragmentainer" and "at or past this region's bottom" are the
        /// same statement — and the coordinate form owes nothing to the row loop's band counter.
        /// </remarks>
        private bool HasShellBeyond(Draft draft)
        {
            if (!_continuationShells.TryGetValue(draft.Key.Box, out var shells)) return false;

            foreach (var rect in shells.Values)
            {
                if (rect.Top >= draft.Region.Bottom - BandOverlapEpsilon) return true;
            }

            return false;
        }

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

            draft.IsMonolithic = MonolithicContent.IsMonolithicForFragmentation(root);

            return draft;
        }

        /// <remarks>
        /// <c>subtreePrunable</c> is set to false by the walk when anything at or below the box means an
        /// "emitted nothing here" observation about it could not be relied on later — see
        /// <see cref="MayBeObservedEmpty"/>. It is accumulated on the way back up, so a single
        /// out-of-flow descendant makes every ancestor unprunable too.
        /// </remarks>
        private Draft? BuildDraft(
            CssBox box,
            CssProxyBox? owner,
            BoxGeometrySnapshot? snapshot,
            Slot slot,
            NestedFragmentainer? nested,
            int instance,
            ref bool hasPrintableContent,
            ref bool subtreePrunable,
            (CssBox Root, double Shift, RRect Band)? displacement = null,
            (double Dx, double Dy) fixedOffset = default)
        {
            // A display:none subtree paints nothing at all, so it produces no fragments either.
            if (box.DerivedStyle.ActualDisplay == Keywords.None) return null;

            // Fixed-position content ignores the page origin and repeats identically on every page, so
            // its fragments carry raw document coordinates (CSS Position 3: a fixed box's containing
            // block is the page box itself).
            var isFixed = box.IsFixed;

            // Established exactly where a box IS the fixed root (not merely descends from one - only the
            // root's own left/top ever resolve against the page), and inherited unchanged by descendants
            // the same way `displacement` is: the whole subtree moves as one rigid unit, since only the
            // fixed root's own position is ever page-relative.
            if (box.Position.Value is PositionMode.Fixed)
                fixedOffset = ComputeFixedPageOffset(box, slot);

            // Unlike fixedOffset, deliberately NOT threaded to descendants: a percentage width/height
            // resize is this box's own outer extent only (ExtentOf), never its content's - see
            // Draft.FixedSizeDeltaWidth's own remarks on why the subtree's own layout is not re-flowed.
            var fixedSizeDelta = box.Position.Value is PositionMode.Fixed
                ? ComputeFixedSizeOverride(box, slot)
                : default;

            // Also not threaded to descendants - unlike the fixed case above, this is deliberate for a
            // different reason: each descendant box asks this same question independently, of its OWN
            // containing block (exactly as CssLayoutEngine.GetBoxWidth/LineContentRightOf already do), so
            // a nested box that isn't itself eligible (its containing block isn't root/html/body) simply
            // gets 0 here on its own, without needing to inherit anything from an ancestor that IS
            // eligible - see ComputeInlineExtentDelta's own remarks.
            var inlineExtentDeltaWidth = ComputeInlineExtentDelta(box, slot);

            // A run being sliced across bands displaces its whole subtree, so an inherited displacement
            // stands until a box states one of its own - which only the root of such a run does.
            //
            // A fixed box is not part of that run even when it descends from one: its containing block is
            // the page box, so it does not move with the content being sliced and is not confined to that
            // content's strip. Dropping the displacement here rather than only from originY is what keeps
            // the two halves agreeing - the membership test below asks about the *displaced* rectangle,
            // so a fixed box that kept the shift would be claimed by a band a strip away from the one it
            // is drawn in, and would then be clipped to that strip as well.
            displacement = isFixed ? null : DisplacementIn(box, slot.Index) ?? displacement;
            var shift = displacement?.Shift ?? 0;

            // Subtracting the displacement here is what draws the box lower in this fragmentainer than its
            // own geometry says, and it reaches every coordinate Localize touches - words, lines, bounds -
            // in one place. Membership is the other half and cannot ride on it: the region test below is
            // asked of where the geometry *lands*, so it takes the displaced rectangle explicitly.
            var originY = isFixed ? 0 : slot.LocalOriginY - shift;

            // A fixed box belongs to the page rather than to any nested fragmentainer inside it: it is
            // emitted in every fragmentainer at identical coordinates, so a column's own extent says
            // nothing about where it lands.
            var region = isFixed || nested is null ? PageRegionOf(isFixed, slot) : nested.Value.Region;

            // Whether an "emitted nothing here" observation about this box could be relied on later at
            // all. Asked before the observation is read as well as before one is made, so a box that
            // could never be marked is never skipped on the strength of a stale mark either.
            //
            // Two separate facts, and only the second travels: whether THIS visit could observe the box
            // (per-visit - which proxy, which column), and whether the box's content is contiguous at
            // all (a property of the box, and therefore of every ancestor that contains it).
            var ownPrunable = MayBeObservedEmpty(box, owner, nested, isFixed, displacement);
            var contiguous = ContentStaysInOneRun(box, isFixed);

            // Skip the whole subtree: the emitter has already walked it once this layout, found it
            // empty at a slot at or before this one, and nothing has written to it since (every write
            // that could give it content discards the observation - see CssBox.DiscardEmittedNothing) -
            // and no reopening since has started at or before the slot the observation names (see
            // InvalidationHistory).
            //
            // Sound only because the observation is made under the far stricter conditions in
            // RecordEmptyObservations: the box had ALREADY produced a fragment, so it sits behind the
            // layout frontier and its remaining fragments are behind it too. A box layout has not
            // reached yet is equally empty and must NOT be concluded about from the same evidence -
            // within one EmitPass range the emitter walks slots the pass has already flowed content
            // into, so "nothing here yet" and "nothing here ever" are indistinguishable at that point.
            //
            // Gated on _forcingUnprunedReferenceWalk rather than _pruningSuspended: an out-of-order
            // replay (reserved blank slots, Finish's stale-slot replay) may not draw new conclusions, but
            // an existing, still-valid one describes ground behind every slot such a replay could still
            // be filling, so reading it is exactly as sound out of order as in it.
            if (ownPrunable && !_forcingUnprunedReferenceWalk && box.EmittedNothingAtOrBefore(slot.Index, _invalidationHistory))
            {
                return null;
            }

            List<(CssLineBox Line, RRect Rect)> lines = [];
            List<TextFragment> words = [];
            var usesOwnBounds = false;
            RRect? shellRect = null;

            // A percentage left/top on a fixed box resolves against THIS page's own area (see
            // ComputeFixedPageOffset), so the delta is applied to the raw rect before anything else reads
            // it - both the region-membership tests below and the value ultimately stored. A no-op
            // (returns rect unchanged) whenever fixedOffset is (0, 0), which covers every box that isn't
            // fixed at all and every fixed box whose offset happens to resolve the same on every page.
            RRect Shifted(RRect r) =>
                fixedOffset is (0, 0) ? r : new RRect(r.X + fixedOffset.Dx, r.Y + fixedOffset.Dy, r.Width, r.Height);

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
                        var shiftedRect = Shifted(rect);
                        if (region.Contains(Displaced(shiftedRect, shift))) lines.Add((line, shiftedRect));
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
                    var shiftedRect = Shifted(rect);

                    if (ClaimsWord(Displaced(shiftedRect, shift), slot.Index, region, isFixed))
                        words.Add(new TextFragment(Localize(shiftedRect, originY), box.Words[i]));
                }
            }

            List<Draft> children = [];

            foreach (var (childBox, childOwner, childSnapshot, childNested, childInstance)
                     in ChildrenOf(box, owner, snapshot, slot, nested, instance))
            {
                // A child that cannot be relied on makes this box unreliable too: the observation is
                // about the whole subtree, so it is only as good as its weakest member.
                var childPrunable = true;

                var childDraft = BuildDraft(
                    childBox, childOwner, childSnapshot, slot, childNested, childInstance,
                    ref hasPrintableContent, ref childPrunable, displacement, fixedOffset);

                contiguous &= childPrunable;

                if (childDraft is not null)
                    children.Add(childDraft);
            }

            // A box with no per-line rectangles still needs its bounds tested somewhere, and the draft has to
            // exist for that test to be run at all. Only its *current* bounds are available here, which is
            // exact for every box whose height is settled by the pass that freezes this slot - and a box whose
            // height is not settled is one that continues into a later fragmentainer, which by construction has
            // content of its own in this one.
            var ownBoundsCoverRegion = usesOwnBounds && region.Contains(Displaced(Shifted(BoundsOf(box, snapshot)), shift));

            if (lines.Count == 0 && words.Count == 0 && children.Count == 0 && !ownBoundsCoverRegion)
            {
                // A shell continues a fragment; it never invents one. Requiring the box to already hold a
                // frozen fragment somewhere is what keeps this from changing _frozen membership, and so
                // from changing which frozen slots are emitted a second time - see the invariant
                // "which drafts exist decides whether a frozen slot is emitted again". The set is
                // monotone and the fragmentainer a box began in is always frozen before the one it
                // continues into is emitted, so this never rejects a shell that should stand.
                if (!_frozen.Contains(box) || ShellIn(box, region) is not { } stated)
                {
                    // Nothing here. Two quite different things can make that a fact about the box
                    // rather than about where the walk happens to be, and one of them has to hold:
                    //
                    //  - it is already frozen, so it is BEHIND the layout frontier: it has had its
                    //    fragments, they were contiguous, and this slot is past them; or
                    //  - layout has never written to it at all, so it is AHEAD of the frontier and holds
                    //    no positioned content anywhere yet.
                    //
                    // What is excluded is the box in between - reached, laid out, and simply not here -
                    // whose content may be in a slot this same EmitPass range is about to freeze.
                    //
                    // The offer is provisional: RecordEmptyObservations makes the final call once the
                    // slot is fully walked, because one box can be visited several times in one slot.
                    if (ownPrunable && contiguous && !_pruningSuspended
                        && (_frozen.Contains(box) || box.NeverTouchedThisLayout))
                    {
                        _emptyHereThisSlot.Add(box);
                    }

                    subtreePrunable &= contiguous;
                    return null;
                }

                shellRect = stated;

                // One whole-box rectangle by construction: a shell holds no content, so there are no
                // per-line rectangles for it to be a set of.
                usesOwnBounds = true;
            }

            // A box that genuinely holds content here - real children, not the pure-shell case just above -
            // whose own declared bounds may not reach far enough to cover it: not a nested fragmentainer
            // (that case is handled below via BoundsEndAtItsContent's other arm), but a page-grid box whose
            // Width/Height an item-content commit pass pinned before the content that overflows past them was
            // known (ItemContentCommit.CommitLayout pins a flex/grid item's content-box size once, on its
            // first, fresh commit, and never revisits it on a later, resumed one - see its own remarks). Its
            // content still fragments and lands in later slots regardless, so this fragment's decoration is
            // extended from what it actually holds here, the same way a nested fragmentainer's continuing box
            // already is. Unconditional whenever there is real content to extend from, not only when the
            // box's own bounds miss this region entirely - a pinned box's declared bounds can still land a
            // sliver inside the right region while the bulk of what it actually holds here runs well past
            // that sliver (ExtentOf only ever grows the bottom, so this is a no-op wherever the box's own
            // bounds already reach far enough on their own). Closes issue #569.
            var boundsEndAtContentOnThePageGrid = shellRect is null && usesOwnBounds
                && nested is null && (children.Count > 0 || words.Count > 0);

            // A shell is backgrounds and borders and nothing else, which CSS Paged Media Level 3 §3.2
            // excludes from printable content by name - so it can never on its own make a slot into a page.
            if (shellRect is null && !hasPrintableContent && IsPrintableContentIn(box, snapshot, isFixed, slot))
                hasPrintableContent = true;

            _frozen.Add(box);

            // This box does hold something here. Recorded because one box can be reached more than once
            // in a single slot - once per multi-column column, once per repeating-header proxy, and a
            // rowspan cell once per row it spans - so an earlier visit finding nothing says nothing
            // about the slot as a whole. RecordEmptyObservations subtracts this set from the empty one.
            if (!_pruningSuspended) _producedSomethingThisSlot.Add(box);

            subtreePrunable &= contiguous;

            var draft = new Draft(new FragmentKey(box, owner, instance), box, slot, region, snapshot, originY);

            draft.Lines.AddRange(lines);
            draft.Words.AddRange(words);
            draft.Children.AddRange(children);
            draft.IsFixed = isFixed;
            draft.IsMonolithic = MonolithicContent.IsMonolithicForFragmentation(box);
            draft.UsesOwnBounds = usesOwnBounds;
            draft.ShellRect = shellRect;
            draft.ConfinedTo = displacement?.Band;
            draft.Shift = shift;
            draft.DisplacementRoot = displacement?.Root;
            draft.FixedOffsetX = fixedOffset.Dx;
            draft.FixedOffsetY = fixedOffset.Dy;
            draft.FixedSizeDeltaWidth = fixedSizeDelta.DeltaWidth;
            draft.FixedSizeDeltaHeight = fixedSizeDelta.DeltaHeight;
            draft.InlineExtentDeltaWidth = inlineExtentDeltaWidth;
            draft.BoundsEndAtItsContent = boundsEndAtContentOnThePageGrid
                || (nested is { } fragmentainer && fragmentainer.Continuing.Contains(box));

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
        /// Whether the fragmentainer of pagination slot <paramref name="slotIndex"/> claims the word at
        /// <paramref name="rect"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Only a line layout could have moved belongs to one fragmentainer alone.</b> Where
        /// <c>CssRect.WouldStraddleFragmentainer</c> answered "no, it fits" — which it does for an overhang
        /// of up to <see cref="HtmlContainerInt.PageBoundaryEpsilon"/> — §4.1 has made the line the unit and
        /// the line is wholly the earlier band's, whatever <see cref="FragmentRegion.Contains"/>'s much finer
        /// <see cref="BandOverlapEpsilon"/> says about the sliver hanging past the boundary. Asking
        /// <see cref="HtmlContainerInt.SlotStartingAt"/> — the convention layout used
        /// (<c>BandStartingAt(Top)</c>) — settles both with one tolerance rather than two that agree over
        /// most of their range, and stops the page's last line being drawn again above the next page's
        /// content top (<see href="https://github.com/jhaygood86/PeachPDF/issues/446">#446</see>).
        /// </para>
        /// <para>
        /// <b>A line layout never had the chance to move is a different case, and the reason the tie-break is
        /// conditional.</b> A flex or grid item's content is laid out under
        /// <see cref="HtmlContainerInt.SuppressWordPageBreaks"/> and never revisited when
        /// <c>AssignLocations</c> translates it, and <c>MonolithicContent.FitsNoFragmentainer</c> keeps
        /// anything taller than the band exactly where it is — so such a line can overhang by many points,
        /// with no fragmentainer of its own to be whole in. Both bands must keep it: the earlier one shows
        /// the sliver that fits, the later one the remainder, and that second claim is the only reason the
        /// content survives the boundary at all. Applied unconditionally, the tie-break deleted it — measured
        /// at 45 words, one line per break, on a four-page flex document
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/477">#477</see>).
        /// <see cref="HtmlContainerInt.FallsPast"/> is the "layout could not fix this" test, in the same
        /// tolerance as the rest — though deliberately a looser form of it than layout's own: the emitter
        /// drops <c>MonolithicContent.ClonedBlockInsets</c>' bottom inset, and asks the page band even
        /// inside a column, where layout asks the column's. Both only ever make it fire more readily, which
        /// is safe because it is intersected with the region test and so can still only remove claims.
        /// </para>
        /// <para>
        /// It is a <i>tie-break on top of</i> the region test rather than a replacement for it, and that is
        /// deliberate: the region is also the inline-axis test that tells one multi-column column from
        /// another, which no page-grid slot index can speak to, and <see cref="PageGeometryTable.PageIndexOf"/>
        /// clamps everything above the first band's top into slot 0 — so asked alone it would hand slot 0
        /// every word a pass has not positioned yet, which is <see href="https://github.com/jhaygood86/PeachPDF/issues/433">#433</see>
        /// arriving by another route (measured: 404 boxes frozen into the first slot's first emission where
        /// 100 belong there). Intersecting the two can only ever <i>remove</i> a claim, never invent one.
        /// </para>
        /// <para>
        /// Fixed content is exempt from the tie-break. It repeats at unshifted document coordinates in every
        /// slot, so the one slot its own Y falls in would name a single page instead of all of them.
        /// </para>
        /// </remarks>
        private bool ClaimsWord(RRect rect, int slotIndex, FragmentRegion region, bool isFixed) =>
            region.Contains(rect)
            && (isFixed
                || container.SlotStartingAt(rect.Top) == slotIndex
                || HtmlContainerInt.FallsPast(rect.Bottom, container.BandStartingAt(rect.Top)));

        /// <summary>
        /// <paramref name="rect"/> where a displacement puts it — the rectangle every membership question
        /// is asked of, since a displaced box lands somewhere its own geometry does not say.
        /// </summary>
        /// <param name="rect">the box's own rectangle, in document space</param>
        /// <param name="shift">how far lower it draws in the fragmentainer being built</param>
        private static RRect Displaced(RRect rect, double shift) =>
            shift == 0 ? rect : new RRect(rect.X, rect.Y + shift, rect.Width, rect.Height);

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
        private static (RRect Rect, BorderRadii? Radii)? OverflowClipOf(CssBox box, BoxGeometrySnapshot? snapshot, double originY)
        {
            var containingBlock = box.ContainingBlock;

            while (true)
            {
                if (containingBlock.Overflow.Value == Overflow.Hidden)
                {
                    var borderBoxRect = BoundsOf(containingBlock, snapshot);
                    var paddingRect = RenderUtils.PaddingEdgeOf(containingBlock, borderBoxRect);
                    var radii = containingBlock.IsRounded
                        ? containingBlock.ComputeInnerRadii(borderBoxRect, paddingRect,
                            containingBlock.ActualBorderLeftWidth, containingBlock.ActualBorderTopWidth,
                            containingBlock.ActualBorderRightWidth, containingBlock.ActualBorderBottomWidth)
                        : (BorderRadii?)null;
                    return (Localize(paddingRect, originY), radii);
                }

                var next = containingBlock.ContainingBlock;
                if (ReferenceEquals(next, containingBlock)) return null;
                containingBlock = next;
            }
        }

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
        /// a zero-based window the size of its OWN page's sheet, not this slot's document-space band -
        /// a fixed box is laid out exactly once (its raw rect is never re-anchored per slot: <c>Localize</c>
        /// passes <c>originY: 0</c> for fixed content, and <see cref="ComputeFixedPageOffset"/> resolves its
        /// offset against this slot's OWN basis, not the slot's cumulative document position), so every
        /// slot's fixed content lives in the same zero-anchored frame regardless of which page it repeats
        /// onto. Measuring membership against the slot's actual cumulative <c>BandTop</c> - as an earlier
        /// version of this method did - matched only the slot whose cumulative top happens to be zero
        /// (slot 0), silently dropping the fragment on every later page.
        /// </summary>
        private FragmentRegion PageRegionOf(bool isFixed, Slot slot)
        {
            if (isFixed)
            {
                // A fixed box's containing block is the page box itself, margins included (CSS2.1 §10.1) -
                // so its membership test has to span the FULL physical sheet, not just this slot's content
                // band. Using container.MarginTop as the top bound (as an earlier version of this method
                // did) silently excluded any fixed content actually positioned inside the page's margins -
                // exactly where a realistic page-corner badge or watermark legitimately sits - discarding
                // the fragment for it entirely (not merely clipping it at paint time - see
                // FragmentPainter.Paint's own remarks on the parallel paint-clip half of this same defect).
                // Derived from this slot's own geometry (not a document-global constant), so a mixed
                // page-size document's differently-sized pages each get their own correct bounds too.
                var ppp = (container.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
                var sheetHeight = slot.Geometry.SheetHeightPt * ppp;

                return new FragmentRegion(0, sheetHeight, null, null);
            }

            // Left/Right null: the page grid's fragmentainers differ in the block axis only, so the inline
            // axis is not a membership question there (§2 shares one inline size across a box's fragments).
            return new FragmentRegion(slot.BandTop, slot.BandTop + slot.Geometry.BandHeight, null, null);
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
        /// <param name="rect">the box's own rectangle, in document space</param>
        /// <param name="box">the box the rectangle belongs to</param>
        /// <param name="region">the fragmentainer area to cut against</param>
        /// <param name="shift">
        /// how far lower the box draws here (<see cref="RecordFragmentDisplacement"/>). The cut is a
        /// membership question, so it is asked of where the rectangle <b>lands</b> — displaced before the
        /// band is applied and un-displaced after, so the caller's <see cref="Localize"/> against the
        /// already-displaced origin still lands in the right place.
        /// </param>
        private RRect BandCut(RRect rect, CssBox box, FragmentRegion region, double shift)
        {
            var landed = Displaced(rect, shift);

            var cut = container.HasCloneDecorations
                ? region.BlockCut(landed, DomUtils.ClonedBlockStart(box.ParentBox, stopAt: null), DomUtils.ClonedBlockEnd(box.ParentBox))
                : region.BlockCut(landed, 0, 0);

            return Displaced(cut, -shift);
        }

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
            double originY,
            double shift)
        {
            var slices = new Dictionary<CssLineBox, SliceGeometry>(rectangles.Count);

            RRect FragmentRectOf(RRect rect) => Localize(BandCut(rect, box, region, shift), originY);

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

            var rtl = ordered[0].Key.OwnerBox.Direction.Value == DirectionMode.Rtl;

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
        /// this fragment actually holds. The page grid needs the same content-derived extent for one further
        /// case despite normally not needing it at all: an item-content commit pass pins a flex/grid item's
        /// own content-box size once, on its first commit, and never revisits it on a later, resumed one - so
        /// a box whose content genuinely outgrows that pinned size has later fragments whose declared bounds
        /// do not reach the band their content landed in, which is exactly the shape
        /// <see cref="Draft.BoundsEndAtItsContent"/>'s page-grid arm exists for (issue
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/569">#569</see>).
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

            // A stated fragment's extent is what was stated: the box's own bounds describe the
            // fragmentainer that placed it, which is a different one.
            var bounds = draft.ShellRect ?? BoundsOf(draft.Box, draft.Snapshot);

            // The box's own live bounds (the ShellRect-absent branch just above) were read fresh here,
            // bypassing whatever BuildDraft already shifted its stored Lines/Words by for the same
            // reason - re-apply the same per-slot fixed-position delta so a box with no per-line
            // rectangles (UsesOwnBounds) agrees with one that has them.
            if (draft.ShellRect is null && (draft.FixedOffsetX != 0 || draft.FixedOffsetY != 0))
            {
                bounds = new RRect(
                    bounds.X + draft.FixedOffsetX, bounds.Y + draft.FixedOffsetY, bounds.Width, bounds.Height);
            }

            // A percentage width/height on a fixed box resolves against THIS slot's own page area (see
            // ComputeFixedSizeOverride) - applied only here, at the box's own outer extent, never to its
            // content: the box's lines/words were laid out once and are not re-flowed to the new size (see
            // Draft.FixedSizeDeltaWidth's own remarks). Same ShellRect-absent guard as the offset above -
            // a stated (sliced) fragment's declared bounds are not this box's own live extent to begin with.
            if (draft.ShellRect is null && (draft.FixedSizeDeltaWidth != 0 || draft.FixedSizeDeltaHeight != 0))
            {
                bounds = new RRect(
                    bounds.X, bounds.Y,
                    Math.Max(0, bounds.Width + draft.FixedSizeDeltaWidth),
                    Math.Max(0, bounds.Height + draft.FixedSizeDeltaHeight));
            }

            // An ordinary in-flow box's own outer frame catching up to content that already re-wraps per
            // fragment (issue #876) - same ShellRect-absent guard as above, and mutually exclusive with
            // FixedSizeDeltaWidth (ComputeInlineExtentDelta explicitly excludes out-of-flow boxes, which
            // includes every fixed one). Left edge (bounds.X) is unaffected - only the box's own
            // content-right edge moves per page, never its content-left one.
            if (draft.ShellRect is null && draft.InlineExtentDeltaWidth != 0)
            {
                bounds = new RRect(bounds.X, bounds.Y, Math.Max(0, bounds.Width + draft.InlineExtentDeltaWidth), bounds.Height);
            }

            // A nonzero FixedSizeDeltaHeight means this box's height came from an explicit per-page
            // percentage override (ComputeFixedSizeOverride never touches an auto height), not from its
            // content - so BoundsEndAtItsContent's usual "grow to whatever the content actually reached"
            // extension must not run here: the box's own content was laid out once, globally, and is not
            // re-flowed to the resized frame (Draft.FixedSizeDeltaWidth's own remarks), so content that
            // overflows a page whose measure shrank the box must overflow visibly rather than silently
            // re-growing the very frame this layer exists to resize.
            if (draft.BoundsEndAtItsContent && draft.FixedSizeDeltaHeight == 0)
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

                if (draft.Region.Contains(Displaced(bounds, draft.Shift)))
                    union = Localize(bounds, draft.OriginY);
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

        /// <summary>
        /// How far <paramref name="box"/>'s (a <c>position: fixed</c> box) own <c>left</c>/<c>top</c>
        /// would need to move on <paramref name="slot"/>'s own page, relative to the single value
        /// <c>CssBox.CommitBlockChildOffset</c> already resolved once, globally, against the document's
        /// base page area. Re-runs the exact same <c>CssBox.ResolveOffsetOrZero</c> calls against this
        /// slot's own resolved sheet size (<see cref="PageBandGeometry.SheetWidthPt"/>/
        /// <see cref="PageBandGeometry.SheetHeightPt"/>, minus this slot's own margins - the same
        /// content-area basis <c>CommitBlockChildOffset</c> uses, reconstructed per slot instead of read
        /// once from the document-global <c>HtmlContainerInt.PageSize</c>), so an absolute-length offset
        /// (whose resolution doesn't depend on the basis at all) always yields a zero delta - the
        /// existing per-page paint-time margin translate already positions those correctly, and this is
        /// purely the correction a percentage offset needs on top of it. Zero whenever the document has
        /// no <c>@page size</c> overrides at all (<see cref="PageGeometryTable.HasSizeOverrides"/>), so
        /// this never even runs for the overwhelming majority of documents.
        /// </summary>
        private (double Dx, double Dy) ComputeFixedPageOffset(CssBox box, Slot slot)
        {
            if (!container.PageGeometry.HasSizeOverrides) return (0, 0);

            var ppp = (container.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var geom = slot.Geometry;
            var basisWidthPx = (geom.SheetWidthPt - geom.MarginLeftPt - geom.MarginRightPt) * ppp;
            var basisHeightPx = (geom.SheetHeightPt - geom.MarginTopPt - geom.MarginBottomPt) * ppp;

            var left = box.ActualMarginLeft + CssBox.ResolveOffsetOrZero(box.Left, basisWidthPx, box);
            var top = box.ActualMarginTop + CssBox.ResolveOffsetOrZero(box.Top, basisHeightPx, box);

            return (left - box.Location.X, top - box.Location.Y);
        }

        /// <summary>
        /// How much wider/taller <paramref name="box"/>'s (a <c>position: fixed</c> box) own outer extent
        /// would need to be on <paramref name="slot"/>'s own page, relative to the single value
        /// <c>CssLayoutEngine.GetBoxWidth</c>/<c>GetBoxHeight</c> already resolved once, globally, against
        /// the document's base page area (<see cref="CssBox.ActualWidth"/>/<see cref="CssBox.ActualHeight"/>).
        /// Only a percentage <c>width</c>/<c>height</c> depends on that basis at all - an absolute length,
        /// or <c>auto</c>, always yields a zero delta for that dimension, since the box's own content isn't
        /// re-measured per slot (see <see cref="Draft.FixedSizeDeltaWidth"/>'s own remarks on why: doing so
        /// would require re-flowing the box's content, which css-position-3 explicitly does not require of
        /// a fixed box). Zero whenever the document has no <c>@page size</c> overrides at all
        /// (<see cref="PageGeometryTable.HasSizeOverrides"/>), so this never even runs for the overwhelming
        /// majority of documents.
        /// </summary>
        private (double DeltaWidth, double DeltaHeight) ComputeFixedSizeOverride(CssBox box, Slot slot)
        {
            if (!container.PageGeometry.HasSizeOverrides) return (0, 0);

            // Mirrors CssLayoutEngine.GetBoxWidth's own percentage-width gate exactly (a fixed box always
            // computes to display:block per CSS2.1 §9.7, so GetBoxWidth's inline-with-no-words exclusion
            // never applies here) - not narrowed to values textually ending in '%', so a calc() expression
            // with a percentage leaf (e.g. `calc(50% + 10pt)`) is recomputed too. An absolute length
            // naturally yields a zero delta on its own, since ParseLength doesn't depend on the basis for
            // one - no separate "is this a percentage" branch is needed to get that for free.
            var widthIsDefinite = box.Width != Keywords.Auto && !string.IsNullOrEmpty(box.Width);
            var heightIsDefinite = box.Height != Keywords.Auto && !string.IsNullOrEmpty(box.Height)
                && CssValueParser.IsValidLength(box.Height);
            if (!widthIsDefinite && !heightIsDefinite) return (0, 0);

            var ppp = (container.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var geom = slot.Geometry;
            var basisWidthPx = (geom.SheetWidthPt - geom.MarginLeftPt - geom.MarginRightPt) * ppp;
            var basisHeightPx = (geom.SheetHeightPt - geom.MarginTopPt - geom.MarginBottomPt) * ppp;

            var deltaWidth = 0.0;
            var deltaHeight = 0.0;

            if (widthIsDefinite)
            {
                var width = CssValueParser.ParseLength(box.Width, basisWidthPx, box) + box.ActualBoxSizeIncludedWidth;

                // Same min/max-width clamps GetBoxWidth itself applies, against the same basis
                // (box.ContainingBlock.Size.Width - the DOM containing block, not the fixed page area;
                // min/max-width isn't given the fixed-page special case width/height themselves get) -
                // reproduced here rather than shared, since GetBoxWidth computes the box's one global width
                // inline rather than through a reusable helper.
                if (CssValueParser.IsValidLength(box.MaxWidth))
                {
                    var maxW = CssValueParser.ParseLength(box.MaxWidth, box.ContainingBlock.Size.Width, box);
                    width = Math.Min(width, maxW);
                }

                if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
                {
                    var minW = CssValueParser.ParseLength(box.MinWidth, box.ContainingBlock.Size.Width, box);
                    width = Math.Max(width, minW);
                }

                deltaWidth = width - box.ActualWidth;
            }

            if (heightIsDefinite)
            {
                var height = CssValueParser.ParseLength(box.Height, basisHeightPx, box) + box.ActualBoxSizeIncludedHeight;

                // Same min/max-height clamps GetBoxHeight/ApplyHeight apply, against the same basis
                // (box.ContainingBlock.Size.Height, mirroring the width side above).
                if (CssValueParser.IsValidLength(box.MaxHeight))
                {
                    var maxH = CssValueParser.ParseLength(box.MaxHeight, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
                    height = Math.Min(height, maxH);
                }

                if (CssValueParser.IsValidLength(box.MinHeight))
                {
                    var minH = CssValueParser.ParseLength(box.MinHeight, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
                    height = Math.Max(height, minH);
                }

                deltaHeight = height - box.ActualHeight;
            }

            return (deltaWidth, deltaHeight);
        }

        /// <summary>
        /// How much wider <paramref name="box"/>'s own outer frame would need to be on <paramref name="slot"/>'s
        /// own page than the single value <c>CssLayoutEngine.GetBoxWidth</c> already resolved once,
        /// globally, against the document's base content-right edge (issue #876). Zero for any box
        /// <c>GetBoxWidth</c>'s own auto-width, containing-block-relative branch doesn't apply to in the
        /// first place: an explicit-length or percentage <c>width</c> (already page-independent), an
        /// out-of-flow box (a float/absolutely-positioned box is sized against its own placement, and a
        /// fixed box already has its own separate mechanism, <see cref="ComputeFixedSizeOverride"/>), a
        /// box with its own words (a non-replaced inline box measures from its content, not its
        /// containing block), a table/table-cell/flex/grid box (its own width comes from
        /// <c>CssLayoutEngineTable</c>/<c>CssLayoutEngineFlex</c>/<c>CssLayoutEngineGrid</c> instead of
        /// <c>GetBoxWidth</c>'s auto-width branch at all - see <c>CssBox.ResolveOwnInlineSize</c>'s own
        /// exact display-type exclusion, mirrored here; this box's OWN display, not merely whether its
        /// containing block is a main column, since a table/flex/grid box sitting directly under
        /// <c>&lt;body&gt;</c> otherwise satisfies every other guard below), or a box whose containing
        /// block isn't an unconstrained main column at all (<c>CssLayoutEngine.IsUnconstrainedMainColumn</c>
        /// - the exact same gate <c>CssLayoutEngine.ContentRightOf</c>/<c>LineContentRightOf</c> already
        /// use to decide whether THIS box's own text re-wraps per page, so its frame and its
        /// already-reflowing content agree on eligibility). Reproduces <c>GetBoxWidth</c>'s own auto-width
        /// formula and min/max-width clamps against THIS slot's own content-right edge
        /// (<see cref="HtmlContainerInt.PageContentRightOf"/>) rather than the document-wide one -
        /// reproduced here rather than shared, for the same reason <see cref="ComputeFixedSizeOverride"/>'s
        /// own remarks give (GetBoxWidth computes its result inline rather than through a reusable helper).
        /// </summary>
        private double ComputeInlineExtentDelta(CssBox box, Slot slot)
        {
            // Cheapest possible check first: UseVariableInlineMeasure is a precomputed bool, so this
            // avoids ever walking the containing-block chain (IsUnconstrainedMainColumn, below) for the
            // overwhelming majority of documents, which don't mix page sizes/margins at all - this runs
            // once per box per slot, so a per-call ancestor walk needs to stay off the common path.
            if (!container.UseVariableInlineMeasure) return 0;
            if (box.Width != Keywords.Auto && !string.IsNullOrEmpty(box.Width)) return 0;
            if (box.IsOutOfFlow) return 0;
            if (box.Words.Count > 0) return 0;

            if (box.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.TableCell
                or Keywords.Flex or Keywords.InlineFlex or Keywords.Grid or Keywords.InlineGrid)
            {
                return 0;
            }

            var containingBlock = box.ContainingBlock;
            if (!CssLayoutEngine.IsUnconstrainedMainColumn(containingBlock)) return 0;

            var availableRight = container.PageContentRightOf(slot.BandTop)
                - CssLayoutEngine.MainColumnRightInset(containingBlock);
            var width = availableRight - containingBlock.ClientLeft - box.ActualMarginLeft - box.ActualMarginRight
                - box.ActualBoxSizeIncludedWidth;

            if (CssValueParser.IsValidLength(box.MaxWidth))
            {
                var maxW = CssValueParser.ParseLength(box.MaxWidth, containingBlock.Size.Width, box);
                width = Math.Min(width, maxW);
            }

            if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
            {
                var minW = CssValueParser.ParseLength(box.MinWidth, containingBlock.Size.Width, box);
                width = Math.Max(width, minW);
            }

            return width - box.Size.Width;
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
