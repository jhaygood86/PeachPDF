using PeachDrawing;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Tiling patterns (PatternType 1) painted onto a raster canvas: where the cells fall, coloured and uncoloured content, matrices, steps and strokes.</summary>
    public class PdfTilingPatternTests
    {
        private const string TwoSquares = "1 0 0 rg 0 0 10 10 re f 0 0 1 rg 10 10 10 10 re f";

        private static string Pattern(string content, string entries = "/PaintType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20", string resources = "") =>
            TestPdf.Stream($"/Type /Pattern /PatternType 1 /TilingType 1 {entries} /Resources << {resources} >>", content);

        private static byte[] Page(string content, string pattern, string extraResources = "", params string[] more) =>
            TestPdf.Build(content, resources: $"/Pattern << /P0 5 0 R >> {extraResources}", extraObjects: [pattern, .. more]);

        /// <summary>The colour at a point given in PDF space (y up) on a 100x100 page.</summary>
        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int pdfX, int pdfY, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = ((99 - pdfY) * 100 + pdfX) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static void IsRed((byte R, byte G, byte B, byte A) p) => Assert.True(p.R > 200 && p.G < 60 && p.B < 60 && p.A > 200, $"expected red, was {p}");

        private static void IsBlue((byte R, byte G, byte B, byte A) p) => Assert.True(p.B > 200 && p.R < 60 && p.G < 60 && p.A > 200, $"expected blue, was {p}");

        private static void IsEmpty((byte R, byte G, byte B, byte A) p) => Assert.True(p.A < 30, $"expected nothing, was {p}");

        [Fact]
        public void ColouredPattern_RepeatsItsCellAcrossTheShape()
        {
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern(TwoSquares));

            IsRed(At(pdf, 5, 5, out var unsupported));            // lower-left of the first cell
            Assert.Empty(unsupported);
            IsBlue(At(pdf, 15, 15, out _));                       // upper-right of the first cell
            IsEmpty(At(pdf, 15, 5, out _));                       // the empty quadrants
            IsEmpty(At(pdf, 5, 15, out _));
            IsRed(At(pdf, 25, 5, out _));                         // the next cell to the right
            IsBlue(At(pdf, 35, 15, out _));
            IsRed(At(pdf, 5, 45, out _));                         // two cells up
            IsRed(At(pdf, 85, 85, out _));                        // and far across: 85 = 4 * 20 + 5
            IsBlue(At(pdf, 95, 95, out _));
        }

        [Fact]
        public void PatternMatrix_ScalesAndShiftsTheGrid()
        {
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern(TwoSquares, "/PaintType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 /Matrix [2 0 0 2 10 0]"));

            IsRed(At(pdf, 15, 5, out _));                         // the red square is now 20x20 and starts at x = 10
            IsRed(At(pdf, 25, 15, out _));
            IsBlue(At(pdf, 45, 25, out _));                       // blue: x 30..50, y 20..40
            IsEmpty(At(pdf, 5, 5, out _));                        // left of the shifted grid's first cell
        }

        [Fact]
        public void PatternStaysInItsOwnSpace_WhateverTheCurrentTransformIs()
        {
            // Filled under a 0.5 scale, but the pattern's space is the page's: the cells are still 20pt on the page.
            var pdf = Page("0.5 0 0 0.5 0 0 cm /Pattern cs /P0 scn 0 0 200 200 re f", Pattern(TwoSquares));

            IsRed(At(pdf, 5, 5, out _));
            IsBlue(At(pdf, 15, 15, out _));
            IsRed(At(pdf, 25, 5, out _));
        }

        [Fact]
        public void StepsLargerThanTheBox_LeaveGapsBetweenCells()
        {
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern("1 0 0 rg 0 0 10 10 re f", "/PaintType 1 /BBox [0 0 10 10] /XStep 30 /YStep 30"));

            IsRed(At(pdf, 5, 5, out _));
            IsEmpty(At(pdf, 20, 5, out _));                       // in the gap
            IsRed(At(pdf, 35, 5, out _));
            IsEmpty(At(pdf, 5, 20, out _));
            IsRed(At(pdf, 65, 65, out _));
        }

        [Fact]
        public void BoundingBoxNotAtTheOrigin_PlacesTheCellWhereTheBoxIs()
        {
            // A cell whose box is x 5..25, y 5..25, steps of 20: its content is a square at the box's lower-left corner.
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern("0 0 1 rg 5 5 10 10 re f", "/PaintType 1 /BBox [5 5 25 25] /XStep 20 /YStep 20"));

            IsBlue(At(pdf, 10, 10, out _));
            IsBlue(At(pdf, 30, 10, out _));
            IsEmpty(At(pdf, 20, 10, out _));
        }

        [Fact]
        public void UncolouredPattern_TakesItsColourFromTheOperator_AndIgnoresItsOwnColourOperators()
        {
            var pdf = Page("/CS0 cs 0 1 0 /P0 scn 0 0 100 100 re f", Pattern("1 0 0 rg 0 0 10 10 re f", "/PaintType 2 /BBox [0 0 20 20] /XStep 20 /YStep 20"),
                "/ColorSpace << /CS0 [/Pattern /DeviceRGB] >>");

            var p = At(pdf, 5, 5, out var unsupported);
            Assert.Empty(unsupported);
            Assert.True(p.G > 200 && p.R < 60 && p.A > 200, $"green, not the pattern's red: {p}");
            IsEmpty(At(pdf, 15, 5, out _));
        }

        [Fact]
        public void TwoUncolouredFills_WithDifferentColours_GetTheirOwnTiles()
        {
            var pdf = Page("/CS0 cs 0 1 0 /P0 scn 0 0 50 100 re f 1 0 0 /P0 scn 50 0 50 100 re f", Pattern("0 0 10 10 re f", "/PaintType 2 /BBox [0 0 20 20] /XStep 20 /YStep 20"),
                "/ColorSpace << /CS0 [/Pattern /DeviceRGB] >>");

            Assert.True(At(pdf, 5, 5, out _).G > 200);
            IsRed(At(pdf, 65, 5, out _));
        }

        [Fact]
        public void PatternUsedForAStroke_PaintsTheLine()
        {
            var pdf = Page("/Pattern CS /P0 SCN 20 w 0 50 m 100 50 l S", Pattern(TwoSquares));

            IsRed(At(pdf, 5, 45, out _));                         // the stroke covers y 40..60: the red square of the cell at y 40..50
            IsBlue(At(pdf, 15, 55, out _));
            IsEmpty(At(pdf, 5, 80, out _));
        }

        [Fact]
        public void PatternFilledText_IsNotNeededHere_ButANonRectanglePathIsClippedToTheShape()
        {
            // A triangle: only the part of the cells inside it is painted.
            var pdf = Page("/Pattern cs /P0 scn 0 0 m 100 0 l 0 100 l f", Pattern("1 0 0 rg 0 0 20 20 re f"));

            IsRed(At(pdf, 5, 5, out _));
            IsEmpty(At(pdf, 90, 90, out _));                      // outside the triangle
        }

        [Theory]
        [InlineData("/PaintType 1 /XStep 20 /YStep 20", "pattern without a bounding box")]
        [InlineData("/PaintType 1 /BBox [0 0 20 20] /XStep 0 /YStep 20", "pattern cell")]
        [InlineData("/PaintType 1 /BBox [0 0 0 20] /XStep 20 /YStep 20", "pattern cell")]
        public void PatternsThatCannotBeTiled_AreReported(string entries, string expected)
        {
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern("0 g 0 0 5 5 re f", entries));

            At(pdf, 0, 0, out var unsupported);

            Assert.Contains(expected, unsupported);
        }

        [Fact]
        public void UncolouredPatternWithoutAColour_AndAPatternWithoutContent_AreReported()
        {
            var uncoloured = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern("0 0 5 5 re f", "/PaintType 2 /BBox [0 0 20 20] /XStep 20 /YStep 20"));
            At(uncoloured, 0, 0, out var first);
            Assert.Contains("uncoloured pattern without a colour", first);

            var noContent = Page("/Pattern cs /P0 scn 0 0 100 100 re f", "<< /Type /Pattern /PatternType 1 /PaintType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 >>");
            At(noContent, 0, 0, out var second);
            Assert.Contains("pattern without content", second);
        }

        [Fact]
        public void APatternThatFillsWithItself_DoesNotRecurseWithoutEnd()
        {
            var pdf = Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern("/Pattern cs /P0 scn 0 0 20 20 re f", resources: "/Pattern << /P0 5 0 R >>"));

            At(pdf, 5, 5, out var unsupported);                   // it returns at all: the nesting limit stops it

            Assert.Contains("pattern cell", unsupported);
        }

        [Fact]
        public void OnAPdfCanvas_TheCellIsKeptAsOneTilingPattern()
        {
            var source = PdfPageSource.Open(Page("/Pattern cs /P0 scn 0 0 100 100 re f", Pattern(TwoSquares)));
            using var composer = new PdfComposer(new PdfRenderContext());
            var (w, h) = source.GetPageSize(0);
            var unsupported = source.Paint(0, composer.AddPage(w, h).Canvas);
            using var ms = new MemoryStream();
            composer.Save(ms);

            Assert.Empty(unsupported);
            var doc = PdfSourceDocument.Open(ms.ToArray());
            var resources = (PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!;
            var patterns = doc.DictOf(resources, "Pattern")!;
            var types = new List<int>();
            foreach (var kv in patterns)
                if (doc.Resolve(kv.Value) is PdfDictionary p) types.Add(doc.IntOf(p.GetRaw("PatternType")));
            Assert.Contains(1, types);
        }
    }
}
