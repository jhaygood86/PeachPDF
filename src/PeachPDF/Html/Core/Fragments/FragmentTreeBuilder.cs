using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// Builds the immutable <see cref="FragmentTree"/> that is layout's output, by slicing the
    /// laid-out <see cref="CssBox"/> tree into per-fragmentainer <see cref="BoxFragment"/>s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fragmentation happens after layout, not during it.</b> PeachPDF lays a document out as one
    /// continuous flow (relocating whole boxes and words that straddle a page as it goes) and this
    /// builder then slices that flow into fragmentainers. A fully incremental engine would instead
    /// stop at each break, emit the fragments produced so far, and resume layout in the next
    /// fragmentainer from a recorded resumption point. The resulting <i>tree</i> is the same shape —
    /// which is what everything downstream needs — but the production model is not, and converting
    /// layout itself is tracked separately.
    /// </para>
    /// <para>
    /// The build walks the box tree twice: <see cref="CollectSpans"/> records, for every box, the
    /// first and last fragmentainer it will appear in (needed for
    /// <see cref="BoxFragment.IsFirstFragment"/>/<see cref="BoxFragment.IsLastFragment"/>, which
    /// cannot be known until the whole document has been walked), then <see cref="BuildFragment"/>
    /// allocates the records. Both walks apply the same emission rule — a box emits a fragment where
    /// its own geometry lands, or where any descendant emits — so they cannot disagree.
    /// </para>
    /// </remarks>
    internal static class FragmentTreeBuilder
    {
        /// <summary>
        /// Defensive backstop on the candidate-slot walk, behind <see cref="PageGeometryTable"/>'s own
        /// minimum-band clamp. Mirrors the cap the pre-fragment-tree slot walk used.
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
        /// Identifies a box within the walk. A repeating table header's source subtree is reached
        /// through a <see cref="CssProxyBox"/> and appears once per page at a different position each
        /// time, so the same <see cref="CssBox"/> can carry several unrelated spans — the owning proxy
        /// disambiguates them.
        /// </summary>
        private readonly record struct BoxKey(CssBox Box, CssProxyBox? Owner);

        /// <summary>
        /// The inclusive range of fragmentainer indices a box produces fragments in.
        /// <see cref="Last"/> below <see cref="First"/> means the box produces none.
        /// </summary>
        private readonly record struct BandSpan(int First, int Last)
        {
            internal static BandSpan None => new(int.MaxValue, int.MinValue);

            internal bool IsEmpty => Last < First;

            internal BandSpan Including(int band) => new(Math.Min(First, band), Math.Max(Last, band));

            internal BandSpan Union(BandSpan other) =>
                other.IsEmpty ? this : new(Math.Min(First, other.First), Math.Max(Last, other.Last));

            internal bool Contains(int band) => band >= First && band <= Last;
        }

        /// <summary>
        /// One candidate pagination slot: its index and the document-space band layout paginated
        /// against. Slots are walked by grid index so a per-page <c>@page</c> margin override can give
        /// each its own band top and height.
        /// </summary>
        private readonly record struct CandidateSlot(int Index, double BandTop, double BandBottom, PageBandGeometry Geometry);

        /// <summary>
        /// Builds the fragment tree for a fully laid-out document. Returns an empty tree when there is
        /// nothing to paginate.
        /// </summary>
        internal static FragmentTree Build(HtmlContainerInt container)
        {
            ArgumentNullException.ThrowIfNull(container);

            var root = container.Root;

            if (root is null || container.ActualSize.Height <= 0)
                return new FragmentTree([]);

            var candidates = CollectCandidateSlots(container);

            if (candidates.Count == 0)
                return new FragmentTree([]);

            var spans = new Dictionary<BoxKey, BandSpan>();
            CollectSpans(root, owner: null, snapshot: null, container, candidates, spans);

            var fragmentainers = new List<FragmentainerFragment>(candidates.Count);
            FragmentainerFragment? firstCandidate = null;

            for (var i = 0; i < candidates.Count; i++)
            {
                var slot = candidates[i];
                var localOriginY = slot.BandTop - container.MarginTop;

                var hasPrintableContent = false;
                var rootFragment = BuildFragment(root, owner: null, snapshot: null, container, slot, i, localOriginY, spans, ref hasPrintableContent)
                                   ?? EmptyRootFragment(root, i, localOriginY);

                var fragmentainer = new FragmentainerFragment(
                    new RRect(
                        container.MarginLeft,
                        container.MarginTop,
                        container.PageSize.Width + container.MarginRight,
                        slot.Geometry.BandHeight),
                    slot.Index,
                    slot.Geometry,
                    localOriginY,
                    rootFragment);

                firstCandidate ??= fragmentainer;

                if (hasPrintableContent)
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

        private static List<CandidateSlot> CollectCandidateSlots(HtmlContainerInt container)
        {
            var slots = new List<CandidateSlot>();

            for (var k = 0; container.PageTopOf(k) - container.MarginTop < container.ActualSize.Height && k < MaxSlots; k++)
            {
                slots.Add(new CandidateSlot(k, container.PageTopOf(k), container.PageBottomOf(k), container.PageGeometry.GetPage(k)));
            }

            return slots;
        }

        /// <summary>
        /// The root fragment of a fragmentainer whose content produced nothing at all — the structural
        /// counterpart of the never-emit-a-0-page-document fallback, so every materialized
        /// fragmentainer still has a root to paint into.
        /// </summary>
        private static BoxFragment EmptyRootFragment(CssBox root, int fragmentainerIndex, double localOriginY) =>
            new(RRect.Empty, root, fragmentainerIndex, localOriginY, Localize(root.Bounds, localOriginY),
                IsFixed: false, IsFirstFragment: true, IsLastFragment: true, [], [], []);

        /// <summary>
        /// Records, for every box, the exact inclusive range of fragmentainers it emits a fragment in.
        /// </summary>
        private static BandSpan CollectSpans(
            CssBox box,
            CssProxyBox? owner,
            BoxGeometrySnapshot? snapshot,
            HtmlContainerInt container,
            List<CandidateSlot> candidates,
            Dictionary<BoxKey, BandSpan> spans)
        {
            // A display:none subtree paints nothing at all (CssBox.Paint and PaintImpCore both return
            // immediately), so it produces no fragments either.
            if (box.Display == CssConstants.None)
                return BandSpan.None;

            var span = OwnSpan(box, snapshot, box.IsFixed, container, candidates);

            foreach (var (childBox, childOwner, childSnapshot) in ChildrenOf(box, owner, snapshot))
            {
                span = span.Union(CollectSpans(childBox, childOwner, childSnapshot, container, candidates, spans));
            }

            spans[new BoxKey(box, owner)] = span;
            return span;
        }

        private static BoxFragment? BuildFragment(
            CssBox box,
            CssProxyBox? owner,
            BoxGeometrySnapshot? snapshot,
            HtmlContainerInt container,
            CandidateSlot slot,
            int fragmentainerIndex,
            double localOriginY,
            Dictionary<BoxKey, BandSpan> spans,
            ref bool hasPrintableContent)
        {
            if (!spans.TryGetValue(new BoxKey(box, owner), out var span) || !span.Contains(fragmentainerIndex))
                return null;

            // Fixed-position content ignores the page origin and repeats identically on every page, so
            // its fragments carry raw document coordinates (CSS Position 3: a fixed box's containing
            // block is the page box itself).
            var isFixed = box.IsFixed;
            var originY = isFixed ? 0 : localOriginY;

            List<LineFragment> lines = [];
            List<TextFragment> words = [];

            // A proxy carries no content of its own - it stands in for its source subtree, whose
            // styles it copied wholesale, so painting its own decoration would draw a repeated
            // header's background twice.
            if (box is not CssProxyBox && HasOwnGeometryIn(box, snapshot, isFixed, container, slot))
            {
                var rectangles = RectanglesOf(box, snapshot);

                if (rectangles.Count > 0)
                {
                    foreach (var (line, rect) in rectangles)
                    {
                        if (IntersectsBand(rect, isFixed, container, slot))
                            lines.Add(new LineFragment(Localize(rect, originY), line));
                    }
                }
                else
                {
                    var bounds = BoundsOf(box, snapshot);

                    if (IntersectsBand(bounds, isFixed, container, slot))
                        lines.Add(new LineFragment(Localize(bounds, originY), null));
                }

                for (var i = 0; i < box.Words.Count; i++)
                {
                    var rect = WordRectOf(box, i, snapshot);

                    if (IntersectsBand(rect, isFixed, container, slot))
                        words.Add(new TextFragment(Localize(rect, originY), box.Words[i]));
                }
            }

            List<BoxFragment> children = [];

            foreach (var (childBox, childOwner, childSnapshot) in ChildrenOf(box, owner, snapshot))
            {
                var childFragment = BuildFragment(
                    childBox, childOwner, childSnapshot, container, slot, fragmentainerIndex, localOriginY, spans, ref hasPrintableContent);

                if (childFragment is not null)
                    children.Add(childFragment);
            }

            if (lines.Count == 0 && words.Count == 0 && children.Count == 0)
                return null;

            if (!hasPrintableContent && IsPrintableContentIn(box, snapshot, isFixed, slot))
                hasPrintableContent = true;

            return new BoxFragment(
                UnionRects(lines, children),
                box,
                fragmentainerIndex,
                originY,
                Localize(BoundsOf(box, snapshot), originY),
                isFixed,
                fragmentainerIndex == span.First,
                fragmentainerIndex == span.Last,
                lines,
                words,
                children);
        }

        /// <summary>
        /// A box's children for fragment-building purposes. This is <see cref="CssBox.Boxes"/> for
        /// every box except a <see cref="CssProxyBox"/>, whose real content is its
        /// <see cref="CssProxyBox.SourceBox"/> — deliberately kept out of the live tree so one source
        /// subtree can be repeated on many pages. Descending into it through the proxy's own captured
        /// geometry is what puts a repeating table header into the fragment tree.
        /// </summary>
        private static IEnumerable<(CssBox Box, CssProxyBox? Owner, BoxGeometrySnapshot? Snapshot)> ChildrenOf(
            CssBox box, CssProxyBox? owner, BoxGeometrySnapshot? snapshot)
        {
            if (box is CssProxyBox proxy)
            {
                if (proxy.SourceGeometry is { } proxyGeometry)
                    yield return (proxy.SourceBox, proxy, proxyGeometry);

                yield break;
            }

            // A rowspan placeholder shows the cell that spans into it, which lives in an earlier row.
            // That cell therefore appears in the tree once per row it spans - the fragments are
            // distinct objects, so each is painted in its own place.
            if (box is CssSpacingBox spacing)
                yield return (spacing.ExtendedBox, owner, snapshot);

            foreach (var childBox in box.Boxes)
            {
                yield return (childBox, owner, snapshot);
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
        private static bool IsPrintableContentIn(CssBox box, BoxGeometrySnapshot? snapshot, bool isFixed, CandidateSlot slot)
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
        /// The fragmentainers <paramref name="box"/>'s own geometry lands in — ignoring descendants,
        /// which <see cref="CollectSpans"/> unions in separately.
        /// </summary>
        private static BandSpan OwnSpan(
            CssBox box, BoxGeometrySnapshot? snapshot, bool isFixed, HtmlContainerInt container, List<CandidateSlot> candidates)
        {
            var span = BandSpan.None;

            // A fixed rectangle does not move with the page, so which bands it lands in has nothing to
            // do with where it sits in the document - every band has to be asked. There are few such
            // boxes, and the answer is usually "all of them".
            if (isFixed)
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    if (HasOwnGeometryIn(box, snapshot, isFixed: true, container, candidates[i]))
                        span = span.Including(i);
                }

                return span;
            }

            var rectangles = RectanglesOf(box, snapshot);

            if (rectangles.Count > 0)
            {
                foreach (var rect in rectangles.Values)
                {
                    span = span.Union(BandsOf(rect, container, candidates));
                }
            }
            else
            {
                span = span.Union(BandsOf(BoundsOf(box, snapshot), container, candidates));
            }

            for (var i = 0; i < box.Words.Count; i++)
            {
                span = span.Union(BandsOf(WordRectOf(box, i, snapshot), container, candidates));
            }

            return span;
        }

        /// <summary>
        /// The fragmentainers one document-space rectangle lands in. Bands are contiguous, so the
        /// candidates worth testing are bounded by the bands containing the rectangle's own top and
        /// bottom — widened by one either way to absorb the boundary and epsilon cases, then confirmed
        /// with the same test <see cref="HasOwnGeometryIn"/> applies. Bounding this matters: a long
        /// document has thousands of bands, and every box would otherwise be asked about all of them.
        /// </summary>
        private static BandSpan BandsOf(RRect rect, HtmlContainerInt container, List<CandidateSlot> candidates)
        {
            var last = candidates.Count - 1;
            var from = Math.Clamp(container.PageIndexOf(rect.Top) - 1, 0, last);
            var to = Math.Clamp(container.PageIndexOf(rect.Bottom) + 1, 0, last);

            var span = BandSpan.None;

            for (var i = from; i <= to; i++)
            {
                if (IntersectsBand(rect, isFixed: false, container, candidates[i]))
                    span = span.Including(i);
            }

            return span;
        }

        private static bool HasOwnGeometryIn(
            CssBox box, BoxGeometrySnapshot? snapshot, bool isFixed, HtmlContainerInt container, CandidateSlot slot)
        {
            var rectangles = RectanglesOf(box, snapshot);

            if (rectangles.Count > 0)
            {
                foreach (var rect in rectangles.Values)
                {
                    if (IntersectsBand(rect, isFixed, container, slot)) return true;
                }
            }
            else if (IntersectsBand(BoundsOf(box, snapshot), isFixed, container, slot))
            {
                return true;
            }

            for (var i = 0; i < box.Words.Count; i++)
            {
                if (IntersectsBand(WordRectOf(box, i, snapshot), isFixed, container, slot)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a document-space rectangle lands in <paramref name="slot"/>'s content band, using
        /// the same minimum-overlap rule the painter's own visibility test applies. A fixed rectangle
        /// is tested against the band at its own unshifted position, since it does not move with the
        /// page.
        /// </summary>
        private static bool IntersectsBand(RRect rect, bool isFixed, HtmlContainerInt container, CandidateSlot slot)
        {
            var bandTop = isFixed ? container.MarginTop : slot.BandTop;
            var bandBottom = bandTop + slot.Geometry.BandHeight;

            return Math.Min(rect.Bottom, bandBottom) - Math.Max(rect.Top, bandTop) > BandOverlapEpsilon;
        }

        private static RRect BoundsOf(CssBox box, BoxGeometrySnapshot? snapshot) =>
            snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) ? geometry.Bounds : box.Bounds;

        private static IReadOnlyDictionary<CssLineBox, RRect> RectanglesOf(CssBox box, BoxGeometrySnapshot? snapshot) =>
            snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) ? geometry.Rectangles : box.Rectangles;

        private static RRect WordRectOf(CssBox box, int index, BoxGeometrySnapshot? snapshot)
        {
            var word = box.Words[index];

            // A snapshot records only where a word sits, not how big it is - a repeated header's words
            // are the very same CssRect objects, measured once.
            if (snapshot is not null && snapshot.TryGetGeometry(box, out var geometry) && index < geometry.WordOrigins.Count)
            {
                var origin = geometry.WordOrigins[index];
                return new RRect(origin.X, origin.Y, word.Width, word.Height);
            }

            return word.Rectangle;
        }

        private static RRect Localize(RRect rect, double originY) =>
            new(rect.X, rect.Y - originY, rect.Width, rect.Height);

        private static RRect UnionRects(List<LineFragment> lines, List<BoxFragment> children)
        {
            RRect? union = null;

            foreach (var line in lines)
            {
                union = union is null ? line.Rect : RRect.Union(union.Value, line.Rect);
            }

            if (union is not null) return union.Value;

            foreach (var child in children)
            {
                union = union is null ? child.Rect : RRect.Union(union.Value, child.Rect);
            }

            return union ?? RRect.Empty;
        }
    }
}
