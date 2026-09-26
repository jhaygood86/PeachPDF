using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// <see cref="GlyphInkScanner"/> — the geometry behind <c>text-decoration-skip-ink</c>: which parts
    /// of a horizontal band a glyph's ink actually occupies.
    /// </summary>
    /// <remarks>
    /// Synthetic outlines rather than a real font, so every expected number is derivable from the fixture
    /// rather than from whatever a particular typeface happens to draw. The units are font design units,
    /// y-up, exactly as <see cref="GlyphOutlineDecoder"/> produces them.
    /// </remarks>
    public class GlyphInkScannerTests
    {
        [Fact]
        public void ABandThroughASolidBox_IsEntirelyInk()
        {
            var outline = Outline(Square(0, 0, 100, 100));

            var spans = GlyphInkScanner.Crossings(outline, 40, 60);

            var only = Assert.Single(spans);
            Assert.Equal(0, only.Start, 3);
            Assert.Equal(100, only.End, 3);
        }

        [Fact]
        public void ABandAboveTheGlyph_FindsNoInk()
        {
            var outline = Outline(Square(0, 0, 100, 100));

            Assert.Empty(GlyphInkScanner.Crossings(outline, 200, 220));
        }

        [Fact]
        public void ABandBelowTheGlyph_FindsNoInk()
        {
            var outline = Outline(Square(0, 0, 100, 100));

            Assert.Empty(GlyphInkScanner.Crossings(outline, -80, -60));
        }

        [Fact]
        public void ACounter_IsNotTreatedAsInk()
        {
            // Resolving each scanline by nonzero winding rather than by "leftmost to rightmost crossing"
            // is what keeps this class an honest report of where the glyph is painted: the hole in an 'o'
            // is genuinely unpainted, so it comes back as a gap between two runs. Whether a decoration
            // line is nevertheless broken across that gap is the caller's decision, not this one's - see
            // GraphicsAdapter.MeasureInkCrossings, which hulls a glyph's runs per CSS Text Decoration 4
            // §2.10.5. Were this to report (0, 100) instead, that choice would no longer be the caller's
            // to make and a genuinely two-piece glyph could not be told from a solid one.
            var outline = Outline(Square(0, 0, 100, 100), ReversedSquare(20, 20, 80, 80));

            var spans = GlyphInkScanner.Crossings(outline, 45, 55);

            Assert.Equal(2, spans.Count);
            Assert.Equal((0, 20), Rounded(spans[0]));
            Assert.Equal((80, 100), Rounded(spans[1]));
        }

        [Fact]
        public void TwoSeparateContours_ProduceTwoSpans()
        {
            var outline = Outline(Square(0, 0, 30, 100), Square(70, 0, 100, 100));

            var spans = GlyphInkScanner.Crossings(outline, 40, 60);

            Assert.Equal(2, spans.Count);
            Assert.Equal((0, 30), Rounded(spans[0]));
            Assert.Equal((70, 100), Rounded(spans[1]));
        }

        [Fact]
        public void TouchingContours_AreMergedIntoOneSpan()
        {
            var outline = Outline(Square(0, 0, 50, 100), Square(50, 0, 100, 100));

            var only = Assert.Single(GlyphInkScanner.Crossings(outline, 40, 60));
            Assert.Equal((0, 100), Rounded(only));
        }

        [Fact]
        public void ADiagonalStroke_IsReportedAcrossTheWholeBandNotJustItsMiddle()
        {
            // A stroke leaning across the band sits at a different x at each edge. Sampling both edges is
            // what makes the result reach all of it; a single mid-band scanline would report only the
            // middle and leave the ends of the stroke unskipped.
            var outline = Outline(Contour((0, 0), (20, 0), (120, 100), (100, 100)));

            var spans = GlyphInkScanner.Crossings(outline, 0, 100);

            Assert.NotEmpty(spans);
            Assert.Equal(0, spans[0].Start, 3);
            // The topmost scanline is taken a hair inside the band's upper edge (see
            // GlyphInkScanner.TopSampleInset), so the reported right edge falls just short of the
            // stroke's own 120 rather than landing exactly on it.
            Assert.Equal(120, spans[^1].End, 1);
        }

        [Fact]
        public void ADiagonalStroke_UnderARealisticallyThinBand_IsOneSpan()
        {
            // The band a decoration line actually asks about is its own thickness - a fraction of an em,
            // not the whole glyph. At that scale consecutive scanlines land on nearly the same x and the
            // runs overlap into one, which is what keeps a skip gap from fragmenting across an italic
            // stroke. (The wide-band fixture above is deliberately extreme: there the samples separate,
            // and the consumer's own clearance dilation is what closes them - see
            // FragmentPainter.AddInkExclusions.)
            var outline = Outline(Contour((0, 0), (20, 0), (120, 100), (100, 100)));

            var only = Assert.Single(GlyphInkScanner.Crossings(outline, 48, 52));

            Assert.True(only.End - only.Start >= 20,
                $"the span should be at least the stroke's own width, got {only.End - only.Start}");
        }

        [Fact]
        public void ACubicOutline_IsFlattenedAndScanned()
        {
            // A rounded shape reaches paint entirely as cubics, so the flattener has to be what finds it.
            var contour = new OutlineContour(new OutlinePoint(0, 50));
            contour.SegmentList.Add(OutlineSegment.Cubic(
                new OutlinePoint(0, 90), new OutlinePoint(100, 90), new OutlinePoint(100, 50)));
            contour.SegmentList.Add(OutlineSegment.Cubic(
                new OutlinePoint(100, 10), new OutlinePoint(0, 10), new OutlinePoint(0, 50)));

            var only = Assert.Single(GlyphInkScanner.Crossings(Outline(contour), 45, 55));

            Assert.True(only.Start <= 0.5 && only.End >= 99.5,
                $"the band through the middle of the blob should span nearly its full width, got {only.Start}..{only.End}");
        }

        [Fact]
        public void AnEmptyOutline_FindsNoInk()
        {
            Assert.Empty(GlyphInkScanner.Crossings(new GlyphOutline(), 0, 10));
        }

        [Fact]
        public void AnInvertedBand_FindsNoInk()
        {
            Assert.Empty(GlyphInkScanner.Crossings(Outline(Square(0, 0, 100, 100)), 60, 40));
        }

        [Fact]
        public void AZeroHeightBand_FindsNoInk()
        {
            Assert.Empty(GlyphInkScanner.Crossings(Outline(Square(0, 0, 100, 100)), 50, 50));
        }

        [Fact]
        public void AVertexExactlyOnAScanline_DoesNotCancelTheStem()
        {
            // The half-open crossing test exists for this: counting a vertex on the scanline from both of
            // its edges would sum to a winding of zero and punch a hole through the middle of a stem.
            // Band edges land exactly on the notch vertices at y = 0 and y = 100.
            var outline = Outline(Contour((0, 0), (100, 0), (100, 100), (50, 100), (50, 50), (0, 50)));

            var spans = GlyphInkScanner.Crossings(outline, 0, 100);

            Assert.NotEmpty(spans);
            Assert.Equal(0, spans[0].Start, 3);
            Assert.Equal(100, spans[^1].End, 3);
        }

        // ─── Fixture helpers ─────────────────────────────────────────────────────

        private static GlyphOutline Outline(params OutlineContour[] contours)
        {
            var outline = new GlyphOutline();
            outline.ContourList.AddRange(contours);
            return outline;
        }

        /// <summary>
        /// A closed contour through <paramref name="points"/>. The closing segment is deliberately left
        /// off, exactly as <see cref="GlyphOutlineDecoder"/> leaves it off - a `glyf` contour is closed by
        /// definition, and closing it is the scanner's own job.
        /// </summary>
        private static OutlineContour Contour(params (double X, double Y)[] points)
        {
            var contour = new OutlineContour(new OutlinePoint(points[0].X, points[0].Y));

            foreach (var (x, y) in points.Skip(1))
            {
                contour.SegmentList.Add(OutlineSegment.Line(new OutlinePoint(x, y)));
            }

            return contour;
        }

        private static OutlineContour Square(double left, double bottom, double right, double top) =>
            Contour((left, bottom), (right, bottom), (right, top), (left, top));

        /// <summary>The same square wound the other way - a counter, which nonzero winding subtracts.</summary>
        private static OutlineContour ReversedSquare(double left, double bottom, double right, double top) =>
            Contour((left, bottom), (left, top), (right, top), (right, bottom));

        private static (double, double) Rounded((double Start, double End) span) =>
            (System.Math.Round(span.Start, 3), System.Math.Round(span.End, 3));
    }
}
