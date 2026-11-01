using System.Text;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Cross-reference streams, object streams and hybrid files, built byte by byte since the writer itself emits a classic table.</summary>
    public class PdfXrefStreamTests
    {
        private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

        private static byte[] Bytes(string text) => Encoding.Latin1.GetBytes(text);

        private static string Content(PdfSourceDocument doc) => Text(doc.GetPageContent(doc.Pages[0])).Trim();

        /// <summary>
        /// A PDF 1.5 file: objects 1-3 (catalog, page tree, page) live in object stream 5, the content is object 4, and a cross-reference
        /// stream (object 6) locates them all.
        /// </summary>
        private static byte[] XrefStreamFile(bool withIndex = false)
        {
            var file = new MemoryStream();

            void Write(string text) => file.Write(Bytes(text));

            Write("%PDF-1.5\n");
            var content4 = (int)file.Length;
            Write("4 0 obj\n<< /Length 8 >>\nstream\n0 0 1 rg\nendstream\nendobj\n");

            const string b1 = "<< /Type /Catalog /Pages 2 0 R >>";
            const string b2 = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
            const string b3 = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>";
            var header = $"1 0 2 {b1.Length + 1} 3 {b1.Length + b2.Length + 2} ";
            var body = header + b1 + "\n" + b2 + "\n" + b3;
            var objectStream = (int)file.Length;
            Write($"5 0 obj\n<< /Type /ObjStm /N 3 /First {header.Length} /Length {body.Length} >>\nstream\n{body}\nendstream\nendobj\n");

            var xrefOffset = (int)file.Length;
            var rows = new List<byte>();

            void Row(int type, int field2, int field3)
            {
                rows.Add((byte)type);
                rows.Add((byte)(field2 >> 8));
                rows.Add((byte)field2);
                rows.Add((byte)field3);
            }

            Row(0, 0, 255);
            Row(2, 5, 0);
            Row(2, 5, 1);
            Row(2, 5, 2);
            Row(1, content4, 0);
            Row(1, objectStream, 0);
            Row(1, xrefOffset, 0);
            var data = Encoding.Latin1.GetString(rows.ToArray());
            var index = withIndex ? "/Index [0 7]" : "";
            Write($"6 0 obj\n<< /Type /XRef /Size 7 /W [1 2 1] {index} /Root 1 0 R /Length {data.Length} >>\nstream\n{data}\nendstream\nendobj\n");
            Write($"startxref\n{xrefOffset}\n%%EOF\n");
            return file.ToArray();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CrossReferenceStream_WithAnObjectStream_IsRead(bool withIndex)
        {
            var doc = PdfSourceDocument.Open(XrefStreamFile(withIndex));

            Assert.Single(doc.Pages);
            Assert.Equal("0 0 1 rg", Content(doc));
        }

        [Fact]
        public void WhenTheCrossReferenceStreamIsNotWhereStartxrefSays_TheFileIsRebuiltIncludingObjectStreams()
        {
            var text = Text(XrefStreamFile());
            var damaged = text[..text.LastIndexOf("startxref", StringComparison.Ordinal)] + "startxref\n12\n%%EOF\n";

            var doc = PdfSourceDocument.Open(Bytes(damaged));

            Assert.Equal("0 0 1 rg", Content(doc));
        }

        [Fact]
        public void HybridFile_ReadsTheStreamNamedByXRefStm()
        {
            // A classic table lists only the content object; /XRefStm points at the stream that lists the rest.
            var text = Text(XrefStreamFile());
            var xrefStreamOffset = text.IndexOf("6 0 obj", StringComparison.Ordinal);
            var contentOffset = text.IndexOf("4 0 obj", StringComparison.Ordinal);
            var classic = new StringBuilder(text[..text.LastIndexOf("startxref", StringComparison.Ordinal)]);
            var tableOffset = classic.Length;
            classic.Append($"xref\n4 1\n{contentOffset:D10} 00000 n \ntrailer\n<< /Size 7 /Root 1 0 R /XRefStm {xrefStreamOffset} >>\nstartxref\n{tableOffset}\n%%EOF\n");

            var doc = PdfSourceDocument.Open(Bytes(classic.ToString()));

            Assert.Equal("0 0 1 rg", Content(doc));
        }
    }
}
