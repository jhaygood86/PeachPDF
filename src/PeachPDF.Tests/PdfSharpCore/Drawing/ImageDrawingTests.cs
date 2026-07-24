using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using StbImageWriteSharp;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    // Adapted from upstream PDFsharp's Drawing/images/ImageTests.cs. Upstream loads real
    // sample JPEG/BMP/PNG files from an external, non-git asset archive; this fork has no
    // such archive, so a tiny image is synthesized in-memory instead (same approach as
    // StbImageSharpImageSourceTests.cs). Tests requiring PdfReader.Open (round-tripping a
    // saved PDF) were dropped, since this fork has no PDF reader.
    public class ImageDrawingTests : IDisposable
    {
        readonly List<string> _tempFiles = [];

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        static byte[] MakePngBytes(int width = 8, int height = 8)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
            }
            using var ms = new MemoryStream();
            new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
        }

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
    }
}
