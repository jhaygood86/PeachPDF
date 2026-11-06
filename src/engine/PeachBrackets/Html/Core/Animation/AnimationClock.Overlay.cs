using System;
using System.Collections.Generic;
using System.Numerics;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Animation
{
    // Which promoted boxes can be drawn apart from the page (DocumentPaintLayer.Animated), so a frame that only moves or fades them repaints nothing of the
    // surface under them.
    //
    // A box drawn later than the rest of the page is only drawn the same if nothing that is painted after it overlaps it, and if nothing between it and the
    // page changes how it is drawn. So a box qualifies when (1) every box above it is plain - not a stacking context, not clipping its overflow - which is
    // what lets the box be painted from the page's coordinates with no ancestor state, and (2) no other positioned box that is drawn over it (or any box the
    // viewport holds in place) overlaps where the box is right now. Everything that is not positioned paints before a box that is a stacking context
    // (CSS 2.1 Appendix E steps 3-7 against step 8), so only the positioned ones need to be looked at. A box that stops qualifying (another box moved over
    // it) goes back to the content layer, and the frame repaints where it was; see AddDirtyRegions.
    internal sealed partial class AnimationClock
    {
        /// <summary>The boxes to draw apart from the page this frame: the set the content layer leaves out, and the fragments the animated layer paints, in order.</summary>
        internal sealed class OverlayPlan
        {
            public static readonly OverlayPlan Empty = new([], []);

            public OverlayPlan(HashSet<CssBox> boxes, List<BoxFragment> roots)
            {
                Boxes = boxes;
                Roots = roots;
            }

            public HashSet<CssBox> Boxes { get; }

            public List<BoxFragment> Roots { get; }
        }

        private readonly record struct OverlayCandidate(BoxState State, BoxFragment Fragment, Rect Extent);

        private readonly record struct OverlayOther(CssBox Box, Rect Extent, double OriginX, double OriginY, int Z, bool Attached);

        private IReadOnlyList<FragmentainerFragment>? _structureFor;
        private int _structureRevision = -1;
        private int _promotionRevision;
        private List<OverlayCandidate> _candidates = [];
        private List<OverlayOther> _others = [];

        private OverlayPlan? _plan;
        private IReadOnlyList<FragmentainerFragment>? _planFor;
        private int _planFrame = -1;
        private int _frame;

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

            HashSet<CssBox>? boxes = null;
            List<(int Z, BoxFragment Fragment)>? chosen = null;
            foreach (var candidate in _candidates)
            {
                if (!candidate.State.Promoted || CollidesWithOthers(candidate)) continue;

                (boxes ??= []).Add(candidate.Fragment.Box);
                (chosen ??= []).Add((LayerOf(candidate.Fragment), candidate.Fragment));
            }

            _planFor = fragmentainers;
            _planFrame = _frame;
            if (boxes is null || chosen is null)
            {
                _plan = OverlayPlan.Empty;
                return;
            }

            // Paint order among them is the page's: by z-index layer, in tree order within one (the walk found them in tree order, and the sort is stable).
            var ordered = new List<(int Z, int Index, BoxFragment Fragment)>(chosen.Count);
            for (var i = 0; i < chosen.Count; i++) ordered.Add((chosen[i].Z, i, chosen[i].Fragment));
            ordered.Sort((a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Index.CompareTo(b.Index));

            var roots = new List<BoxFragment>(ordered.Count);
            foreach (var entry in ordered) roots.Add(entry.Fragment);
            _plan = new OverlayPlan(boxes, roots);
        }

        private static int LayerOf(BoxFragment fragment) => StackingOrder.LayerOf(new StackingOrder.StackingParticipant(fragment, []));

        // What does not change from step to step: where the candidates are in the tree, and what else is positioned.
        private void EnsureStructure(IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            if (ReferenceEquals(_structureFor, fragmentainers) && _structureRevision == _promotionRevision) return;

            _structureFor = fragmentainers;
            _structureRevision = _promotionRevision;
            _candidates = [];
            _others = [];

            var root = fragmentainers[0].Root;
            FindCandidates(root);

            foreach (var participant in StackingOrder.Flatten(root))
            {
                var box = participant.Box;
                if (!(box.IsPositioned || DomUtils.IsStackingContextBox(box))) continue;

                var extent = FragmentPainter.SubtreeExtent(participant.Fragment) ?? participant.Fragment.WholeBoxRect;
                var bleed = FragmentPainter.SubtreeBleed(participant.Fragment) + 2;
                _others.Add(new OverlayOther(box, Grow(extent, bleed), participant.Fragment.WholeBoxRect.X, participant.Fragment.WholeBoxRect.Y,
                    StackingOrder.LayerOf(participant), box.Position.Value is PositionMode.Fixed or PositionMode.Sticky));
            }
        }

        private void FindCandidates(BoxFragment fragment)
        {
            foreach (var child in fragment.Children)
            {
                var box = child.Box;
                if (box.IsMarkerPseudoElement) continue;

                if (_byBox.TryGetValue(box, out var state) && state.Compositor && box.Position.Value is not (PositionMode.Fixed or PositionMode.Sticky))
                {
                    // A box in a layer below the page's own content (a negative z-index) paints under the text and floats that are not positioned, which the
                    // animated layer, drawn over the whole of the content, would put it over.
                    if (LayerOf(child) < 0) continue;

                    var extent = FragmentPainter.SubtreeExtent(child) ?? child.WholeBoxRect;
                    _candidates.Add(new OverlayCandidate(state, child, Grow(extent, FragmentPainter.SubtreeBleed(child) + 2)));
                    continue;
                }

                // Nothing under a box that composites, clips or reorders what is in it can be drawn on its own.
                if (!IsPlain(box)) continue;
                FindCandidates(child);
            }
        }

        private static bool IsPlain(CssBox box) =>
            !DomUtils.ClipsItsOverflow(box) && (box.IsRoot || !DomUtils.IsStackingContextBox(box));

        private bool CollidesWithOthers(OverlayCandidate candidate)
        {
            var box = candidate.Fragment.Box;
            var mine = CurrentBounds(box, candidate.Extent, candidate.Fragment.WholeBoxRect.X, candidate.Fragment.WholeBoxRect.Y);
            var layer = LayerOf(candidate.Fragment);

            foreach (var other in _others)
            {
                if (ReferenceEquals(other.Box, box)) continue;

                // What is painted before the box (a lower layer) is under it either way. A box the viewport holds is drawn over the animated layer whatever its z-index.
                if (!other.Attached && other.Z < layer) continue;

                var theirs = CurrentBounds(other.Box, other.Extent, other.OriginX, other.OriginY);
                if (other.Attached && _container.View.DisplacementOf(other.Box) is var (dx, dy) && (dx != 0 || dy != 0))
                    theirs = new Rect(theirs.X + dx, theirs.Y + dy, theirs.Width, theirs.Height);

                if (mine.Left < theirs.Right && theirs.Left < mine.Right && mine.Top < theirs.Bottom && theirs.Top < mine.Bottom) return true;
            }

            return false;
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

            foreach (var (page, list) in rects)
            {
                foreach (var rect in Merge(list)) result.Add(new DirtyRegion(page, rect));
            }

            return result;
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
