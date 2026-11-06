using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html
{
    /// <summary>How far a scroll container can scroll and where it is, in layout units (CSS Overflow 3 §2: the scrollport and the scrollable overflow area).</summary>
    /// <param name="X">how far the content is scrolled to the left</param>
    /// <param name="Y">how far the content is scrolled up</param>
    /// <param name="MaxX">the most it can be scrolled to the left (zero when it does not overflow on that axis)</param>
    /// <param name="MaxY">the most it can be scrolled up</param>
    /// <param name="Port">the scrollport: the container's padding box, in the surface's space</param>
    /// <param name="ContentWidth">the width of the scrollable overflow area</param>
    /// <param name="ContentHeight">the height of the scrollable overflow area</param>
    internal readonly record struct ScrollState(double X, double Y, double MaxX, double MaxY, Rect Port, double ContentWidth, double ContentHeight);

    /// <summary>
    /// The scroll containers of a continuous document - boxes whose <c>overflow</c> is <c>auto</c>, <c>scroll</c> or <c>hidden</c> (CSS Overflow 3 §2) - and how far each is
    /// scrolled. A scroll container's content is drawn carried back by that offset, inside the container's padding box; layout is not touched, so scrolling
    /// costs a repaint of the scrollport and nothing else. A paged document has no scrolling: print shows the whole of the content.
    /// </summary>
    internal sealed partial class ViewportState
    {
        private readonly Dictionary<CssBox, (double X, double Y)> _scroll = new(ReferenceEqualityComparer.Instance);
        private ScrollIndex? _scrollIndex;

        /// <summary>Whether any scroll container is scrolled away from its start.</summary>
        internal bool HasScroll => _scroll.Count > 0;

        /// <summary>How far <paramref name="box"/> is scrolled when it is a scroll container.</summary>
        internal (double X, double Y) ScrollOffsetOf(CssBox box) => _scroll.TryGetValue(box, out var offset) ? offset : default;

        /// <summary>
        /// The box whose scrolling carries <paramref name="box"/>: the one it is clipped by when it is in flow, and the containing block when it is absolutely
        /// positioned (which a scroller that is not its containing block does not move). A fixed box is held by the viewport and is carried by no scroller.
        /// </summary>
        private static CssBox? ScrollParentOf(CssBox box) => box.Position.Value switch
        {
            PositionMode.Fixed => null,
            PositionMode.Absolute => DomUtils.ClippingContainingBlockOf(box),
            _ => box.EffectiveParentBox
        };

        /// <summary>
        /// How far <paramref name="box"/> as a whole is moved by the scrolling of what holds it, and by the viewport: the shift to push before painting it.
        /// A scroll container's own text is moved by its own offset too, which is <see cref="ContentDisplacementOf"/>.
        /// </summary>
        internal bool TryGetEntryShift(CssBox box, out (double Dx, double Dy) shift)
        {
            var found = TryGetOwn(box, out shift);

            if (_scroll.Count > 0 && ScrollParentOf(box) is { } scroller && _scroll.TryGetValue(scroller, out var offset))
            {
                shift = (shift.Dx - offset.X, shift.Dy - offset.Y);
                found = true;
            }

            return found;
        }

        /// <summary>How far the text and inline content a scroll container holds directly is moved: its own displacement and how far it is scrolled itself.</summary>
        internal (double Dx, double Dy) ContentDisplacementOf(CssBox box)
        {
            var shift = DisplacementOf(box);
            if (_scroll.Count > 0 && _scroll.TryGetValue(box, out var offset))
                shift = (shift.Dx - offset.X, shift.Dy - offset.Y);

            return shift;
        }

        /// <summary>
        /// How far the clip of <paramref name="clipper"/> has to be moved to be drawn for <paramref name="box"/>: the clip is the clipper's padding box in the clipper's own frame,
        /// and the frame <paramref name="box"/> is painted in has been carried back by the scrolling of every scroll container from the clipper down to the box.
        /// </summary>
        internal (double Dx, double Dy) ClipShiftFor(CssBox box, CssBox clipper)
        {
            if (_scroll.Count == 0)
                return default;

            double dx = 0, dy = 0;
            for (var current = box; current is not null && !ReferenceEquals(current, clipper);)
            {
                var scroller = ScrollParentOf(current);
                if (scroller is null) break;

                if (_scroll.TryGetValue(scroller, out var offset))
                {
                    dx += offset.X;
                    dy += offset.Y;
                }

                if (ReferenceEquals(scroller, clipper)) break;
                current = scroller;
            }

            return (dx, dy);
        }

        /// <summary>Whether <paramref name="box"/> is a scroll container of the laid-out document.</summary>
        internal bool IsScrollContainer(HtmlContainerInt container, CssBox box) => IndexFor(container)?.Entries.ContainsKey(box) == true;

        /// <summary>Every scroll container of the laid-out document, innermost last within each branch.</summary>
        internal IEnumerable<CssBox> ScrollContainers(HtmlContainerInt container) => IndexFor(container)?.Entries.Keys ?? (IEnumerable<CssBox>)[];

        /// <summary>A scrollbar under a point: its container, which axis, the part of it, and its track and thumb as they are drawn (layout units).</summary>
        internal readonly record struct ScrollbarTarget(CssBox Box, bool Vertical, int Part, Rect Track, Rect Thumb, double Max);

        /// <summary>
        /// The scrollbar under a point of the surface (layout units): a thumb, or the track before or after it (<c>Part</c> -1, 0 or 1 for before, thumb, after). The innermost
        /// container whose scrollport holds the point is the one asked.
        /// </summary>
        internal ScrollbarTarget? ScrollbarAt(HtmlContainerInt container, double x, double y)
        {
            if (IndexFor(container) is not { Entries.Count: > 0 } index)
                return null;

            ScrollbarTarget? best = null;
            var bestDepth = -1;
            foreach (var (box, entry) in index.Entries)
            {
                if (!PortHolds(entry.Port, box, x, y) || !VisibleThroughScrollers(index, box, x, y)) continue;

                var state = ScrollStateOf(container, box)!.Value;
                var (shiftX, shiftY) = DisplacementOf(box);
                var (vertical, horizontal) = ScrollbarGeometry.Of(box, state, container.RenderContext.LayoutUnitsPerPoint);
                foreach (var (shape, isVertical) in new[] { (vertical, true), (horizontal, false) })
                {
                    if (shape is not { } bar) continue;

                    var track = new Rect(bar.Track.X + shiftX, bar.Track.Y + shiftY, bar.Track.Width, bar.Track.Height);
                    var thumb = new Rect(bar.Thumb.X + shiftX, bar.Thumb.Y + shiftY, bar.Thumb.Width, bar.Thumb.Height);
                    if (!(x >= track.X && x < track.Right && y >= track.Y && y < track.Bottom)) continue;

                    var along = isVertical ? y : x;
                    var thumbStart = isVertical ? thumb.Y : thumb.X;
                    var thumbEnd = isVertical ? thumb.Bottom : thumb.Right;
                    var part = along < thumbStart ? -1 : along >= thumbEnd ? 1 : 0;

                    var depth = 0;
                    for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox) depth++;
                    if (depth > bestDepth)
                    {
                        best = new ScrollbarTarget(box, isVertical, part, track, thumb, isVertical ? state.MaxY : state.MaxX);
                        bestDepth = depth;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The scroll container that holds <paramref name="from"/> (the box itself first) and can scroll the way asked, nearest first; used to send a key to the container the
        /// focus is in.
        /// </summary>
        internal CssBox? ScrollContainerFor(HtmlContainerInt container, CssBox from, double dx, double dy)
        {
            if (IndexFor(container) is not { Entries.Count: > 0 } index)
                return null;

            for (var current = from; current is not null; current = ScrollParentOf(current))
            {
                if (!index.Entries.TryGetValue(current, out var entry)) continue;

                var offset = ScrollOffsetOf(current);
                var scrollsX = dx != 0 && (dx > 0 ? offset.X < entry.MaxX : offset.X > 0);
                var scrollsY = dy != 0 && (dy > 0 ? offset.Y < entry.MaxY : offset.Y > 0);
                if ((dx == 0 && dy == 0) || scrollsX || scrollsY) return current;
            }

            return null;
        }

        /// <summary>
        /// The scrollport of <paramref name="box"/> as it is drawn, when it is a scroll container: what its content is clipped to, so a point outside it is not on the content.
        /// </summary>
        internal bool TryGetScrollPort(HtmlContainerInt container, CssBox box, out Rect port)
        {
            port = default;
            if (box.Overflow.Value is Overflow.Visible or Overflow.Clip || !container.Continuous || IndexFor(container) is not { } index || !index.Entries.TryGetValue(box, out var entry))
                return false;

            var (dx, dy) = DisplacementOf(box);
            port = new Rect(entry.Port.X + dx, entry.Port.Y + dy, entry.Port.Width, entry.Port.Height);
            return true;
        }

        /// <summary>The scroll container's scrollport and how far it can scroll, or null when the box is not a scroll container of the laid-out document.</summary>
        internal ScrollState? ScrollStateOf(HtmlContainerInt container, CssBox box)
        {
            if (IndexFor(container) is not { } index || !index.Entries.TryGetValue(box, out var entry))
                return null;

            var offset = ScrollOffsetOf(box);
            return new ScrollState(offset.X, offset.Y, entry.MaxX, entry.MaxY, entry.Port, entry.ContentWidth, entry.ContentHeight);
        }

        /// <summary>
        /// Scrolls <paramref name="box"/> to (<paramref name="x"/>, <paramref name="y"/>), kept within what it can scroll. Returns the scrollport that looks different, in
        /// the surface's space, or null when nothing moved (or the box is not a scroll container).
        /// </summary>
        internal Rect? SetScroll(HtmlContainerInt container, CssBox box, double x, double y)
        {
            if (ScrollStateOf(container, box) is not { } state)
                return null;

            x = double.IsNaN(x) ? state.X : Math.Clamp(x, 0, state.MaxX);
            y = double.IsNaN(y) ? state.Y : Math.Clamp(y, 0, state.MaxY);
            if (x == state.X && y == state.Y)
                return null;

            if (x == 0 && y == 0)
                _scroll.Remove(box);
            else
                _scroll[box] = (x, y);

            // Where the scrollport is drawn: the container itself may be carried by a scroller of its own.
            var shift = DisplacementOf(box);
            return new Rect(state.Port.X + shift.Dx, state.Port.Y + shift.Dy, state.Port.Width, state.Port.Height);
        }

        /// <summary>Whether the laid-out document has any scroll container, which lets a caller that looks for one under a point skip its walk.</summary>
        internal bool HasScrollContainers(HtmlContainerInt container) => IndexFor(container) is { Entries.Count: > 0 };

        /// <summary>
        /// The innermost scroll container whose scrollport holds the point (in the surface's space, layout units) and that can scroll the way asked (<paramref name="dx"/>,
        /// <paramref name="dy"/>; 0 asks for either), or null.
        /// </summary>
        internal CssBox? ScrollContainerAt(HtmlContainerInt container, double x, double y, double dx, double dy)
        {
            if (IndexFor(container) is not { Entries.Count: > 0 } index)
                return null;

            // Innermost first: the first container that can go the way asked has it, and one that cannot but says its overscroll stays with it (overscroll-behavior: contain or
            // none on that axis, CSS Overscroll Behavior 1 §3) keeps it from the containers around.
            var holders = new List<(CssBox Box, ScrollIndex.Entry Entry, int Depth)>();
            foreach (var (box, entry) in index.Entries)
            {
                if (!PortHolds(entry.Port, box, x, y) || !VisibleThroughScrollers(index, box, x, y)) continue;

                var depth = 0;
                for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox) depth++;
                holders.Add((box, entry, depth));
            }

            holders.Sort((p, q) => q.Depth.CompareTo(p.Depth));
            foreach (var (box, entry, _) in holders)
            {
                if (dx == 0 && dy == 0) return box;

                // Asked for a direction, the container must be able to go that way on an axis that was asked for.
                var offset = ScrollOffsetOf(box);
                var scrollsX = dx != 0 && (dx > 0 ? offset.X < entry.MaxX : offset.X > 0);
                var scrollsY = dy != 0 && (dy > 0 ? offset.Y < entry.MaxY : offset.Y > 0);
                if (scrollsX || scrollsY) return box;

                if ((dx != 0 && OverscrollContained(box, vertical: false)) || (dy != 0 && OverscrollContained(box, vertical: true)))
                    return box;
            }

            return null;
        }

        // The scrollport as it is drawn: carried by whatever scrolls the container itself.
        private bool PortHolds(Rect port, CssBox box, double x, double y)
        {
            var (dx, dy) = DisplacementOf(box);
            return x >= port.X + dx && x < port.Right + dx && y >= port.Y + dy && y < port.Bottom + dy;
        }

        // A point under a scroll container that is itself scrolled out of the way of an outer one is not on the inner one.
        private bool VisibleThroughScrollers(ScrollIndex index, CssBox box, double x, double y)
        {
            for (var current = box; current is not null;)
            {
                var scroller = ScrollParentOf(current);
                if (scroller is null) break;

                if (index.Entries.TryGetValue(scroller, out var outer) && !PortHolds(outer.Port, scroller, x, y)) return false;
                current = scroller;
            }

            return true;
        }

        /// <summary>
        /// Scrolls the scroll containers that hold <paramref name="box"/>, outermost first, by as little as brings it into view. Returns each one that moved with the scrollport that
        /// looks different.
        /// </summary>
        internal List<(CssBox Scroller, Rect Port)> ScrollIntoView(HtmlContainerInt container, CssBox box, int behavior = 0)
        {
            var moved = new List<(CssBox, Rect)>();
            if (IndexFor(container) is not { Entries.Count: > 0 } index || FragmentRectOf(container, box) is not { } own)
                return moved;

            var chain = new List<CssBox>();
            for (var current = box; current is not null;)
            {
                var scroller = ScrollParentOf(current);
                if (scroller is null) break;
                if (index.Entries.ContainsKey(scroller)) chain.Add(scroller);
                current = scroller;
            }

            chain.Reverse();
            var margins = Margins(box);
            foreach (var scroller in chain)
            {
                var entry = index.Entries[scroller];

                // Where the element is drawn now, with every shift in place: its margin box, against the scrollport less the container's scroll-padding.
                var (sx, sy) = DisplacementOf(box);
                var rect = new Rect(own.X + sx - margins.Left, own.Y + sy - margins.Top, own.Width + margins.Left + margins.Right, own.Height + margins.Top + margins.Bottom);
                var (px, py) = DisplacementOf(scroller);
                var padding = Paddings(scroller, entry.Port);
                var port = new Rect(entry.Port.X + px + padding.Left, entry.Port.Y + py + padding.Top,
                    Math.Max(0, entry.Port.Width - padding.Left - padding.Right), Math.Max(0, entry.Port.Height - padding.Top - padding.Bottom));

                var offset = ScrollOffsetOf(scroller);
                var targetY = offset.Y;
                if (rect.Y < port.Y || rect.Height > port.Height) targetY += rect.Y - port.Y;
                else if (rect.Bottom > port.Bottom) targetY += rect.Bottom - port.Bottom;

                var targetX = offset.X;
                if (rect.X < port.X || rect.Width > port.Width) targetX += rect.X - port.X;
                else if (rect.Right > port.Right) targetX += rect.Right - port.Right;

                if (Navigate(container, scroller, targetX, targetY, snap: true, behavior) is { } changed) moved.Add((scroller, changed));
            }

            return moved;
        }

        /// <summary>The rectangle of <paramref name="box"/> in the layout, before anything scrolls or sticks it; null when it has no fragment.</summary>
        private static Rect? FragmentRectOf(HtmlContainerInt container, CssBox box)
        {
            if (container.FragmentTree is not { Fragmentainers.Count: > 0 } tree)
                return null;

            Rect? Find(BoxFragment fragment)
            {
                if (ReferenceEquals(fragment.Box, box)) return fragment.WholeBoxRect;
                foreach (var child in fragment.Children)
                {
                    if (Find(child) is { } found) return found;
                }

                return null;
            }

            return Find(tree.Fragmentainers[0].Root);
        }

        private ScrollIndex? IndexFor(HtmlContainerInt container)
        {
            var tree = container.FragmentTree;
            if (!container.Continuous || tree is not { Fragmentainers.Count: > 0 })
            {
                _scrollIndex = null;
                if (_scroll.Count > 0) _scroll.Clear();
                return null;
            }

            if (_scrollIndex is null || !ReferenceEquals(_scrollIndex.Tree, tree))
            {
                _scrollIndex = ScrollIndex.Build(tree);

                // A layout that no longer has a container (or has it somewhere new) keeps the offset only where the container still scrolls.
                if (_scroll.Count > 0)
                {
                    foreach (var box in new List<CssBox>(_scroll.Keys))
                    {
                        if (!_scrollIndex.Entries.TryGetValue(box, out var entry))
                        {
                            _scroll.Remove(box);
                            continue;
                        }

                        var offset = _scroll[box];
                        var clamped = (Math.Clamp(offset.X, 0, entry.MaxX), Math.Clamp(offset.Y, 0, entry.MaxY));
                        if (clamped.Item1 == 0 && clamped.Item2 == 0) _scroll.Remove(box);
                        else _scroll[box] = clamped;
                    }
                }
            }

            return _scrollIndex;
        }

        /// <summary>The scroll containers of one layout, each with its scrollport and the extent of what it holds.</summary>
        private sealed class ScrollIndex
        {
            internal sealed record Entry(BoxFragment Fragment, Rect Port, double MaxX, double MaxY, double ContentWidth, double ContentHeight);

            private ScrollIndex(FragmentTree tree) => Tree = tree;

            internal FragmentTree Tree { get; }

            internal Dictionary<CssBox, Entry> Entries { get; } = new(ReferenceEqualityComparer.Instance);

            internal static ScrollIndex Build(FragmentTree tree)
            {
                var index = new ScrollIndex(tree);
                if (tree.Fragmentainers.Count > 0)
                    index.Collect(tree.Fragmentainers[0].Root);

                return index;
            }

            private void Collect(BoxFragment fragment)
            {
                var box = fragment.Box;
                if (!Entries.ContainsKey(box) && DomUtils.IsScrollerOnScreen(box))
                    Entries[box] = Measure(fragment);

                foreach (var child in fragment.Children)
                    Collect(child);
            }

            private static Entry Measure(BoxFragment fragment)
            {
                var box = fragment.Box;
                var port = RenderUtils.PaddingEdgeOf(box, fragment.WholeBoxRect);

                // The scrollable overflow area: what the container holds (its own text and every box under it), and the padding at the far end of the content.
                double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
                void Add(Rect rect)
                {
                    if (!(rect.Width > 0) || !(rect.Height > 0) || double.IsNaN(rect.X + rect.Y + rect.Width + rect.Height)) return;
                    right = Math.Max(right, rect.Right);
                    bottom = Math.Max(bottom, rect.Bottom);
                }

                foreach (var word in fragment.Words) Add(word.Rect);

                // What a nested scroll container holds is clipped to it, so only its own box counts here; a fixed box is held by the viewport and does not
                // belong to the scrollable area of what it sits in.
                void AddBelow(BoxFragment node)
                {
                    foreach (var child in node.Children)
                    {
                        if (child.Box.Position.Value == PositionMode.Fixed) continue;

                        Add(child.WholeBoxRect);
                        Add(child.Rect);
                        foreach (var line in child.Lines) Add(line.Rect);
                        foreach (var word in child.Words) Add(word.Rect);

                        if (!DomUtils.IsScrollerOnScreen(child.Box)) AddBelow(child);
                    }
                }

                AddBelow(fragment);

                var maxX = right > port.Right ? Math.Max(0, right + box.ActualPaddingRight - port.Right) : 0;
                var maxY = bottom > port.Bottom ? Math.Max(0, bottom + box.ActualPaddingBottom - port.Bottom) : 0;

                // An axis that does not scroll (overflow-x: hidden next to overflow-y: auto is still scrollable by script, but visible and clip are not scrollers).
                if (box.OverflowX.Value is Overflow.Visible or Overflow.Clip) maxX = 0;
                if (box.OverflowY.Value is Overflow.Visible or Overflow.Clip) maxY = 0;

                return new Entry(fragment, port, maxX, maxY, port.Width + maxX, port.Height + maxY);
            }
        }
    }
}
