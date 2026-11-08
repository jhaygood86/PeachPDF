using PeachBrackets.Css;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The break-point model of a vertical-writing-mode multi-column container's content: the flow
    /// laid out as one long strip, the places css-break-3 lets it be cut, and the cut itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is in <b>logical block coordinates</b>: the distance along the block axis from the
    /// container's block-start edge (its right edge for <c>vertical-rl</c>, its left edge for
    /// <c>vertical-lr</c>), which is the direction content flows within a column. A column ("strip") is
    /// the same block-axis range of the long strip shifted back to the block-start edge and down to its
    /// own place along the inline axis.
    /// </para>
    /// <para>
    /// The break points are the ones css-break-3 §4.1 lists for block flow: between sibling blocks (§4.1,
    /// rule 1) and between line boxes inside a block (rule 2), never inside either. A box that is not
    /// itself cut - a replaced element, a table, a flex or grid container, an orthogonal flow, a float -
    /// is one indivisible piece, as css-break-3 §2 says monolithic content is; one that is taller than the
    /// column overflows it rather than being cut.
    /// </para>
    /// </remarks>
    internal static partial class VerticalColumnFlow
    {
        private const double Epsilon = 0.01;

        /// <summary>The container's block axis as physical X, which is all the logical coordinates need.</summary>
        internal readonly record struct BlockAxis(double ClientLeft, double ClientRight, bool BlockStartIsRight)
        {
            /// <summary>The logical block range of the physical X range <paramref name="x0"/>..<paramref name="x1"/>.</summary>
            internal (double Start, double End) Logical(double x0, double x1) => BlockStartIsRight
                ? (ClientRight - x1, ClientRight - x0)
                : (x0 - ClientLeft, x1 - ClientLeft);

            /// <summary>The physical X range of the logical block range.</summary>
            internal (double Left, double Right) Physical(double start, double end) => BlockStartIsRight
                ? (ClientRight - end, ClientRight - start)
                : (ClientLeft + start, ClientLeft + end);

            /// <summary>The physical X displacement that takes content back by <paramref name="logicalOffset"/>.</summary>
            internal double Displacement(double logicalOffset) => BlockStartIsRight ? logicalOffset : -logicalOffset;
        }

        /// <summary>One thing the flow can be cut around: a line box, an indivisible block, or a block holding either.</summary>
        internal sealed class Node(CssBox box, CssLineBox? line, double start, double end)
        {
            internal CssBox Box { get; } = box;
            internal CssLineBox? Line { get; } = line;
            internal double Start { get; set; } = start;
            internal double End { get; set; } = end;
            internal List<Node> Kids { get; } = [];

            /// <summary>Floats of this block: they ride with the strip their position lands in and never decide a break.</summary>
            internal List<Node> Anchored { get; } = [];

            /// <summary>Whether this is a <c>column-span: all</c> child of the container, which takes every column's place.</summary>
            internal bool IsSpanning { get; set; }

            internal bool BreakBefore { get; set; }
            internal bool BreakAfter { get; set; }
            internal bool AvoidInside { get; set; }
            internal int Orphans { get; set; } = 1;
            internal int Widows { get; set; } = 1;
            internal int Strip { get; set; } = -1;

            /// <summary>The box's top and bottom on the long strip, before the flow was cut.</summary>
            internal double VirtualTop { get; set; }

            internal double VirtualBottom { get; set; }

            /// <summary>The block range of this node in each strip it has content in, logical, before the shift.</summary>
            internal SortedDictionary<int, (double Start, double End)> Pieces { get; } = [];

            internal bool IsLeaf => Kids.Count == 0;
        }

        /// <summary>Builds the break-point tree of <paramref name="container"/>'s laid-out content.</summary>
        internal static Node Build(CssBox container, BlockAxis axis)
        {
            var root = new Node(container, null, 0, 0);
            Fill(root, axis);
            if (root.Kids.Count > 0) root.Start = root.Kids[0].Start;
            foreach (var kid in root.Kids) kid.IsSpanning = kid.Box.ColumnSpan.Value == ColumnSpanMode.All;
            return root;
        }

        private static void Fill(Node node, BlockAxis axis)
        {
            var box = node.Box;
            node.Orphans = Math.Max(1, int.TryParse(box.Orphans, out var o) ? o : 1);
            node.Widows = Math.Max(1, int.TryParse(box.Widows, out var w) ? w : 1);

            if (DomUtils.ContainsInlinesOnly(box))
            {
                foreach (var line in box.LineBoxes)
                {
                    double lo = double.MaxValue, hi = double.MinValue;

                    foreach (var rect in line.Rectangles.Values)
                    {
                        if (rect.Width <= 0 && rect.Height <= 0) continue;
                        lo = Math.Min(lo, rect.Left);
                        hi = Math.Max(hi, rect.Right);
                    }

                    foreach (var word in line.Words)
                    {
                        lo = Math.Min(lo, word.Left);
                        hi = Math.Max(hi, word.Right);
                    }

                    if (lo > hi) continue;

                    var (start, end) = axis.Logical(lo, hi);
                    node.Kids.Add(new Node(box, line, start, end));
                }

                foreach (var child in box.Boxes)
                {
                    if (child.IsFloated && child.DerivedStyle.ActualDisplay != Keywords.None)
                    {
                        node.Anchored.Add(WholeBlock(child, axis));
                    }
                }

                return;
            }

            foreach (var child in box.Boxes)
            {
                if (child.DerivedStyle.ActualDisplay == Keywords.None || CssBox.IsOutsideMarker(child)
                    || child.IsRunningPositioned) continue;

                if (child.IsFloated)
                {
                    node.Anchored.Add(WholeBlock(child, axis));
                    continue;
                }

                if (child.IsOutOfFlow) continue;

                var kid = WholeBlock(child, axis);

                if (IsCut(child))
                {
                    Fill(kid, axis);
                    if (kid.Kids.Count > 0)
                    {
                        // A cut block keeps its own border box as its range, but never starts after its first
                        // piece of content or ends before its last one: a child can overhang the box that holds it.
                        kid.Start = Math.Min(kid.Start, kid.Kids[0].Start);
                        kid.End = Math.Max(kid.End, kid.Kids[^1].End);
                    }
                }

                node.Kids.Add(kid);
            }
        }

        private static Node WholeBlock(CssBox box, BlockAxis axis)
        {
            var (start, end) = axis.Logical(box.Location.X, box.ActualRight);
            return new Node(box, null, start, end)
            {
                BreakBefore = BreakValues.IsForcedBreak(box.BreakBefore.Value, FragmentationContext.Column),
                BreakAfter = BreakValues.IsForcedBreak(box.BreakAfter.Value, FragmentationContext.Column),
                AvoidInside = BreakValues.AvoidsBreak(box.BreakInside.Value, FragmentationContext.Column)
            };
        }

        /// <summary>
        /// Whether a block child is cut between its own lines and children, rather than moved as one piece.
        /// </summary>
        private static bool IsCut(CssBox child) =>
            child.DerivedStyle.ActualDisplay is var display
            && (display == Keywords.Block || display == Keywords.ListItem)
            && child.WritingMode.Value.IsVertical()
            && child.PlacesItselfAsBlockBox
            && !child.EstablishesMultiColumnContext
            && !child.IsImage
            && !MonolithicContent.IsMonolithic(child)
            && (child.Boxes.Count > 0 || child.LineBoxes.Count > 0);

        // ─── Packing ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One column of the container: a range of the long strip that becomes a strip along the inline
        /// axis. A <c>column-span: all</c> child is a strip of its own that spans the whole inline axis.
        /// </summary>
        internal sealed class Strip(int segment, int index, bool isSpan, double start)
        {
            /// <summary>Which run of columns - or which spanning child - this strip belongs to, in block order.</summary>
            internal int Segment { get; } = segment;

            /// <summary>The position of this strip within its run, from the inline start.</summary>
            internal int Index { get; } = index;

            internal bool IsSpan { get; } = isSpan;

            /// <summary>Where this strip begins on the long strip.</summary>
            internal double Start { get; } = start;

            /// <summary>Where this strip begins along the block axis of the container, once the runs are placed one after another.</summary>
            internal double Origin { get; set; }

            /// <summary>How far content on the long strip moves back along the block axis to land in this strip.</summary>
            internal double Shift => Start - Origin;

            /// <summary>How far it moves down the inline axis.</summary>
            internal double Dy { get; set; }

            /// <summary>This strip's inline extent: the column size, or the whole inline axis for a spanning child.</summary>
            internal double Height { get; set; }

            /// <summary>How much of the block axis this strip's content takes, from its origin.</summary>
            internal double Extent { get; set; }
        }

        /// <summary>
        /// Lays the flow out in strips: each run of columns between two <c>column-span: all</c> children is
        /// filled and balanced on its own (css-multicol-1 section 7.1), the runs and the spanning children
        /// following one another along the block axis. Returns every strip in block order and the block
        /// extent they take together.
        /// </summary>
        internal static (List<Strip> Strips, double Used) PackAll(Node root, int count, double available, bool balances)
        {
            var strips = new List<Strip>();
            var origin = 0.0;
            var previousEnd = 0.0;
            var segment = 0;
            var run = new List<Node>();

            void Finish()
            {
                if (run.Count == 0) return;

                var room = Math.Max(0, available - origin);
                var size = balances ? BalancedSize(run, count, room) : room;
                var runStrips = Pack(run, size, segment);
                var by = strips.Count;

                // The gap between the runs - a margin that did not collapse - is part of the flow.
                origin += Math.Max(0, run[0].Start - previousEnd);

                foreach (var node in run) Rebase(node, by);
                foreach (var node in run) MeasureExtents(node, runStrips, by);

                var used = 0.0;
                foreach (var strip in runStrips)
                {
                    strip.Origin = origin;
                    strips.Add(strip);
                    used = Math.Max(used, strip.Extent);
                }

                origin += used;
                previousEnd = run[^1].End;
                segment++;
                run.Clear();
            }

            foreach (var kid in root.Kids)
            {
                if (!kid.IsSpanning)
                {
                    run.Add(kid);
                    continue;
                }

                Finish();

                var span = new Strip(segment, 0, isSpan: true, kid.Start) { Origin = origin + Math.Max(0, kid.Start - previousEnd) };
                span.Extent = kid.End - kid.Start;
                origin = span.Origin + span.Extent;
                previousEnd = kid.End;
                AssignAll(kid, strips.Count);
                strips.Add(span);
                segment++;
            }

            Finish();

            // Floats ride with the strip whose range holds their start.
            AssignAnchored(root, strips);

            return (strips, origin);
        }

        private static void AssignAll(Node node, int strip)
        {
            if (node.IsLeaf) node.Strip = strip;
            foreach (var kid in node.Kids) AssignAll(kid, strip);
        }

        private static void Rebase(Node node, int by)
        {
            if (node.IsLeaf && node.Strip >= 0) node.Strip += by;
            foreach (var kid in node.Kids) Rebase(kid, by);
        }

        private static void MeasureExtents(Node node, List<Strip> runStrips, int by)
        {
            if (node.IsLeaf && node.Strip >= 0)
            {
                var strip = runStrips[node.Strip - by];
                strip.Extent = Math.Max(strip.Extent, node.End - strip.Start);
            }

            foreach (var kid in node.Kids) MeasureExtents(kid, runStrips, by);
        }

        /// <summary>
        /// Assigns every piece of <paramref name="run"/> to a strip of <paramref name="blockSize"/> and
        /// returns the strips, in order. The strip indexes recorded on the nodes are positions in the
        /// returned list; <see cref="PackAll"/> rebases them onto the whole list.
        /// </summary>
        internal static List<Strip> Pack(IReadOnlyList<Node> run, double blockSize, int segment)
        {
            var strips = new List<Strip> { new(segment, 0, isSpan: false, run.Count > 0 ? run[0].Start : 0) };
            var current = strips[0].Start;
            var hasContent = false;
            var forceNext = false;

            void StartStrip(double at)
            {
                strips.Add(new Strip(segment, strips.Count, isSpan: false, at));
                current = at;
                hasContent = false;
                forceNext = false;
            }

            void Place(Node node)
            {
                if (node.BreakBefore || forceNext)
                {
                    if (hasContent) StartStrip(node.Start);
                    forceNext = false;
                }

                if (node.IsLeaf)
                {
                    if (hasContent && node.End - current > blockSize + Epsilon) StartStrip(node.Start);

                    node.Strip = strips.Count - 1;
                    hasContent = true;
                }
                else
                {
                    // break-inside: avoid-column - move a block that would fit a fresh strip rather than split it.
                    if (node.AvoidInside && hasContent && node.End - node.Start <= blockSize + Epsilon
                        && node.End - current > blockSize + Epsilon)
                    {
                        StartStrip(node.Start);
                    }

                    PlaceKids(node);
                }

                if (node.BreakAfter) forceNext = true;
            }

            void PlaceKids(Node node)
            {
                if (node.Kids[0].Line is not null)
                {
                    PlaceLines(node);
                    return;
                }

                foreach (var kid in node.Kids) Place(kid);
            }

            void PlaceLines(Node node)
            {
                // Lines are the finest break points (css-break-3 §4.1): the strip ends after as many of them as
                // fit, moved earlier or later so the block keeps at least `orphans` lines at the end of a
                // strip and `widows` at the start of the next (css-break-3 §3.3) when it has lines to spare.
                var lines = node.Kids;
                var index = 0;

                while (index < lines.Count)
                {
                    var fit = 0;
                    while (index + fit < lines.Count
                           && ((!hasContent && fit == 0) || lines[index + fit].End - current <= blockSize + Epsilon))
                    {
                        fit++;
                    }

                    var remaining = lines.Count - index;

                    if (fit < remaining)
                    {
                        var mayMoveWholeBlock = index == 0 && hasContent;

                        if (fit < node.Orphans)
                        {
                            if (mayMoveWholeBlock) fit = 0;
                        }
                        else if (remaining - fit < node.Widows)
                        {
                            var shorter = remaining - node.Widows;
                            if (shorter >= node.Orphans) fit = shorter;
                            else if (mayMoveWholeBlock) fit = 0;
                        }
                    }

                    for (var i = 0; i < fit; i++) lines[index + i].Strip = strips.Count - 1;
                    if (fit > 0) hasContent = true;
                    index += fit;

                    if (index < lines.Count) StartStrip(lines[index].Start);
                }
            }

            foreach (var kid in run) Place(kid);

            return strips;
        }

        private static void AssignAnchored(Node node, List<Strip> strips)
        {
            foreach (var floated in node.Anchored)
            {
                var strip = 0;
                for (var i = 0; i < strips.Count; i++)
                {
                    if (!strips[i].IsSpan && strips[i].Start <= floated.Start + Epsilon) strip = i;
                }

                floated.Strip = strip;
            }

            foreach (var kid in node.Kids) AssignAnchored(kid, strips);
        }

        /// <summary>
        /// The least strip size, no larger than <paramref name="available"/>, that holds <paramref name="run"/>
        /// in <paramref name="count"/> strips - <c>column-fill: balance</c>.
        /// </summary>
        internal static double BalancedSize(IReadOnlyList<Node> run, int count, double available)
        {
            if (run.Count == 0) return available;
            if (Pack(run, available, 0).Count > count) return available;

            var low = 0.0;
            var high = available;

            for (var i = 0; i < 28 && high - low > 0.05; i++)
            {
                var mid = (low + high) / 2;
                if (Pack(run, mid, 0).Count <= count) high = mid;
                else low = mid;
            }

            return high;
        }

        private static void Visit(Node node, Action<Node> action)
        {
            action(node);
            foreach (var kid in node.Kids) Visit(kid, action);
        }

        // ─── Moving ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves every piece of the flow from the long strip to its strip: back by where the strip begins
        /// along the block axis, and along the inline axis to the strip's own place.
        /// </summary>
        internal static void Move(Node root, BlockAxis axis, List<Strip> strips)
        {
            ComputePieces(root);
            foreach (var kid in root.Kids) MoveNode(kid, axis, strips);
            MoveAnchored(root, axis, strips);
        }

        private static void MoveAnchored(Node node, BlockAxis axis, List<Strip> strips)
        {
            foreach (var floated in node.Anchored)
            {
                var dx = axis.Displacement(strips[floated.Strip].Shift);
                var dy = strips[floated.Strip].Dy;
                if (dx != 0) floated.Box.OffsetLeft(dx);
                if (dy != 0) floated.Box.OffsetTop(dy);
            }
        }

        /// <summary>The strips <paramref name="node"/> has content in, and the logical range it covers in each.</summary>
        private static void ComputePieces(Node node)
        {
            node.Pieces.Clear();
            node.VirtualTop = node.Box.Location.Y;
            node.VirtualBottom = node.Box.ActualBottom;

            if (node.IsLeaf)
            {
                if (node.Strip >= 0) node.Pieces[node.Strip] = (node.Start, node.End);
                return;
            }

            foreach (var kid in node.Kids)
            {
                ComputePieces(kid);
                foreach (var (strip, range) in kid.Pieces)
                {
                    node.Pieces[strip] = node.Pieces.TryGetValue(strip, out var existing)
                        ? (Math.Min(existing.Start, range.Start), Math.Max(existing.End, range.End))
                        : range;
                }
            }

            if (node.Pieces.Count == 0) return;

            // The box's own start and end belong to its first and last piece: its padding and border begin and
            // end the box, and a break in between is a slice with neither (box-decoration-break: slice).
            var first = node.Pieces.Keys.First();
            var last = node.Pieces.Keys.Last();
            var firstRange = node.Pieces[first];
            node.Pieces[first] = (Math.Min(firstRange.Start, node.Start), firstRange.End);
            var lastRange = node.Pieces[last];
            node.Pieces[last] = (lastRange.Start, Math.Max(lastRange.End, node.End));
        }

        private static void MoveNode(Node node, BlockAxis axis, List<Strip> strips)
        {
            if (node.Pieces.Count == 0) return;

            if (node.Line is { } line)
            {
                MoveLine(line, axis.Displacement(strips[node.Strip].Shift), strips[node.Strip].Dy);
                return;
            }

            if (node.Pieces.Count == 1)
            {
                // All of it lands in one strip: it moves as the one subtree it is, decorations and all.
                var strip = node.Pieces.Keys.First();
                var dx = axis.Displacement(strips[strip].Shift);
                var dy = strips[strip].Dy;
                if (dx != 0) node.Box.OffsetLeft(dx);
                if (dy != 0) node.Box.OffsetTop(dy);
                return;
            }

            foreach (var kid in node.Kids) MoveNode(kid, axis, strips);
            MoveAnchored(node, axis, strips);

            // The box the pieces belong to reports its last piece, as a box that continues from one column
            // into the next does: the earlier pieces are what the nested fragmentainers state.
            var lastStrip = node.Pieces.Keys.Last();
            var (start, end) = node.Pieces[lastStrip];
            var (left, right) = axis.Physical(start - strips[lastStrip].Shift, end - strips[lastStrip].Shift);
            var dyLast = strips[lastStrip].Dy;
            // ActualRight and ActualBottom are derived from the location and the size, so the location is set
            // first and the far edges after it.
            node.Box.Location = new PaintPoint(left, node.VirtualTop + dyLast);
            node.Box.ActualRight = right;
            node.Box.ActualBottom = node.VirtualBottom + dyLast;
        }

        /// <summary>Moves one line box - its words, rectangles and atomic inline boxes - by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
        private static void MoveLine(CssLineBox line, double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;

            var movedWithBox = new HashSet<CssRect>();

            // An atomic inline (an inline-block, an image) is a box of its own with a subtree and a position of
            // its own, which the line's word list holds only a placeholder for.
            foreach (var word in line.Words)
            {
                var owner = word.OwnerBox;
                if (ReferenceEquals(owner, line.OwnerBox) || owner.DerivedStyle.ActualDisplay == Keywords.Inline) continue;

                foreach (var ownedWord in owner.Words) movedWithBox.Add(ownedWord);
                if (dx != 0) owner.OffsetLeft(dx);
                if (dy != 0) owner.OffsetTop(dy);
            }

            foreach (var word in line.Words)
            {
                if (movedWithBox.Contains(word)) continue;
                word.Left += dx;
                word.Top += dy;
            }

            foreach (var box in line.Rectangles.Keys.ToList())
            {
                var rect = line.Rectangles[box];
                var moved = new Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
                line.Rectangles[box] = moved;

                if (box.RectanglesView.ContainsKey(line))
                {
                    box.Rectangles[line] = moved;
                }
            }

            if (line.FlowTop is { } flowTop) line.FlowTop = flowTop + dy;
            if (line.BaselineY is { } baselineY) line.BaselineY = baselineY + dy;
        }
    }
}
