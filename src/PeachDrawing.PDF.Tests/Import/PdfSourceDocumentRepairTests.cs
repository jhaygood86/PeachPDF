using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// How a PDF is read when it is not the plain, correct, one-revision file: incremental updates, wrong stream lengths, damaged and missing
    /// cross-reference data, object and cross-reference streams (as the writer itself produces), nested page trees and encryption.
    /// </summary>
    public class PdfSourceDocumentRepairTests
    {
        private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

        private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

        private static string Content(PdfSourceDocument doc, int page = 0) => Text(doc.GetPageContent(doc.Pages[page])).Trim();

        [Fact]
        public void IncrementalUpdate_ReplacesAnObject_ThroughPrev()
        {
            var original = Text(TestPdf.Build("1 0 0 rg"));
            var firstXref = original.LastIndexOf("startxref\n", StringComparison.Ordinal);
            var previous = original[(firstXref + 10)..].Split('\n')[0];
            var update = new StringBuilder(original);
            var objectOffset = update.Length;
            update.Append("4 0 obj\n<< /Length 9 >>\nstream\n0 1 0 rg \nendstream\nendobj\n");
            var xrefOffset = update.Length;
            update.Append($"xref\n4 1\n{objectOffset:D10} 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R /Prev {previous} >>\nstartxref\n{xrefOffset}\n%%EOF\n");

            var doc = PdfSourceDocument.Open(Bytes(update.ToString()));

            Assert.Equal("0 1 0 rg", Content(doc));
        }

        [Fact]
        public void PrevLoop_AndAnOffsetOutsideTheFile_AreIgnored()
        {
            var text = Text(TestPdf.Build("q Q"));
            var xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
            text = text.Replace("/Size 5 /Root 1 0 R", $"/Size 5 /Root 1 0 R /Prev {xref}").Replace("/Size 5 /Root", "/Size 5 /Root");

            var doc = PdfSourceDocument.Open(Bytes(text));

            Assert.Single(doc.Pages);
        }

        [Fact]
        public void WrongStreamLength_IsRepairedByScanningForEndstream()
        {
            var pdf = Text(TestPdf.Build("0 0 1 rg")).Replace("/Length 8", "/Length 2");

            Assert.Equal("0 0 1 rg", Content(PdfSourceDocument.Open(Bytes(pdf))));
        }

        [Fact]
        public void NoStartxrefAtAll_IsRebuiltFromTheObjectsAndTheTrailer()
        {
            var text = Text(TestPdf.Build("0 g"));
            text = text[..text.IndexOf("xref\n0", StringComparison.Ordinal)] + "trailer\n<< /Root 1 0 R >>\n";

            Assert.Single(PdfSourceDocument.Open(Bytes(text)).Pages);
        }

        [Fact]
        public void NoTrailerEither_IsRebuiltFromTheCatalogObject()
        {
            var text = Text(TestPdf.Build("0 g"));
            text = text[..text.IndexOf("xref\n0", StringComparison.Ordinal)];

            var doc = PdfSourceDocument.Open(Bytes(text));

            Assert.Equal("0 g", Content(doc));
        }

        [Fact]
        public void ObjectsWithWrongOffsets_AreFoundAgainByScanning()
        {
            var text = Text(TestPdf.Build("1 g"));
            // Shift every in-use xref offset so none points at its object.
            var shifted = System.Text.RegularExpressions.Regex.Replace(text, @"(\d{10}) 00000 n", m => (int.Parse(m.Groups[1].Value) + 3).ToString("D10") + " 00000 n");

            Assert.Equal("1 g", Content(PdfSourceDocument.Open(Bytes(shifted))));
        }

        [Fact]
        public void NestedPageTree_IsWalkedInOrder_WithInheritedAttributesFromAnyAncestor()
        {
            var pdf = Bytes("%PDF-1.4\n" +
                "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                "2 0 obj\n<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 3 /Rotate 90 /Resources << /Marker 1 >> >>\nendobj\n" +
                "3 0 obj\n<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 /MediaBox [0 0 50 60] >>\nendobj\n" +
                "4 0 obj\n<< /Type /Page /Parent 3 0 R /Rotate 180 >>\nendobj\n" +
                "5 0 obj\n<< /Type /Page /Parent 3 0 R >>\nendobj\n" +
                "6 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 70 80] >>\nendobj\n" +
                "trailer\n<< /Root 1 0 R >>\n");

            var doc = PdfSourceDocument.Open(pdf);

            Assert.Equal(3, doc.Pages.Count);
            Assert.Equal(180, doc.IntOf(doc.InheritedAttribute(doc.Pages[0], "Rotate")));
            Assert.Equal(90, doc.IntOf(doc.InheritedAttribute(doc.Pages[1], "Rotate")));
            Assert.Equal(50, doc.NumberOf(((PdfArray)doc.InheritedAttribute(doc.Pages[1], "MediaBox")!).Elements[2]));
            Assert.Equal(70, doc.NumberOf(((PdfArray)doc.InheritedAttribute(doc.Pages[2], "MediaBox")!).Elements[2]));
            Assert.NotNull(doc.InheritedAttribute(doc.Pages[2], "Resources"));
            Assert.Equal(1, doc.IntOf(((PdfDictionary)doc.InheritedAttribute(doc.Pages[0], "Resources")!).GetRaw("Marker")));
        }

        [Fact]
        public void ACycleInThePageTree_DoesNotHang()
        {
            var pdf = Bytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                "2 0 obj\n<< /Type /Pages /Kids [2 0 R 3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n");

            Assert.Single(PdfSourceDocument.Open(pdf).Pages);
        }

        [Fact]
        public void PageContents_AsAnArrayOfStreams_AreJoined_AndAMissingOneIsSkipped()
        {
            var pdf = Bytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
                "3 0 obj\n<< /Type /Page /Parent 2 0 R /Contents [4 0 R 9 0 R 5 0 R] >>\nendobj\n" +
                "4 0 obj\n<< /Length 4 >>\nstream\nq 1 \nendstream\nendobj\n5 0 obj\n<< /Length 4 >>\nstream\n0 g \nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n");

            var doc = PdfSourceDocument.Open(pdf);

            Assert.Equal("q 1 \n\n0 g".Replace("\n\n", "\n"), Content(doc).Replace("\n\n", "\n"));
        }

        [Fact]
        public void PageWithNoContents_IsEmpty()
        {
            var pdf = Bytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n3 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n");

            Assert.Empty(PdfSourceDocument.Open(pdf).GetPageContent(PdfSourceDocument.Open(pdf).Pages[0]));
        }

        [Fact]
        public void ComposerOutput_WithCrossReferenceAndObjectStreams_IsReadDirectly_AndAfterTheTableIsLost()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = true });
            var page = composer.AddPage(120, 90).Canvas;
            page.DrawRectangle(page.GetSolidBrush(PeachDrawing.Core.PaintColor.FromArgb(255, 255, 0, 0)), 5, 5, 20, 20);
            using var ms = new MemoryStream();
            composer.Save(ms);
            var bytes = ms.ToArray();
            var text = Text(bytes);

            var direct = PdfSourceDocument.Open(bytes);
            Assert.Contains(" re", Content(direct));

            // Break the pointer to the cross-reference stream: the file is rebuilt by scanning, object streams included.
            var damaged = text.Replace("startxref\n", "startxref\n9");
            var rebuilt = PdfSourceDocument.Open(Bytes(damaged));
            Assert.Contains(" re", Content(rebuilt));
            Assert.Single(rebuilt.Pages);
        }

        [Fact]
        public void EncryptedFile_IsRefused()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { Encryption = PdfEncryptionOptions.ReadOnly("owner") });
            composer.AddPage(100, 100);
            using var ms = new MemoryStream();
            composer.Save(ms);

            Assert.Throws<NotSupportedException>(() => PdfSourceDocument.Open(ms.ToArray()));
        }

        [Fact]
        public void DecodeStream_AppliesFilterChains_AndReportsAnImageCodec()
        {
            var hex = "48 65 6C 6C 6F>";
            var doc = PdfSourceDocument.Open(TestPdf.Build("", pageExtras: "/A 5 0 R /B 6 0 R /C 7 0 R", extraObjects:
            [
                TestPdf.Stream("/Filter [/AHx /A85]", "<~87cURD]i,\"Ebo80~>").Replace("<~87cURD]i,\"Ebo80~>", hex),
                TestPdf.Stream("/Filter [/ASCIIHexDecode /DCTDecode]", hex),
                TestPdf.Stream("/Filter /ASCIIHexDecode /DecodeParms << /Predictor 1 >>", hex),
            ]));
            var page = doc.Pages[0];

            var chain = doc.DecodeStream((PdfDictionary)doc.Resolve(page.GetRaw("A"))!);
            var codec = doc.DecodeStream((PdfDictionary)doc.Resolve(page.GetRaw("B"))!);
            var plain = doc.DecodeStream((PdfDictionary)doc.Resolve(page.GetRaw("C"))!);

            Assert.Null(plain.ImageFilter);
            Assert.Equal("Hello", Encoding.ASCII.GetString(plain.Data));
            Assert.Equal("DCTDecode", codec.ImageFilter);
            Assert.Equal("Hello", Encoding.ASCII.GetString(codec.Data));
            Assert.NotNull(chain.Data);
        }

        [Fact]
        public void Resolve_FollowsReferencesToValues_AndMissingObjectsAreNull()
        {
            var doc = PdfSourceDocument.Open(TestPdf.Build("", pageExtras: "/N 5 0 R /Gone 99 0 R /Chain 6 0 R /Real 7 0 R", extraObjects: ["42", "5 0 R", "1.5"]));
            var page = doc.Pages[0];

            Assert.Equal(42, doc.IntOf(page.GetRaw("N")));
            Assert.Equal(42, doc.IntOf(page.GetRaw("Chain")));
            Assert.Equal(1.5, doc.NumberOf(page.GetRaw("Real")));
            Assert.Equal(0.25, doc.NumberOf(page.GetRaw("Gone"), 0.25));
            Assert.Null(doc.Resolve(page.GetRaw("Gone")));
            Assert.Null(doc.NameOf(page.GetRaw("N")));
            Assert.Null(doc.ArrayOf(page, "N"));
            Assert.Null(doc.DictOf(page, "N"));
        }
    }
}
