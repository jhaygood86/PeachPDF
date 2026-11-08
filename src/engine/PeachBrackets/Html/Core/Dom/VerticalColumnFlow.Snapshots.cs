using PeachBrackets.Css;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Dom
{
    internal static partial class VerticalColumnFlow
    {
        /// <summary>The strip each line box of the flow landed in.</summary>
        internal static Dictionary<CssLineBox, int> LineStrips(Node root)
        {
            var map = new Dictionary<CssLineBox, int>();
            Visit(root, node =>
            {
                if (node.Line is not null && node.Strip >= 0) map[node.Line] = node.Strip;
            });
            return map;
        }

        /// <summary>
        /// Every box with content in strip <paramref name="strip"/> and the boxes that carry on past it: the
        /// geometry the fragment emitter reads that strip's fragments from.
        /// </summary>
        /// <remarks>
        /// A box cut across strips has one live position - its last piece's - so the other pieces exist
        /// nowhere but here: each strip states the lines and words that landed in it and the rectangle the
        /// box covers there, the way a column of a horizontal container is captured while its boxes still
        /// hold that column's geometry.
        /// </remarks>
        internal static (BoxGeometrySnapshot Snapshot, HashSet<CssBox> Continuing) SnapshotOf(
            Node root, int strip, BlockAxis axis, List<Strip> strips, Dictionary<CssLineBox, int> lineStrips)
        {
            var snapshot = BoxGeometrySnapshot.CreateEmpty();
            var continuing = new HashSet<CssBox>(ReferenceEqualityComparer.Instance);

            bool LineIsHere(CssLineBox? line) => line is not null && lineStrips.TryGetValue(line, out var s) && s == strip;

            bool InStrip(CssBox box)
            {
                foreach (var line in box.RectangleKeys)
                {
                    if (LineIsHere(line)) return true;
                }

                foreach (var word in box.Words)
                {
                    if (LineIsHere(word.Line)) return true;
                }

                return false;
            }

            bool HoldsInline(CssBox box)
            {
                if (InStrip(box)) return true;

                foreach (var child in box.Boxes)
                {
                    if (!child.IsOutOfFlow && HoldsInline(child)) return true;
                }

                return false;
            }

            void StateLinesOf(CssBox box, BoxGeometrySnapshot.BoxGeometry geometry)
            {
                foreach (var (line, rect) in box.RectanglesView)
                {
                    if (LineIsHere(line)) geometry.Rectangles[line] = rect;
                }

                foreach (var word in box.Words)
                {
                    geometry.WordOrigins.Add(LineIsHere(word.Line) ? new PaintPoint(word.Left, word.Top) : null);
                }
            }

            void AddInlineContent(CssBox box)
            {
                foreach (var child in box.Boxes)
                {
                    if (child.IsOutOfFlow) continue;

                    if (child.DerivedStyle.ActualDisplay == Keywords.Inline)
                    {
                        if (!HoldsInline(child)) continue;

                        var geometry = snapshot.Add(child, child.Location, child.ActualRight, child.ActualBottom);
                        StateLinesOf(child, geometry);
                        AddInlineContent(child);
                    }
                    else if (InStrip(child))
                    {
                        // An atomic inline sits on one line and its whole subtree moved with it.
                        snapshot.AddWhole(child);
                    }
                }
            }

            void AddBlock(Node node)
            {
                if (!node.Pieces.TryGetValue(strip, out var piece)) return;

                if (node.Pieces.Count == 1)
                {
                    snapshot.AddWhole(node.Box);
                    return;
                }

                var (left, right) = axis.Physical(piece.Start - strips[strip].Shift, piece.End - strips[strip].Shift);
                var dy = strips[strip].Dy;
                var geometry = snapshot.Add(
                    node.Box, new PaintPoint(left, node.VirtualTop + dy), right, node.VirtualBottom + dy);

                if (node.Pieces.Keys.Last() > strip) continuing.Add(node.Box);

                if (node.Kids.Count > 0 && node.Kids[0].Line is not null)
                {
                    StateLinesOf(node.Box, geometry);
                    AddInlineContent(node.Box);
                }
                else
                {
                    foreach (var kid in node.Kids) AddBlock(kid);
                }

                foreach (var floated in node.Anchored)
                {
                    if (floated.Strip == strip) snapshot.AddWhole(floated.Box);
                }
            }

            foreach (var kid in root.Kids) AddBlock(kid);

            foreach (var floated in root.Anchored)
            {
                if (floated.Strip == strip) snapshot.AddWhole(floated.Box);
            }

            return (snapshot, continuing);
        }
    }
}
