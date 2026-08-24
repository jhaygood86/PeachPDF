using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.Text;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    // Adapted from upstream PDFsharp's Drawing/images/ImageTests.cs. Upstream loads real
    // sample JPEG/BMP/PNG files from an external, non-git asset archive; this fork has no
    // such archive, so a tiny image is synthesized in-memory instead (same approach as
    // PeachImageSourceTests.cs). Tests requiring PdfReader.Open (round-tripping a
    // saved PDF) were dropped, since this fork has no PDF reader.
    public class ImageDrawingTests : IDisposable
    {
        readonly List<string> _tempFiles = [];

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        static byte[] MakePngBytes(int width = 8, int height = 8) =>
            RasterPngFixture.MakeSolidRgbaPngBytes(width, height, 255, 0, 0);

        string WriteTempPngFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
            File.WriteAllBytes(path, MakePngBytes());
            _tempFiles.Add(path);
            return path;
        }

        [Fact]
        public void PDF_with_Images()
        {
            // Create a new PDF document.
            var document = new PdfDocument();
            document.Info.Title = "Created with PDFsharp";

            document.Options.EnableCcittCompressionForBilevelImages = true;
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;

            // Create an empty page in this document.
            var page = document.AddPage();

            // Get an XGraphics object for drawing on this page.
            var gfx = XGraphics.FromPdfPage(page);

            // Draw two lines with a red default pen.
            var width = page.Width.Point;
            var height = page.Height.Point;
            gfx.DrawLine(XPens.Red, 0, 0, width, height);
            gfx.DrawLine(XPens.Red, width, 0, 0, height);

            // Draw a circle with a red pen which is 1.5 point thick.
            var r = width / 5;
            gfx.DrawEllipse(new XPen(XColors.Red, 1.5), XBrushes.White, new XRect(width / 2 - r, height / 2 - r, 2 * r, 2 * r));

            // Create a font.
            var font = new XFont("Arial", 20, XFontStyle.BoldItalic, new FontResolver());

            // Draw the text.
            gfx.DrawString("Hello, World!", font, XBrushes.Black,
                new XRect(0, 0, width, height), XStringFormats.Center);

            var fullName = WriteTempPngFile();
            var image = XImage.FromFile(fullName);

            gfx.DrawImage(image, 100, 100, 100, 100);

            // Save the document.
            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void PDF_with_Image_from_stream()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = MakePngBytes();
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.DrawImage(xImage, 100, 100, 100, 100);

            // Save the document.
            using var stream = new MemoryStream();
            document.Save(stream);

            Assert.True(stream.Length > 0);
        }

        // --- Downscaling: embedded /Width /Height reflects the display size, not the source size ---
        // (this repo's testing convention: assert real PDF structure, not just "didn't throw" - a
        // content-stream substring match alone doesn't prove what actually got embedded.)

        private static string SaveAndReadAscii(PdfDocument document)
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            return Encoding.ASCII.GetString(stream.ToArray());
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        [Fact]
        public void DownscaleImages_ShrinksEmbeddedResolutionToDisplaySize()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 255, 0, 0);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            // 20pt display size, default MaximumDownscaleMultiplier (1.0): 20 / 0.75 = 26.67px, rounds to 27.
            gfx.DrawImage(xImage, 0, 0, 20, 20);

            var pdfText = SaveAndReadAscii(document);

            Assert.Contains("/Width 27", pdfText);
            Assert.Contains("/Height 27", pdfText);
            Assert.DoesNotContain("/Width 200", pdfText);
        }

        [Fact]
        public void DownscaleImagesDisabled_EmbedsAtNaturalResolution()
        {
            var document = new PdfDocument();
            document.Options.DownscaleImages = false;
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 255, 0, 0);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.DrawImage(xImage, 0, 0, 20, 20);

            var pdfText = SaveAndReadAscii(document);

            Assert.Contains("/Width 200", pdfText);
            Assert.Contains("/Height 200", pdfText);
        }

        [Fact]
        public void DownscaleImages_SameSourceAtTwoDisplaySizes_EmbedsTwoDistinctCopies()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 0, 255, 0);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.DrawImage(xImage, 0, 0, 20, 20);   // 20 / 0.75 = 26.67px, rounds to 27
            gfx.DrawImage(xImage, 0, 0, 40, 40);   // 40 / 0.75 = 53.33px, rounds to 53

            var pdfText = SaveAndReadAscii(document);

            Assert.Contains("/Width 27", pdfText);
            Assert.Contains("/Width 53", pdfText);
        }

        [Fact]
        public void DownscaleImages_SameSourceAtSameDisplaySizeTwice_EmbedsOnlyOnce()
        {
            // Regression guard for the common case (e.g. a logo reused unchanged across a repeating
            // header/footer): dedup by (path, target size) must still collapse repeat draws at the
            // identical size to a single embedded copy, exactly as plain path-based dedup already did
            // before downscaling existed.
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 0, 0, 255);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.DrawImage(xImage, 0, 0, 20, 20);
            gfx.DrawImage(xImage, 50, 50, 20, 20);

            var pdfText = SaveAndReadAscii(document);

            Assert.Equal(1, CountOccurrences(pdfText, "/Width 27"));
        }

        [Fact]
        public void DownscaleImages_UnderActiveTransform_AccountsForTransformScale()
        {
            // A raster image drawn while a transform (CSS `transform: scale(...)`, or an SVG viewport/
            // element transform - both apply via the same XGraphics.PushTransform/ScaleTransform
            // mechanism) is active on the graphics state ends up at destRect size * transform scale on
            // the actual page, not destRect size alone. Realize() must fold that scale in before
            // computing a downscale target, or a transformed image gets embedded too small and displays
            // visibly blurry once the transform blows it back up.
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 255, 255, 0);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.ScaleTransform(2.0, 2.0);
            // 20pt destRect under a 2x active transform -> 40pt true on-page size -> 40 / 0.75 = 53.33px, rounds to 53.
            gfx.DrawImage(xImage, 0, 0, 20, 20);

            var pdfText = SaveAndReadAscii(document);

            Assert.Contains("/Width 53", pdfText);
            Assert.DoesNotContain("/Width 27", pdfText);
        }

        [Fact]
        public void DownscaleImages_UnderNonUniformTransformScale_AccountsForEachAxisSeparately()
        {
            // A single geometric-mean-of-determinant scalar applied to both axes would be wrong here:
            // scaleX(3) alone has a determinant-derived area scale of sqrt(3) =~ 1.73, which if applied
            // to both axes would under-size the stretched (x) axis and over-size the untouched (y) axis.
            // Realize() must extract each axis's own scale from the CTM's basis vectors instead.
            var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(200, 200, 0, 255, 255);
            using var xImage = XImage.FromStream(() => new MemoryStream(bytes));

            gfx.ScaleTransform(3.0, 1.0);
            // x: 20pt * 3 = 60pt -> 60 / 0.75 = 80px. y: 20pt * 1 = 20pt -> 20 / 0.75 = 26.67px, rounds to 27.
            gfx.DrawImage(xImage, 0, 0, 20, 20);

            var pdfText = SaveAndReadAscii(document);

            Assert.Contains("/Width 80", pdfText);
            Assert.Contains("/Height 27", pdfText);
        }
    }
}
