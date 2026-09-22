using PeachPDF.CSS;
using System;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Paint
{
    /// <summary>
    /// Where two collapsed grid lines cross, exactly one of them paints the whole <i>joint square</i>
    /// their two widths span - and at the table's own four corners one of them has to, or the corner is
    /// left blank, which is what
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/1257">issue #1257</see> was. CSS 2.1
    /// §17.6.2 resolves a border per segment and says nothing about the square two segments share, so
    /// the rule these pin is measured browser behaviour - see
    /// <c>CssLayoutEngineTable.InlineLineOwnsJoint</c>'s own remarks for the four fixtures it was read
    /// off Chrome 153 with, and for why it is stated in logical (inline-start/block-start) rather than
    /// physical terms.
    /// </summary>
    /// <remarks>
    /// Every assertion here is about <i>coverage</i> of the joint square rather than about any one
    /// segment's rect, because the two are not the same claim: a line can reach into a joint and still
    /// be painted over by the line that owns it. The owner is therefore the last covering segment in
    /// <see cref="CssBox.CollapsedBorderSegments"/>' own order, which is the order
    /// <c>FragmentPainter</c> paints them in.
    /// </remarks>
    public class CollapsedBorderJointTests
    {
        private const double Tolerance = 0.01;

        /// <summary>
        /// A 2×2 grid of 60pt × 40pt cells, laid out and reduced to what a joint assertion needs: the
        /// three grid-line centres on each axis, read off the real laid-out cells and rows rather than
        /// re-derived, and the segments themselves in paint order.
        /// </summary>
        private static async Task<Grid> LayoutGridAsync(string cellBorder)
        {
            var cell = $"<td style='width:60pt;height:40pt;padding:0;{cellBorder}'></td>";

            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<table style='border-collapse:collapse'><tr>{cell}{cell}</tr><tr>{cell}{cell}</tr></table>"));

            var table = LayoutHarness.Descendants(root)
                .First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

            var rows = table.Boxes.SelectMany(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow ? [b] : b.Boxes)
                .Where(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow)
                .ToList();
            Assert.Equal(2, rows.Count);

            var firstRowCells = rows[0].Boxes.Where(b => b.DerivedStyle.ActualDisplay == Keywords.TableCell).ToList();
            Assert.Equal(2, firstRowCells.Count);

            Assert.NotNull(table.CollapsedBorderSegments);

            return new Grid(
                table,
                table.CollapsedBorderSegments!,
                [firstRowCells[0].Location.X, firstRowCells[0].ActualRight, firstRowCells[1].ActualRight],
                [rows[0].Location.Y, rows[0].ActualBottom, rows[1].ActualBottom]);
        }

        private sealed record Grid(
            CssBox Table,
            IReadOnlyList<CollapsedBorderSegment> Segments,
            double[] InlineCentres,
            double[] BlockCentres)
        {
            /// <summary>
            /// Whether the block-axis (horizontal) line owns the joint at
            /// (<paramref name="inlineLine"/>, <paramref name="blockLine"/>) - having first asserted that
            /// something covers the whole square, since "nobody painted it" is the defect these exist for
            /// and would otherwise read as an ownership answer.
            /// </summary>
            public bool BlockAxisOwnsJoint(int inlineLine, int blockLine, double inlineWidth, double blockWidth)
            {
                var left = InlineCentres[inlineLine] - inlineWidth / 2;
                var right = InlineCentres[inlineLine] + inlineWidth / 2;
                var top = BlockCentres[blockLine] - blockWidth / 2;
                var bottom = BlockCentres[blockLine] + blockWidth / 2;

                var covering = Segments
                    .Where(s => s.Rect.Left <= left + Tolerance && s.Rect.Right >= right - Tolerance &&
                                s.Rect.Top <= top + Tolerance && s.Rect.Bottom >= bottom - Tolerance)
                    .ToList();

                Assert.NotEmpty(covering);
                return covering[^1].IsHorizontal;
            }
        }

        [Fact]
        public async Task EveryJointIncludingTheFourOuterCorners_IsFullyPaintedByAGridLine()
        {
            // The corners are the reason this file exists: each outer one is where a run used to stop at
            // the perpendicular line's centre from both directions at once, leaving a quarter of the
            // square blank (and, before the table's box grew to hold the whole outer line, outside the
            // table entirely). BlockAxisOwnsJoint asserts full coverage before answering, so simply
            // asking about all nine joints is the assertion - which line wins each is
            // AtEqualWidthAndStyle_OnlyTheBlockStartLineGivesAJointAway_AndNotAtTheInlineStartEdge's
            // business, so the answer is discarded here.
            var grid = await LayoutGridAsync("border:20pt solid #000");

            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                for (var blockLine = 0; blockLine <= 2; blockLine++)
                {
                    _ = grid.BlockAxisOwnsJoint(inlineLine, blockLine, 20, 20);
                }
            }
        }

        [Fact]
        public async Task NoSegmentIsPaintedOutsideTheTablesOwnBorderBox()
        {
            // The other half of issue #1257: the outermost lines used to be centred on the table's own
            // border-box edges, so their outer halves painted over whatever preceded the table (measured
            // at 10px of a 20px border, over the block immediately above) and were clipped away entirely
            // at a page or container edge, leaving an outer line half the thickness of an interior one.
            var grid = await LayoutGridAsync("border:20pt solid #000");

            Assert.All(grid.Segments, s =>
            {
                Assert.True(s.Rect.Left >= grid.Table.Location.X - Tolerance,
                    $"segment at {s.Rect.Left} is left of the table's own {grid.Table.Location.X}");
                Assert.True(s.Rect.Top >= grid.Table.Location.Y - Tolerance,
                    $"segment at {s.Rect.Top} is above the table's own {grid.Table.Location.Y}");
                Assert.True(s.Rect.Right <= grid.Table.ActualRight + Tolerance,
                    $"segment to {s.Rect.Right} is right of the table's own {grid.Table.ActualRight}");
                Assert.True(s.Rect.Bottom <= grid.Table.ActualBottom + Tolerance,
                    $"segment to {s.Rect.Bottom} is below the table's own {grid.Table.ActualBottom}");
            });

            // And the box is no larger than it needs to be: its edges are the outermost lines' own outer
            // edges, half a line beyond the centres the cells meet at.
            Assert.Equal(grid.InlineCentres[0] - 10, grid.Table.Location.X, 3);
            Assert.Equal(grid.BlockCentres[0] - 10, grid.Table.Location.Y, 3);
            Assert.Equal(grid.InlineCentres[2] + 10, grid.Table.ActualRight, 3);
            Assert.Equal(grid.BlockCentres[2] + 10, grid.Table.ActualBottom, 3);
        }

        [Fact]
        public async Task AtEqualWidthAndStyle_OnlyTheBlockStartLineGivesAJointAway_AndNotAtTheInlineStartEdge()
        {
            // The measured tiebreak: logical side order inline-start < block-start < inline-end <
            // block-end, later wins. The block-axis line is the joint's owning cell's block-start side
            // only on line 0, and the inline-axis line is its inline-start side only on line 0 - so the
            // inline-axis line takes a joint in exactly one row of the grid, and not at its first column.
            //
            // The two axes carry different COLOURS, which is what makes ownership observable at all: two
            // borders identical in style, width and colour are deliberately left to whichever line is
            // already painting there, since the square would come out the same paint either way.
            var grid = await LayoutThickAsync(inlineWidth: 20, blockWidth: 20, differentColors: true);

            Assert.True(grid.BlockAxisOwnsJoint(0, 0, 20, 20));
            Assert.False(grid.BlockAxisOwnsJoint(1, 0, 20, 20));
            Assert.False(grid.BlockAxisOwnsJoint(2, 0, 20, 20));

            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                Assert.True(grid.BlockAxisOwnsJoint(inlineLine, 1, 20, 20));
                Assert.True(grid.BlockAxisOwnsJoint(inlineLine, 2, 20, 20));
            }
        }

        [Fact]
        public async Task ALostJointIsPaintedOverTheCrossingRun_NotCutOutOfIt()
        {
            // A joint is taken by painting a square OVER the line that loses it, rather than by
            // splitting that line around the square - so the losing line stays unbroken. That matters
            // because the segment meant to fill a cut-out square can fail to reach a page (a multi-page
            // table's own body dividers do not), and a hole in a grid line reads as a defect where the
            // wrong paint merely reads as a different colour. Asserted structurally: every joint on the
            // block-start line is lost to a column line here, and the row line still runs across all of
            // them in one piece.
            var grid = await LayoutThickAsync(inlineWidth: 20, blockWidth: 20, differentColors: true);

            var blockStartLine = grid.Segments
                .Where(s => s.IsHorizontal && Math.Abs(s.Rect.Y + s.Rect.Height / 2 - grid.BlockCentres[0]) < Tolerance)
                .ToList();

            var unbroken = Assert.Single(blockStartLine);
            Assert.Equal(grid.InlineCentres[0] - 10, unbroken.Rect.Left, 3);
            Assert.Equal(grid.InlineCentres[2] + 10, unbroken.Rect.Right, 3);

            // ...and the squares that take those two joints are painted after it, which is what settles
            // ownership when nothing is cut out.
            var unbrokenIndex = grid.Segments.ToList().IndexOf(unbroken);
            Assert.InRange(unbrokenIndex, 0, grid.Segments.Count - 2);
        }

        [Fact]
        public async Task AWinnerWhoseStyleLeavesGaps_IsCutOutOfTheLosingRunInstead()
        {
            // `double` cannot take a joint by being painted over the crossing line, because that line
            // goes on showing through the gap between its two rules - Chrome leaves that gap empty. The
            // losing run is cut back clear of the square instead, and the winner's own run fills it.
            // Both directions, since the two axes implement the cut independently.
            var inlineWins = await LayoutThickAsync(
                inlineWidth: 20, blockWidth: 20, inlineStyle: "double", blockStyle: "solid");

            // The block-start line loses every joint to the `double` column lines, so it is emitted in
            // pieces that stop clear of each of them rather than as one run.
            var blockStartPieces = inlineWins.Segments
                .Where(s => s.IsHorizontal &&
                            Math.Abs(s.Rect.Y + s.Rect.Height / 2 - inlineWins.BlockCentres[0]) < Tolerance)
                .OrderBy(s => s.Rect.Left)
                .ToList();

            Assert.Equal(2, blockStartPieces.Count);
            Assert.All(blockStartPieces, s => Assert.All(
                inlineWins.InlineCentres,
                centre => Assert.True(s.Rect.Left >= centre + 10 - Tolerance || s.Rect.Right <= centre - 10 + Tolerance,
                    $"piece {s.Rect.Left}..{s.Rect.Right} reaches into the square at {centre}")));

            // Mirrored: `double` row lines take every joint, so the column lines are the ones cut.
            var blockWins = await LayoutThickAsync(
                inlineWidth: 20, blockWidth: 20, inlineStyle: "solid", blockStyle: "double");

            var inlineStartPieces = blockWins.Segments
                .Where(s => !s.IsHorizontal &&
                            Math.Abs(s.Rect.X + s.Rect.Width / 2 - blockWins.InlineCentres[0]) < Tolerance)
                .ToList();

            Assert.Equal(2, inlineStartPieces.Count);
            Assert.All(inlineStartPieces, s => Assert.All(
                blockWins.BlockCentres,
                centre => Assert.True(s.Rect.Top >= centre + 10 - Tolerance || s.Rect.Bottom <= centre - 10 + Tolerance,
                    $"piece {s.Rect.Top}..{s.Rect.Bottom} reaches into the square at {centre}")));
        }

        [Fact]
        public async Task TwoIdenticalSolidBorders_GetNoJointSquareOfTheirOwn()
        {
            // Ownership is only worth realizing when it changes what is painted. Two borders identical
            // in style, width and colour come out the same either way, so the crossing run simply keeps
            // the square - no extra rect, and (the reason it matters beyond size) no second pass of
            // paint over a translucent one. A bevel is the exception and has its own coverage above:
            // its two faces say which line the square belongs to even when both borders match.
            var uniform = await LayoutGridAsync("border:20pt solid #000");
            var perAxis = await LayoutThickAsync(inlineWidth: 20, blockWidth: 20, differentColors: true);

            Assert.True(perAxis.Segments.Count > uniform.Segments.Count,
                $"expected extra joint squares once the axes differ: {uniform.Segments.Count} vs {perAxis.Segments.Count}");

            // Every joint is still covered either way - skipping a square never leaves one unpainted.
            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                for (var blockLine = 0; blockLine <= 2; blockLine++)
                {
                    _ = uniform.BlockAxisOwnsJoint(inlineLine, blockLine, 20, 20);
                }
            }
        }

        [Fact]
        public async Task AWiderInlineAxisLine_TakesEveryJoint_IncludingTheOneItLosesAtEqualWidth()
        {
            // Width comes before the side tiebreak, so a wider inline-axis line takes even joint (0,0),
            // the one it is the inline-start side of.
            var grid = await LayoutThickAsync(inlineWidth: 30, blockWidth: 10);

            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                for (var blockLine = 0; blockLine <= 2; blockLine++)
                {
                    Assert.False(grid.BlockAxisOwnsJoint(inlineLine, blockLine, 30, 10));
                }
            }
        }

        [Fact]
        public async Task AWiderBlockAxisLine_TakesEveryJoint_IncludingTheOneItLosesAtEqualWidth()
        {
            // The mirror of the above, and the reason the width arm is not "the inline-axis line always
            // wins when the widths differ".
            var grid = await LayoutThickAsync(inlineWidth: 10, blockWidth: 30);

            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                for (var blockLine = 0; blockLine <= 2; blockLine++)
                {
                    Assert.True(grid.BlockAxisOwnsJoint(inlineLine, blockLine, 10, 30));
                }
            }
        }

        [Fact]
        public async Task AtEqualWidth_TheHigher17_6_2StylePriority_TakesEveryJoint()
        {
            // double outranks solid in §17.6.2's own style order, and that arm is consulted before the
            // side tiebreak - so `double` inline-axis lines take joint (0,0) too.
            var grid = await LayoutThickAsync(inlineWidth: 20, blockWidth: 20, inlineStyle: "double", blockStyle: "solid");

            for (var inlineLine = 0; inlineLine <= 2; inlineLine++)
            {
                for (var blockLine = 0; blockLine <= 2; blockLine++)
                {
                    Assert.False(grid.BlockAxisOwnsJoint(inlineLine, blockLine, 20, 20));
                }
            }
        }

        private static async Task<Grid> LayoutThickAsync(
            double inlineWidth, double blockWidth, string inlineStyle = "solid", string blockStyle = "solid",
            bool differentColors = false)
        {
            var inlineColor = differentColors ? "#00f" : "#000";
            var border =
                $"border-left:{inlineWidth}pt {inlineStyle} {inlineColor};border-right:{inlineWidth}pt {inlineStyle} {inlineColor};" +
                $"border-top:{blockWidth}pt {blockStyle} #000;border-bottom:{blockWidth}pt {blockStyle} #000";

            return await LayoutGridAsync(border);
        }
    }
}
