using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// <see cref="PdfSourceDocument"/>: PDFs the composer writes are read back page by page (compressed content, xref
    /// streams and object streams as the writer chooses them), and hand-built files exercise the classic xref table, a
    /// damaged table that forces reconstruction, and the individual stream filters.
    /// </summary>
    public class PdfSourceDocumentTests
    {
        private static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);

        /// <summary>Builds a one-page PDF with a correct classic xref table.</summary>
        private static byte[] HandBuilt(string content, bool brokenXref = false)
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            void Obj(string body) { offsets.Add(sb.Length); sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n"); }
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 200 100] /Rotate 90 >>");
            Obj("<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
            Obj($"<< /Length 5 0 R >>\nstream\n{content}\nendstream");
            Obj(content.Length.ToString());
            int xref = sb.Length;
            sb.Append("xref\n0 6\n0000000000 65535 f \n");
            foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(brokenXref ? 1 : xref).Append("\n%%EOF\n");
            return Ascii(sb.ToString());
        }

        [Fact]
        public void ClassicXrefTable_ReadsPageContentThroughAnIndirectLength()
        {
            var doc = PdfSourceDocument.Open(HandBuilt("0 0 10 10 re f"));

            var page = Assert.Single(doc.Pages);
            Assert.Equal("0 0 10 10 re f\n", Encoding.Latin1.GetString(doc.GetPageContent(page)));
        }

        [Fact]
        public void InheritedAttributes_ComeFromTheParentPagesNode()
        {
            var doc = PdfSourceDocument.Open(HandBuilt("q Q"));
            var page = doc.Pages[0];

            var box = Assert.IsType<PdfArray>(doc.InheritedAttribute(page, "MediaBox"));
            Assert.Equal(200, doc.NumberOf(box.Elements[2]));
            Assert.Equal(90, doc.IntOf(doc.InheritedAttribute(page, "Rotate")));
            Assert.Null(doc.InheritedAttribute(page, "CropBox"));
        }

        [Fact]
        public void WrongStartxref_IsRepairedByScanningForObjects()
        {
            var doc = PdfSourceDocument.Open(HandBuilt("1 0 0 rg", brokenXref: true));

            var page = Assert.Single(doc.Pages);
            Assert.Equal("1 0 0 rg\n", Encoding.Latin1.GetString(doc.GetPageContent(page)));
        }

        [Fact]
        public void NoXrefAtAll_IsRepairedFromTheObjectsAndTheCatalog()
        {
            var bytes = HandBuilt("0 g");
            var text = Encoding.Latin1.GetString(bytes);
            text = text[..text.IndexOf("xref\n", StringComparison.Ordinal)];

            var doc = PdfSourceDocument.Open(Ascii(text));

            Assert.Single(doc.Pages);
        }

        [Fact]
        public void NotAPdf_Throws()
        {
            Assert.Throws<InvalidDataException>(() => PdfSourceDocument.Open(Ascii("this is not a pdf")));
        }

        [Fact]
        public async Task ComposerOutput_RoundTripsPageCountAndSize_WithCompressionAndXrefStreams()
        {
            var context = new PdfRenderContext();
            using var composer = new PdfComposer(context);
            var one = composer.AddPage(300, 200).Canvas;
            one.DrawRectangle(one.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 10, 10, 50, 50);
            composer.AddPage(400, 100);
            using var ms = new MemoryStream();
            composer.Save(ms);
            await Task.CompletedTask;

            var doc = PdfSourceDocument.Open(ms.ToArray());

            Assert.Equal(2, doc.Pages.Count);
            var box0 = Assert.IsType<PdfArray>(doc.InheritedAttribute(doc.Pages[0], "MediaBox"));
            Assert.Equal(300, doc.NumberOf(box0.Elements[2]), 3);
            var box1 = Assert.IsType<PdfArray>(doc.InheritedAttribute(doc.Pages[1], "MediaBox"));
            Assert.Equal(400, doc.NumberOf(box1.Elements[2]), 3);
            // The first page's content has real drawing operators after decoding.
            var content = Encoding.Latin1.GetString(doc.GetPageContent(doc.Pages[0]));
            Assert.Contains(" re", content);
        }

        [Fact]
        public void Lexer_ReadsNamesStringsAndNumbers()
        {
            var lx = new PdfLexer(Ascii("/A#20B (a\\(b\\051\\101) <48656C6C6F> -3.5 +7 .5 [ ] << >> true"));

            Assert.Equal("A B", lx.Next().Text);
            Assert.Equal("a(b)A", lx.Next().Text);
            Assert.Equal("Hello", lx.Next().Text);
            Assert.Equal(-3.5, lx.Next().Real);
            Assert.Equal(7, lx.Next().Integer);
            Assert.Equal(0.5, lx.Next().Real);
            Assert.Equal(PdfTokenKind.ArrayStart, lx.Next().Kind);
            Assert.Equal(PdfTokenKind.ArrayEnd, lx.Next().Kind);
            Assert.Equal(PdfTokenKind.DictStart, lx.Next().Kind);
            Assert.Equal(PdfTokenKind.DictEnd, lx.Next().Kind);
            Assert.Equal("true", lx.Next().Text);
            Assert.Equal(PdfTokenKind.Eof, lx.Next().Kind);
        }

        [Fact]
        public void Ascii85_AsciiHex_RunLength_Decode()
        {
            Assert.Equal("Man ", Encoding.ASCII.GetString(PdfStreamFilters.Decode("ASCII85Decode", Ascii("9jqo^~>"))));
            Assert.Equal("Hi", Encoding.ASCII.GetString(PdfStreamFilters.Decode("ASCIIHexDecode", Ascii("48 69>"))));
            Assert.Equal(new byte[] { 1, 2, 3, 9, 9, 9, 9 }, PdfStreamFilters.Decode("RunLengthDecode", [2, 1, 2, 3, 253, 9, 128]));
        }

        [Fact]
        public void Lzw_DecodesTheSpecExample()
        {
            // ISO 32000-1, 7.4.4.2: 45 45 45 45 45 65 45 45 45 66.
            byte[] encoded = [0x80, 0x0B, 0x60, 0x50, 0x22, 0x0C, 0x0C, 0x85, 0x01];
            Assert.Equal(new byte[] { 45, 45, 45, 45, 45, 65, 45, 45, 45, 66 }, PdfStreamFilters.Decode("LZWDecode", encoded));
        }

        [Fact]
        public void PngUpPredictor_IsUndone()
        {
            // Two rows of 3 bytes with filter 2 (Up): the second row adds the first.
            byte[] data = [0, 1, 2, 3, 2, 1, 1, 1];
            Assert.Equal(new byte[] { 1, 2, 3, 2, 3, 4 }, PdfStreamFilters.Predict(data, 12, 1, 8, 3));
        }
    }
}
