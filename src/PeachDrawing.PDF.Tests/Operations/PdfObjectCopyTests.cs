using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests.Operations
{
    /// <summary><see cref="PdfCopyMode.ObjectCopy"/>: the objects of a page reach the output as they were, not drawn again.</summary>
    public class PdfObjectCopyTests
    {
        private const string RedSquare = "1 0 0 rg 10 10 40 40 re f";

        private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

        private static int Count(string text, string needle)
        {
            var count = 0;
            for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + 1, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int page, int x, int y)
        {
            var source = PdfPageSource.Open(pdf);
            var (w, h) = source.GetPageSize(page);
            using var canvas = new RasterRenderContext().CreateCanvas((int)Math.Ceiling(w), (int)Math.Ceiling(h));
            source.Paint(page, canvas);
            var buffer = canvas.ToPixelBuffer();
            var i = (y * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static byte[] Copy(PdfOperation operation) => operation.WithCopyMode(PdfCopyMode.ObjectCopy).ToArray();

        [Fact]
        public void ContentStream_ReachesTheOutputByteForByte()
        {
            var result = Copy(PdfOperation.LoadBytes(TestPdf.Build(RedSquare)));

            // Drawn again, the square would be written as a path of the new file's own making.
            Assert.Contains(RedSquare, Text(result));
            var (r, g, b, a) = At(result, 0, 30, 70);
            Assert.True(r > 200 && g < 50 && b < 50 && a > 200);
        }

        [Fact]
        public void SharedResource_IsCopiedOnce()
        {
            var shared = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 20 20]", "0 0 1 rg 0 0 20 20 re f % marker-of-the-shared-form");
            var one = TestPdf.Build("/F Do", "/XObject << /F 5 0 R >>", extraObjects: shared);

            var result = Copy(PdfOperation.LoadBytes(one).MergeBytes(one).MergeBytes(one));

            // Three pages, and each file has its own form (they are separate files), but the page taken from one file twice shares it.
            Assert.Equal(3, PdfPageSource.Open(result).PageCount);
            Assert.Equal(3, Count(Text(result), "marker-of-the-shared-form"));

            var twice = Copy(PdfOperation.LoadBytes(one).TakePages("1,1"));
            Assert.Equal(2, PdfPageSource.Open(twice).PageCount);
            Assert.Equal(1, Count(Text(twice), "marker-of-the-shared-form"));
        }

        [Fact]
        public void Merge_KeepsEachPagesContentAndSize()
        {
            var a = TestPdf.Build(RedSquare, width: 100, height: 100);
            var b = TestPdf.Build("0 1 0 rg 0 0 50 50 re f", width: 200, height: 150);

            var result = Copy(PdfOperation.LoadBytes(a).MergeBytes(b));

            var source = PdfPageSource.Open(result);
            Assert.Equal(2, source.PageCount);
            Assert.Equal((200.0, 150.0), source.GetPageSize(1));
            Assert.True(At(result, 1, 20, 130).G > 200);
        }

        [Fact]
        public void InheritedAttributes_AreResolvedOntoThePage()
        {
            // The page itself names no media box or resources; they come from the page tree node.
            var pdf = Encoding.Latin1.GetBytes(Text(TestPdf.Build(RedSquare, "")).Replace(
                "/Type /Pages /Kids [3 0 R] /Count 1", "/Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 120 80] /Rotate 0")
                .Replace("/MediaBox [0 0 100 100]", ""));

            var result = Copy(PdfOperation.LoadBytes(pdf));

            Assert.Equal((120.0, 80.0), PdfPageSource.Open(result).GetPageSize(0));
        }

        [Fact]
        public void Rotation_IsKept()
        {
            var pdf = TestPdf.Build(RedSquare, pageExtras: "/Rotate 90", width: 100, height: 60);

            var result = Copy(PdfOperation.LoadBytes(pdf));

            Assert.Equal((60.0, 100.0), PdfPageSource.Open(result).GetPageSize(0));
        }

        [Fact]
        public void Links_LeadToTheNewPages_AndAreDroppedWhenTheirTargetIsGone()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            for (var i = 0; i < 3; i++)
                composer.AddPage(100, 100);
            composer.AddNamedDestination("end", 2, 10);
            composer.Pages[0].AddDestinationLink(new Rect(10, 10, 20, 20), "end");
            composer.Pages[0].AddLink(new Rect(40, 40, 20, 20), "https://example.com/");
            using var stream = new MemoryStream();
            composer.Save(stream);
            var source = stream.ToArray();

            var kept = PdfPageSource.Open(Copy(PdfOperation.LoadBytes(source)));
            Assert.Equal(3, kept.PageCount);
            Assert.Contains(kept.GetLinks(0), l => l.Uri == "https://example.com/");
            Assert.Contains(kept.GetLinks(0), l => l.Destination?.PageIndex == 2);

            var trimmed = PdfPageSource.Open(Copy(PdfOperation.LoadBytes(source).TakePages("1-2")));
            Assert.Contains(trimmed.GetLinks(0), l => l.Uri == "https://example.com/");
            Assert.DoesNotContain(trimmed.GetLinks(0), l => l.Destination is not null);
        }

        [Fact]
        public void Outline_IsCarriedToTheNewPages()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            composer.AddPage(100, 100);
            composer.AddPage(100, 100);
            composer.AddBookmarks([new PdfBookmark(1, "Chapter", true) { PageIndex = 1 }]);
            using var stream = new MemoryStream();
            composer.Save(stream);

            var result = Copy(PdfOperation.LoadBytes(stream.ToArray()).MergeBytes(stream.ToArray()));

            var outline = PdfPageSource.Open(result).GetOutline();
            Assert.Equal([1, 3], outline.Select(o => o.PageIndex!.Value));
        }

        private const string Widget =
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Hello) /Rect [10 10 90 30] /DA (/Helv 12 Tf 0 g) /P 3 0 R >>";

        private static byte[] WithForm() =>
            Encoding.Latin1.GetBytes(Text(TestPdf.Build("0 g", pageExtras: "/Annots [5 0 R]", extraObjects: Widget))
                .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] /DA (/Helv 0 Tf 0 g) >>"));

        [Fact]
        public void FormFields_AreCopiedAndRenamedWhenTheyClash()
        {
            var form = WithForm();

            var result = Copy(PdfOperation.LoadBytes(form).MergeBytes(form));

            var source = PdfPageSource.Open(result);
            var first = Assert.Single(source.GetFormFields(0));
            var second = Assert.Single(source.GetFormFields(1));
            Assert.Equal("name", first.Name);
            Assert.Equal("name_2", second.Name);
            Assert.Equal("Hello", second.Value);
        }

        [Fact]
        public void PageTakenTwice_HasItsFormFieldOnce()
        {
            var result = Copy(PdfOperation.LoadBytes(WithForm()).TakePages("1,1"));

            var source = PdfPageSource.Open(result);
            Assert.Single(source.GetFormFields(0));
            Assert.Empty(source.GetFormFields(1));
        }

        [Fact]
        public void ScriptsAndLaunchActions_AreNotCarried()
        {
            var pdf = TestPdf.Build("0 g", pageExtras: "/Annots [5 0 R]", extraObjects:
                "<< /Type /Annot /Subtype /Link /Rect [0 0 50 50] /A << /S /Launch /F (evil.exe) >> >>");

            var result = Copy(PdfOperation.LoadBytes(pdf));

            Assert.DoesNotContain("evil.exe", Text(result));
        }

        [Fact]
        public void Overlay_IsDrawnOverTheCopiedContent_AndUnderlayUnderIt()
        {
            var page = TestPdf.Build(RedSquare);
            var layer = TestPdf.Build("0 0 1 rg 0 0 100 100 re f");

            var over = Copy(PdfOperation.LoadBytes(page).OverlayFile(new PdfLayerConfiguration { Data = layer }));
            var under = Copy(PdfOperation.LoadBytes(page).UnderlayFile(new PdfLayerConfiguration { Data = layer }));

            Assert.Contains(RedSquare, Text(over));
            var overlaid = At(over, 0, 30, 70);
            Assert.True(overlaid.B > 200 && overlaid.R < 50, $"the blue layer is on top: {overlaid}");
            var underlaid = At(under, 0, 30, 70);
            Assert.True(underlaid.R > 200 && underlaid.B < 50, $"the red square is on top: {underlaid}");
            Assert.True(At(under, 0, 80, 20).B > 200, "the layer shows where the page draws nothing");
        }

        [Fact]
        public void Overlay_RefusesAPageItCannotPlaceTheLayerOn()
        {
            var rotated = TestPdf.Build(RedSquare, pageExtras: "/Rotate 90");
            var operation = PdfOperation.LoadBytes(rotated).OverlayFile(new PdfLayerConfiguration { Data = TestPdf.Build("0 g") });

            Assert.Throws<NotSupportedException>(() => Copy(operation));

            var redrawn = operation.WithCopyMode(PdfCopyMode.Redraw).ToArray();
            Assert.Equal(1, PdfPageSource.Open(redrawn).PageCount);
        }

        [Fact]
        public void Encrypt_AppliesToCopiedPages()
        {
            var result = Copy(PdfOperation.LoadBytes(TestPdf.Build(RedSquare)).Encrypt(new PdfEncryptionOptions { UserPassword = "pw", OwnerPassword = "ow" }));

            Assert.DoesNotContain(RedSquare, Text(result));
            Assert.Throws<PdfPasswordException>(() => PdfPageSource.Open(result));
            Assert.Equal(1, PdfPageSource.Open(result, "pw").PageCount);
        }

        [Fact]
        public void EncryptedSource_IsCopiedDecrypted()
        {
            var encrypted = PdfOperation.LoadBytes(TestPdf.Build(RedSquare)).Encrypt(new PdfEncryptionOptions { UserPassword = "pw", OwnerPassword = "ow" }).ToArray();

            var result = Copy(PdfOperation.LoadBytes(encrypted, "pw"));

            Assert.Equal(1, PdfPageSource.Open(result).PageCount);
        }

        [Fact]
        public void WithCopyMode_RefusesAnUnknownMode() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfOperation.LoadBytes(TestPdf.Build("0 g")).WithCopyMode((PdfCopyMode)99));
    }
}
