using System;
using System.Collections.Generic;
using System.Numerics;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Animation
{
    // Which promoted boxes can be drawn apart from the page (DocumentPaintLayer.Animated), so a frame that only moves or fades them repaints nothing of the
    // surface under them.
    //
    // The animated layer is composited over the content layer, so a box drawn there is only drawn the same if everything the page paints over it is drawn
    // there too, in the page's own order. The plan is therefore a set of *members*, each a participant of the root stacking context (Appendix E step 8): the
    // outermost stacking context that holds an animated box (so an ancestor's opacity, filter or transform composites it as it always did - the whole group is
    // painted by the animated layer), or the animated box itself. The set is closed under "paints later and overlaps": a positioned box painted over a member
    // that overlaps it joins the set as a companion, repainted by the animated layer in the page's order. What cannot be layered keeps the animated box in the
    // content layer: a member something fixed or sticky must sit under, in-flow content that paints over a box with a negative z-index, a subtree that blends or
    // reads its backdrop, a set that has grown past a cap. Ancestors that only clip are not part of the group: the animated layer re-applies their clips
    // around the member (PaintStackingParticipant), as the page's own paint order does for a hoisted box.
    //
    // A box that stops qualifying goes back to the content layer, and the frame repaints where it was; see AddDirtyRegions.
    internal sealed partial class AnimationClock
    {
        // The most boxes (members and companions) one plan holds, and the largest area (in points squared) a member is worth repainting each frame for unless it
        // is no larger than a few times the animated boxes in it: past these the content layer is cheaper than the animated layer.
        private const int MaxMembers = 12;
        private const double MaxMemberArea = 640000;
        private const double MaxAreaOverAnimated = 8;

        /// <summary>The boxes to draw apart from the page this frame: the set the content layer leaves out, and what the animated layer paints, in order.</summary>
        internal sealed class OverlayPlan
        {
            public static readonly OverlayPlan Empty = new([], [], []);

            public OverlayPlan(HashSet<CssBox> boxes, List<StackingOrder.StackingParticipant> roots, Dictionary<CssBox, Rect> staticRects)
            {
                Boxes = boxes;
                Roots = roots;
                StaticRects = staticRects;
            }

            /// <summary>Every box the content layer leaves out: the members and companions, and the animated boxes inside them.</summary>
            public HashSet<CssBox> Boxes { get; }

            /// <summary>The members to paint, in paint order, each with the ancestors whose overflow clip applies to it.</summary>
            public List<StackingOrder.StackingParticipant> Roots { get; }

            /// <summary>Where each member that is not itself an animated box covers on the page (in points, as the dirty regions are), for repainting it when it changes layers.</summary>
            public Dictionary<CssBox, Rect> StaticRects { get; }
        }

        // A participant of the root stacking context that is positioned or a stacking context itself: what the page paints in step 8, in order.
        private sealed class Part
        {
            public required CssBox Box { get; init; }
            public required StackingOrder.StackingParticipant Participant { get; init; }
            public required Rect Extent { get; init; }
            public required double OriginX { get; init; }
            public required double OriginY { get; init; }
            public required int Z { get; init; }
            public required int Order { get; init; }
            public required bool Attached { get; init; }
            public bool Eligible { get; set; }
            public List<Candidate> Animated { get; } = [];
        }

        private sealed class Candidate
        {
            public required BoxState State { get; init; }
            public required BoxFragment Fragment { get; init; }
            public required Rect Extent { get; init; }
        }

        private readonly record struct Ink(Rect Rect);

        private IReadOnlyList<FragmentainerFragment>? _structureFor;
        private int _structureRevision = -1;
        private int _promotionRevision;
        private List<Part> _parts = [];
        private List<Part> _roots = [];
        private List<Ink> _ink = [];

        private OverlayPlan? _plan;
        private IReadOnlyList<FragmentainerFragment>? _planFor;
        private int _planFrame = -1;
        private int _frame;

        // Where each static member was last known to cover, kept across plans: a member that leaves the plan is repainted where the last plan that held it put it.
        private readonly Dictionary<CssBox, Rect> _knownStatic = [];

        /// <summary>The plan the host last painted with, or null when none was made.</summary>
        internal OverlayPlan? CurrentOverlay => _plan;

        /// <summary>
        /// What the animated layer draws for the pages as they are laid out now, or null when nothing is. Made again for each step of the clock and each layout, and
        /// otherwise kept, so painting the content and the animated layers in turn sees the same plan.
        /// </summary>
        internal OverlayPlan? OverlayFor(IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            if (!_container.Continuous || fragmentainers.Count != 1) return null;
            if (_plan is null || !ReferenceEquals(_planFor, fragmentainers) || _planFrame != _frame) BuildPlan(fragmentainers);
            return _plan!.Boxes.Count == 0 ? null : _plan;
        }

        private void BuildPlan(IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            EnsureStructure(fragmentainers);

            _planFor = fragmentainers;
            _planFrame = _frame;

            // The roots to try: those holding a promoted animated box. One that cannot be layered is dropped and the set worked out again, since the companions
            // another root pulled in may have been pulled in for it alone.
            var active = new List<Part>();
            foreach (var root in _roots)
            {
                if (!root.Eligible) continue;
                foreach (var candidate in root.Animated)
                {
                    if (candidate.State.Promoted)
                    {
                        active.Add(root);
                        break;
                    }
                }
            }

            HashSet<Part>? members = null;
            while (active.Count > 0)
            {
                members = [];
                Part? failed = null;
                foreach (var root in active)
                {
                    if (!Close(root, members))
                    {
                        failed = root;
                        break;
                    }
                }

                if (failed is null) break;
                active.Remove(failed);
                members = null;
            }

            if (members is null || members.Count == 0)
            {
                _plan = OverlayPlan.Empty;
                return;
            }

            // Paint order is the page's: by z-index layer, in the order the page's own paint finds them within one.
            var ordered = new List<Part>(members);
            ordered.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Order.CompareTo(b.Order));

            var boxes = new HashSet<CssBox>();
            var roots = new List<StackingOrder.StackingParticipant>(ordered.Count);
            var statics = new Dictionary<CssBox, Rect>();
            var frame = FrameOf(fragmentainers[0]);
            foreach (var part in ordered)
            {
                boxes.Add(part.Box);
                roots.Add(part.Participant);
                var animatedSelf = false;
                foreach (var candidate in part.Animated)
                {
                    boxes.Add(candidate.Fragment.Box);
                    animatedSelf |= ReferenceEquals(candidate.Fragment.Box, part.Box);
                }

                if (animatedSelf) continue;

                var layout = BoundsOf(part);
                var rect = new Rect(frame.X(layout.X) - Inflate, frame.Y(layout.Y) - Inflate, (layout.Width / frame.Ppp) + (Inflate * 2), (layout.Height / frame.Ppp) + (Inflate * 2));
                statics[part.Box] = rect;
                _knownStatic[part.Box] = rect;
            }

            _plan = new OverlayPlan(boxes, roots, statics);
        }

        private TextRunCollector.Frame FrameOf(FragmentainerFragment fragmentainer)
        {
            var geometry = fragmentainer.Geometry;
            return new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, _container.MarginLeft, _container.MarginTop, _container.RenderContext.LayoutUnitsPerPoint);
        }

        // Adds the member and everything that has to be drawn with it; false when the set cannot be layered.
        private bool Close(Part start, HashSet<Part> members)
        {
            if (!members.Add(start)) return true;

            var queue = new Queue<Part>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var member = queue.Dequeue();
                if (members.Count > MaxMembers) return false;

                var mine = BoundsOf(member);
                foreach (var other in _parts)
                {
                    if (ReferenceEquals(other, member)) continue;
                    if (IsAncestorBox(other.Box, member.Box) || IsAncestorBox(member.Box, other.Box)) continue;

                    var theirs = BoundsOf(other);
                    if (!Overlaps(mine, theirs)) continue;

                    var before = other.Z != member.Z ? other.Z < member.Z : other.Order < member.Order;

                    // Drawn over the animated layer whatever its z-index: right when it is meant to be over the member, wrong when it is meant to be under it.
                    if (_container.View.IsAttached(other.Box))
                    {
                        if (before) return false;
                        continue;
                    }

                    // Painted before the member, so under it in either layer.
                    if (before) continue;

                    // Painted after the member and overlapping it: it has to be drawn over it, so it is drawn by the animated layer too.
                    if (!other.Eligible) return false;
                    if (members.Add(other)) queue.Enqueue(other);
                }

                // Under the root's own content, a negative z-index sits below the text and boxes that are not positioned, which the animated layer would put it over.
                if (member.Z < 0)
                {
                    foreach (var ink in _ink)
                    {
                        if (Overlaps(mine, ink.Rect)) return false;
                    }
                }
            }

            return true;
        }

        // What the member covers right now: the extent of its subtree under its own current matrix, and where each animated box inside it has moved to.
        private Rect BoundsOf(Part part)
        {
            var bounds = CurrentBounds(part.Box, part.Extent, part.OriginX, part.OriginY);
            foreach (var candidate in part.Animated)
            {
                if (ReferenceEquals(candidate.Fragment.Box, part.Box)) continue;
                bounds = Union(bounds, CurrentBounds(candidate.Fragment.Box, candidate.Extent, candidate.Fragment.WholeBoxRect.X, candidate.Fragment.WholeBoxRect.Y));
            }

            if (part.Attached && _container.View.DisplacementOf(part.Box) is var (dx, dy) && (dx != 0 || dy != 0))
                bounds = new Rect(bounds.X + dx, bounds.Y + dy, bounds.Width, bounds.Height);

            return bounds;
        }

        private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

        private static bool IsAncestorBox(CssBox ancestor, CssBox box)
        {
            for (var current = box.ParentBox; current is not null; current = current.ParentBox)
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }

            return false;
        }

        // What does not change from step to step: what is positioned or composites, the animated boxes in it, and the content that paints under it.
        private void EnsureStructure(IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            // Whether an animated box is a stacking context follows its opacity and transform, which change from step to step: it moves in and out of step 8.
            var signature = StackingSignature();
            if (ReferenceEquals(_structureFor, fragmentainers) && _structureRevision == _promotionRevision && signature == _stackingSignature) return;

            _stackingSignature = signature;
            _structureFor = fragmentainers;
            _structureRevision = _promotionRevision;
            _parts = [];
            _roots = [];
            _ink = [];

            var root = fragmentainers[0].Root;
            var byBox = new Dictionary<CssBox, Part>();
            var order = 0;
            foreach (var participant in StackingOrder.Flatten(root))
            {
                var box = participant.Box;
                if (!(box.IsPositioned || DomUtils.IsStackingContextBox(box))) continue;

                var extent = FragmentPainter.SubtreeExtent(participant.Fragment) ?? participant.Fragment.WholeBoxRect;
                var bleed = FragmentPainter.SubtreeBleed(participant.Fragment) + 2;
                var attached = box.Position.Value is PositionMode.Fixed or PositionMode.Sticky;
                var part = new Part
                {
                    Box = box,
                    Participant = participant,
                    Extent = Grow(extent, bleed),
                    OriginX = participant.Fragment.WholeBoxRect.X,
                    OriginY = participant.Fragment.WholeBoxRect.Y,
                    Z = StackingOrder.LayerOf(participant),
                    Order = order++,
                    Attached = attached,
                };

                part.Eligible = !attached && IsLayerable(participant.Fragment, isRoot: true) && AreaOf(part) <= MaxMemberArea;
                if (!byBox.TryAdd(box, part)) continue;
                _parts.Add(part);
            }

            FindCandidates(root, byBox);

            // A member as large as the page is worth repainting each frame only when what is animated in it is a good part of it.
            foreach (var part in _parts)
            {
                if (part.Animated.Count == 0) continue;
                _roots.Add(part);

                var animatedArea = 0.0;
                foreach (var candidate in part.Animated) animatedArea += candidate.Extent.Width * candidate.Extent.Height;
                if (AreaOf(part) > MaxMemberArea && AreaOf(part) > animatedArea * MaxAreaOverAnimated) part.Eligible = false;
            }

            CollectInk(root, byBox);
        }

        private int _stackingSignature;

        private int StackingSignature()
        {
            var hash = new HashCode();
            foreach (var state in _states)
            {
                if (state.Compositor) hash.Add(DomUtils.IsStackingContextBox(state.Box));
            }

            return hash.ToHashCode();
        }

        private static double AreaOf(Part part) => part.Extent.Width * part.Extent.Height;

        // Finds each animated box and what it is drawn with: the outermost stacking context it is in (or itself), which is a participant of the root.
        private void FindCandidates(BoxFragment fragment, Dictionary<CssBox, Part> byBox)
        {
            foreach (var child in fragment.Children)
            {
                var box = child.Box;
                if (box.IsMarkerPseudoElement) continue;

                if (_byBox.TryGetValue(box, out var state) && state.Compositor && box.Position.Value is not (PositionMode.Fixed or PositionMode.Sticky))
                {
                    // The root of the group it is drawn with: the outermost stacking context at or above it. A box that is not one now (an opacity that is 1, no
                    // transform) is painted with the page's block and inline content, not in step 8, and stays where it is.
                    Part? rootPart = null;
                    for (var current = box; current is not null && !current.IsRoot; current = current.ParentBox)
                    {
                        if (DomUtils.IsStackingContextBox(current) && byBox.TryGetValue(current, out var found)) rootPart = found;
                    }

                    if (rootPart is not null)
                    {
                        var extent = FragmentPainter.SubtreeExtent(child) ?? child.WholeBoxRect;
                        rootPart.Animated.Add(new Candidate { State = state, Fragment = child, Extent = Grow(extent, FragmentPainter.SubtreeBleed(child) + 2) });
                    }

                    continue;
                }

                FindCandidates(child, byBox);
            }
        }

        // Whether a subtree can be painted away from the page and composited over it: nothing in it blends with, or reads, what is behind the group, and
        // nothing in it is held by the viewport (the attached layer draws those, and the content layer would leave them out a second time).
        private static bool IsLayerable(BoxFragment fragment, bool isRoot)
        {
            var box = fragment.Box;
            if (box.ActualMixBlendMode != BlendMode.Normal) return false;
            if (box.ActualBackdropFilterFunctions.Count > 0) return false;
            if (!isRoot && box.Position.Value is PositionMode.Fixed or PositionMode.Sticky) return false;

            foreach (var child in fragment.Children)
            {
                if (!IsLayerable(child, isRoot: false)) return false;
            }

            return true;
        }

        // The content a negative z-index box would be under: what is not positioned, in the root's own context, that paints something.
        private void CollectInk(BoxFragment fragment, Dictionary<CssBox, Part> byBox)
        {
            foreach (var child in fragment.Children)
            {
                var box = child.Box;
                if (byBox.ContainsKey(box)) continue;

                if (HasInk(child))
                {
                    var extent = child.WholeBoxRect;
                    foreach (var line in child.Lines) extent = Union(extent, line.Rect);
                    foreach (var word in child.Words) extent = Union(extent, word.Rect);
                    _ink.Add(new Ink(Grow(extent, FragmentPainter.SubtreeBleed(child) + 2)));
                }

                CollectInk(child, byBox);
            }
        }

        private static bool HasInk(BoxFragment fragment)
        {
            var box = fragment.Box;
            return fragment.Words.Count > 0
                || box.HasOwnBackground
                || box.ActualBorderLeftWidth > 0 || box.ActualBorderTopWidth > 0 || box.ActualBorderRightWidth > 0 || box.ActualBorderBottomWidth > 0
                || box.ActualOutlineWidth > 0
                || FragmentPainter.ShadowBleed(box) > 0
                || MonolithicContent.IsReplaced(box);
        }

        // Where the box covers now: the extent of its subtree under its current matrix, which pivots at the box's own top-left.
        private static Rect CurrentBounds(CssBox box, Rect extent, double originX, double originY) =>
            box.HasTransform ? Bounds(box.ActualTransformMatrix.RebaseOrigin(originX, originY), extent) : extent;

        private static Rect Grow(Rect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + (by * 2), rect.Height + (by * 2));

        /// <summary>
        /// The regions of the pages to repaint after a layout for the boxes that changed layers: one that was in <paramref name="before"/> and is no longer drawn
        /// apart (so the content layer must now hold it) and one that is now drawn apart and was not (so the content layer must stop holding it).
        /// </summary>
        internal List<DirtyRegion> OverlayFlipRegions(IReadOnlySet<CssBox>? before, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var result = new List<DirtyRegion>();
            var after = OverlayFor(fragmentainers)?.Boxes;
            if ((before is null || before.Count == 0) && (after is null || after.Count == 0)) return result;

            EnsureExtents(fragmentainers, resetMatrices: false);
            var rects = new Dictionary<int, List<Rect>>();
            foreach (var state in _states)
            {
                var was = before?.Contains(state.Box) == true;
                var now = after?.Contains(state.Box) == true;
                if (was != now) AddStateRects(state, rects);
            }

            AddStaticFlipRects(before, after, rects);

            foreach (var (page, list) in rects)
            {
                foreach (var rect in Merge(list)) result.Add(new DirtyRegion(page, rect));
            }

            return result;
        }

        // A member that is not an animated box (an ancestor group, a companion) that changed layers: repainted where it was last known to be.
        private void AddStaticFlipRects(IReadOnlySet<CssBox>? before, IReadOnlySet<CssBox>? after, Dictionary<int, List<Rect>> perPage)
        {
            foreach (var (box, rect) in _knownStatic)
            {
                if (_byBox.ContainsKey(box)) continue;

                var was = before?.Contains(box) == true;
                var now = after?.Contains(box) == true;
                if (was == now) continue;

                if (!perPage.TryGetValue(0, out var list)) perPage[0] = list = [];
                list.Add(rect);
            }
        }

        // Where a box covers on each page it is on, as it is now.
        private void AddStateRects(BoxState state, Dictionary<int, List<Rect>> perPage)
        {
            var matrix = state.Box.HasTransform ? state.Box.ActualTransformMatrix : Matrix3x2.Identity;
            foreach (var part in state.Fragments)
            {
                var layout = Bounds(part.Rebased(matrix, state.Box.HasTransform), part.Extent);
                var shifted = _container.View.DisplacementOf(state.Box) is var (dx, dy) && (dx != 0 || dy != 0)
                    ? new Rect(layout.X + dx, layout.Y + dy, layout.Width, layout.Height)
                    : layout;

                var rect = new Rect(part.Frame.X(shifted.X) - Inflate, part.Frame.Y(shifted.Y) - Inflate,
                    (shifted.Width / part.Frame.Ppp) + (Inflate * 2), (shifted.Height / part.Frame.Ppp) + (Inflate * 2));

                if (!perPage.TryGetValue(part.Page, out var list)) perPage[part.Page] = list = [];
                list.Add(rect);
            }
        }
    }
}
