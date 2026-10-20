using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// Text in an imported page: a PDF the composer writes with an embedded font is read back, painted onto a raster canvas
    /// (the glyphs really land) and onto a second composer (the ToUnicode map survives the round trip).
    /// </summary>
    public class PdfImportedTextTests
    {
        private static async Task<byte[]> ComposeAsync(string text, double size = 40)
        {
            var context = new PdfRenderContext();
            var name = "Imp-" + Guid.NewGuid().ToString("N");
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await context.AddFont(stream, name);
            var font = context.GetFont(name, size, PaintFontStyle.Regular)!;
            using var composer = new PdfComposer(context);
            var canvas = composer.AddPage(300, 100).Canvas;
            canvas.DrawString(text, font, PaintColor.Black, new PaintPoint(10, 20), new Size(280, 60));
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        private static int CountInked(RasterCanvas canvas)
        {
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            int inked = 0;
            for (int i = 3; i < span.Length; i += 4) if (span[i] > 0) inked++;
            return inked;
        }

        /// <summary>The ToUnicode destinations of every font on the first page, as a sorted list of strings.</summary>
        private static List<string> ToUnicodeValues(byte[] pdf)
        {
            var doc = PdfSourceDocument.Open(pdf);
            var page = doc.Pages[0];
            var fonts = doc.DictOf((PdfDictionary)doc.InheritedAttribute(page, "Resources")!, "Font")!;
            var values = new List<string>();
            foreach (var kv in fonts)
            {
                var font = (PdfDictionary)doc.Resolve(kv.Value)!;
                var map = new Dictionary<int, string>();
                if (doc.Resolve(font.GetRaw("ToUnicode")) is PdfDictionary { Stream: not null } tu)
                    PdfImportedFont.ParseToUnicode(doc.DecodeStream(tu).Data, map);
                values.AddRange(map.Values);
            }
            values.Sort(StringComparer.Ordinal);
            return values;
        }

        [Fact]
        public async Task EmbeddedFontText_IsDrawnOntoARasterCanvas()
        {
            var pdf = await ComposeAsync("HHHH");
            var doc = PdfSourceDocument.Open(pdf);

            using var canvas = new RasterRenderContext().CreateCanvas(300, 100);
            var unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);

            Assert.Empty(unsupported);
            int inked = CountInked(canvas);
            Assert.True(inked > 400, $"only {inked} pixels were inked");
        }

        [Fact]
        public async Task ToUnicode_RoundTripsThroughAnImportAndResave()
        {
            var original = await ComposeAsync("Office: fi ffl");
            var doc = PdfSourceDocument.Open(original);

            var context = new PdfRenderContext();
            using var composer = new PdfComposer(context);
            var (w, h) = PdfPagePainter.GetPageSize(doc, doc.Pages[0]);
            var unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], composer.AddPage(w, h).Canvas);
            using var ms = new MemoryStream();
            composer.Save(ms);

            Assert.Empty(unsupported);
            var before = ToUnicodeValues(original);
            Assert.NotEmpty(before);
            Assert.Equal(before, ToUnicodeValues(ms.ToArray()));
        }

        [Fact]
        public void ToUnicodeCMap_ReadsCharsRangesAndArrays()
        {
            var map = new Dictionary<int, string>();
            var cmap = "1 beginbfchar <0003> <0020> endbfchar 2 beginbfrange <0010> <0012> <0041> <0020> <0021> [<0066 0069> <00660066>] endbfrange"u8.ToArray();

            PdfImportedFont.ParseToUnicode(cmap, map);

            Assert.Equal(" ", map[3]);
            Assert.Equal("A", map[0x10]);
            Assert.Equal("C", map[0x12]);
            Assert.Equal("fi", map[0x20]);
            Assert.Equal("ff", map[0x21]);
        }
    }
}
