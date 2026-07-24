using System;
using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Unit + integration coverage of <see cref="GlyphOutlineDecoder"/>. The contour-building math
    /// (implied on-curve midpoints, quadratic-to-cubic elevation, all-off-curve start synthesis) is
    /// tested directly through <see cref="GlyphOutlineDecoder.BuildContourForTest"/>; the byte-level
    /// simple/composite decode is tested end-to-end against the bundled Source Sans 3 TrueType font.
    /// </summary>
    public class GlyphOutlineDecoderTests
    {
        private const double Eps = 1e-9;

        private static OpenTypeFontface Face(string path)
            => XFontSource.GetOrCreateFrom(File.ReadAllBytes(path)).Fontface;

        private static int Gid(OpenTypeFontface face, char ch)
        {
            var descriptor = new OpenTypeDescriptor("outline-test", "outline-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            return descriptor.CharCodeToGlyphIndex(new System.Text.Rune(ch));
        }

        private static (double X, double Y, bool On) P(double x, double y, bool on) => (x, y, on);

        // ---- Contour-building math (no font needed) --------------------------------------------

        [Fact]
        public void BuildContour_AllOnCurveSquare_ProducesOnlyLineSegments()
        {
            var contour = GlyphOutlineDecoder.BuildContourForTest(
                [P(0, 0, true), P(10, 0, true), P(10, 10, true), P(0, 10, true)]);

            Assert.NotNull(contour);
            Assert.Equal(new GlyphOutlinePoint(0, 0), contour!.Start);
            Assert.Equal(4, contour.Segments.Count);
            Assert.All(contour.Segments, s => Assert.False(s.IsCubic));
            // Walk visits the remaining corners in order, then closes back to the start.
            Assert.Equal(new GlyphOutlinePoint(10, 0), contour.Segments[0].End);
            Assert.Equal(new GlyphOutlinePoint(10, 10), contour.Segments[1].End);
            Assert.Equal(new GlyphOutlinePoint(0, 10), contour.Segments[2].End);
            Assert.Equal(new GlyphOutlinePoint(0, 0), contour.Segments[3].End);
        }

        [Fact]
        public void BuildContour_SingleQuadratic_ElevatesToCubicWithTwoThirdsControlPoints()
        {
            // on (0,0) -> off (6,12) -> on (12,0)
            var contour = GlyphOutlineDecoder.BuildContourForTest(
                [P(0, 0, true), P(6, 12, false), P(12, 0, true)]);

            Assert.NotNull(contour);
            // First segment is the elevated quadratic.
            GlyphSegment quad = contour!.Segments[0];
            Assert.True(quad.IsCubic);
            AssertPoint(4, 8, quad.Control1);   // (0,0) + 2/3*((6,12)-(0,0)) = (4,8)
            AssertPoint(8, 8, quad.Control2);   // (12,0) + 2/3*((6,12)-(12,0)) = (8,8)
            AssertPoint(12, 0, quad.End);
            // Second segment closes back to the start.
            Assert.False(contour.Segments[1].IsCubic);
            AssertPoint(0, 0, contour.Segments[1].End);
        }

        [Fact]
        public void BuildContour_TwoConsecutiveOffCurve_InsertsImpliedOnCurveMidpoint()
        {
            // on (0,0) -> off (4,8) -> off (8,8) -> on (12,0)
            var contour = GlyphOutlineDecoder.BuildContourForTest(
                [P(0, 0, true), P(4, 8, false), P(8, 8, false), P(12, 0, true)]);

            Assert.NotNull(contour);
            Assert.Equal(3, contour!.Segments.Count);
            // First cubic ends at the implied midpoint of the two off-curve points.
            Assert.True(contour.Segments[0].IsCubic);
            AssertPoint(6, 8, contour.Segments[0].End);   // midpoint((4,8),(8,8))
            Assert.True(contour.Segments[1].IsCubic);
            AssertPoint(12, 0, contour.Segments[1].End);
            // Closing line back to start.
            Assert.False(contour.Segments[2].IsCubic);
            AssertPoint(0, 0, contour.Segments[2].End);
        }

        [Fact]
        public void BuildContour_AllOffCurve_SynthesizesMidpointStartAndAllCubics()
        {
            // Entirely off-curve: start is the midpoint of the first and last points.
            var contour = GlyphOutlineDecoder.BuildContourForTest(
                [P(0, 0, false), P(10, 0, false), P(5, 10, false)]);

            Assert.NotNull(contour);
            AssertPoint(2.5, 5, contour!.Start);          // midpoint((0,0),(5,10))
            Assert.Equal(3, contour.Segments.Count);
            Assert.All(contour.Segments, s => Assert.True(s.IsCubic));
            AssertPoint(2.5, 5, contour.Segments[^1].End); // closes back to the synthesized start
        }

        [Fact]
        public void BuildContour_Empty_ReturnsNull()
        {
            Assert.Null(GlyphOutlineDecoder.BuildContourForTest([]));
        }

        // ---- Byte-level decode against a real TrueType font -------------------------------------

        [Fact]
        public void TryGetGlyphOutline_SimpleGlyph_DecodesExpectedContourCounts()
        {
            var face = Face(BundledFonts.Ttf);

            // 'l' is a single stroke (one contour); 'o' is a ring plus its counter (two contours).
            Assert.True(GlyphOutlineDecoder.TryGetGlyphOutline(face, Gid(face, 'l'), out var lOutline));
            Assert.Single(lOutline.Contours);

            Assert.True(GlyphOutlineDecoder.TryGetGlyphOutline(face, Gid(face, 'o'), out var oOutline));
            Assert.Equal(2, oOutline.Contours.Count);
            Assert.All(oOutline.Contours, c => Assert.NotEmpty(c.Segments));
        }

        [Fact]
        public void TryGetGlyphOutline_CompositeGlyph_AddsAccentContoursAboveTheBase()
        {
            var face = Face(BundledFonts.Ttf);

            Assert.True(GlyphOutlineDecoder.TryGetGlyphOutline(face, Gid(face, 'a'), out var a));
            Assert.True(GlyphOutlineDecoder.TryGetGlyphOutline(face, Gid(face, 'á'), out var aAcute));

            // The precomposed 'á' pulls in the acute accent, so it has strictly more contours than 'a'...
            Assert.True(aAcute.Contours.Count > a.Contours.Count,
                $"expected 'á' ({aAcute.Contours.Count}) to have more contours than 'a' ({a.Contours.Count})");
            // ...and the component-offset transform places that accent above the base letter.
            Assert.True(MaxY(aAcute) > MaxY(a),
                $"expected 'á' top ({MaxY(aAcute)}) above 'a' top ({MaxY(a)})");
        }

        [Fact]
        public void TryGetGlyphOutline_EmptyGlyph_ReturnsFalse()
        {
            var face = Face(BundledFonts.Ttf);

            // The space glyph has an advance but no contours.
            Assert.False(GlyphOutlineDecoder.TryGetGlyphOutline(face, Gid(face, ' '), out var outline));
            Assert.True(outline.IsEmpty);
        }

        [Fact]
        public void TryGetGlyphOutline_CffFontWithoutGlyfTable_ReturnsFalse()
        {
            // Source Code Pro is a CFF/OTTO font: no `glyf` table, so the glyf decoder must decline.
            var face = Face(BundledFonts.Otf);
            Assert.Null(face.glyf);
            Assert.False(GlyphOutlineDecoder.TryGetGlyphOutline(face, 1, out var outline));
            Assert.True(outline.IsEmpty);
        }

        private static void AssertPoint(double x, double y, GlyphOutlinePoint p)
        {
            Assert.Equal(x, p.X, Eps);
            Assert.Equal(y, p.Y, Eps);
        }

        private static double MaxY(GlyphOutline outline)
        {
            double max = double.NegativeInfinity;
            foreach (GlyphContour contour in outline.Contours)
            {
                max = Math.Max(max, contour.Start.Y);
                foreach (GlyphSegment s in contour.Segments)
                {
                    max = Math.Max(max, s.End.Y);
                    if (s.IsCubic)
                        max = Math.Max(max, Math.Max(s.Control1.Y, s.Control2.Y));
                }
            }
            return max;
        }
    }
}
