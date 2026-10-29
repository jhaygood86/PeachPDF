using PeachDrawing;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary><see cref="PdfPageSource"/>, the public face of the importer, used the way a package consumer would.</summary>
    public class PdfPageSourceTests
    {
        private static byte[] Document(string content = "1 0 0 rg 0 0 50 50 re f") =>
            TestPdf.Build(content, pageExtras: "/Rotate 90", width: 200, height: 100);

        [Fact]
        public void Open_FromBytesAndFromAStream_ReadsThePages()
        {
            var fromBytes = PdfPageSource.Open(Document());
            using var stream = new MemoryStream(Document());
            var fromStream = PdfPageSource.Open(stream);

            Assert.Equal(1, fromBytes.PageCount);
            Assert.Equal(1, fromStream.PageCount);
            Assert.Equal(stream.Length, stream.Position);              // read to the end, not closed
            Assert.True(stream.CanRead);
        }

        [Fact]
        public void GetPageSize_IsThePageAsAViewerShowsIt()
        {
            var source = PdfPageSource.Open(Document());

            Assert.Equal((100.0, 200.0), source.GetPageSize(0));       // 200x100 turned a quarter
        }

        [Fact]
        public void CustomString_InAssociatedDictionary_RoundTrips()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(100, 100);
            page.SetCustomString("/ApplicationData", "/Employee", "æmployee-1");
            page.SetCustomString("/ApplicationData", "/Department", "R&D");
            using var stream = new MemoryStream();
            composer.Save(stream);

            var source = PdfPageSource.Open(stream.ToArray());

            Assert.True(source.TryGetCustomString(0, "/ApplicationData", "/Employee", out var employee));
            Assert.Equal("æmployee-1", employee);
            Assert.True(source.TryGetCustomString(0, "/ApplicationData", "/Department", out var department));
            Assert.Equal("R&D", department);
        }

        [Fact]
        public void TryGetCustomString_MissingOrNonStringValue_ReturnsFalse()
        {
            var missingDictionary = PdfPageSource.Open(Document());
            var missingValue = PdfPageSource.Open(
                TestPdf.Build("", pageExtras: "/ApplicationData << /Other (value) >>"));
            var nonStringValue = PdfPageSource.Open(
                TestPdf.Build("", pageExtras: "/ApplicationData << /Employee 42 >>"));

            Assert.False(missingDictionary.TryGetCustomString(
                0, "/ApplicationData", "/Employee", out var fromMissingDictionary));
            Assert.Null(fromMissingDictionary);
            Assert.False(missingValue.TryGetCustomString(
                0, "/ApplicationData", "/Employee", out var missing));
            Assert.Null(missing);
            Assert.False(nonStringValue.TryGetCustomString(
                0, "/ApplicationData", "/Employee", out var nonString));
            Assert.Null(nonString);
        }

        [Fact]
        public void Paint_DrawsOntoTheCanvas_AndReturnsWhatItCouldNotDraw()
        {
            var source = PdfPageSource.Open(Document("1 0 0 rg 0 0 50 50 re f /Sh0 sh"));
            using var canvas = new RasterRenderContext().CreateCanvas(100, 200);

            var unsupported = source.Paint(0, canvas);

            Assert.Equal(["sh (missing shading)"], unsupported);
            var pixels = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            Assert.Contains(pixels.ToArray().Where((_, i) => i % 4 == 3), a => a == 255);
        }

        [Fact]
        public void BadArguments_Throw()
        {
            var source = PdfPageSource.Open(Document());
            using var canvas = new RasterRenderContext().CreateCanvas(10, 10);

            Assert.Throws<ArgumentNullException>(() => PdfPageSource.Open((byte[])null!));
            Assert.Throws<ArgumentNullException>(() => PdfPageSource.Open((Stream)null!));
            Assert.Throws<ArgumentNullException>(() => source.Paint(0, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Paint(1, canvas));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.GetPageSize(-1));
            Assert.Throws<InvalidDataException>(() => PdfPageSource.Open([1, 2, 3]));
            Assert.Throws<ArgumentException>(() =>
                source.TryGetCustomString(0, "", "/Employee", out _));
            Assert.Throws<ArgumentException>(() =>
                source.TryGetCustomString(0, "/ApplicationData", "", out _));
        }
    }
}
