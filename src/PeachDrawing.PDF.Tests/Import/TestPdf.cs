using System.Text;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Builds small hand-written PDFs for the importer tests: a one-page file whose extra objects are numbered from 5.</summary>
    internal static class TestPdf
    {
        /// <summary>Bytes held as chars 0-255, so binary stream data can sit inside the text a test writes.</summary>
        public static string Binary(byte[] data) => Encoding.Latin1.GetString(data);

        /// <summary>A stream object: its dictionary entries, then the data (chars 0-255).</summary>
        public static string Stream(string dictEntries, string data) =>
            $"<< {dictEntries} /Length {data.Length} >>\nstream\n{data}\nendstream";

        /// <summary>
        /// A PDF whose page 1 has <paramref name="content"/>; <paramref name="extraObjects"/> become objects 5, 6, 7...
        /// (<paramref name="pageExtras"/> and <paramref name="resources"/> may refer to them).
        /// </summary>
        public static byte[] Build(string content, string resources = "", string pageExtras = "", int width = 100, int height = 100, params string[] extraObjects)
        {
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            void Obj(string body)
            {
                offsets.Add(sb.Length);
                sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
            }

            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents 4 0 R /Resources << {resources} >> {pageExtras} >>");
            Obj(Stream("", content));
            foreach (var o in extraObjects) Obj(o);

            var xref = sb.Length;
            sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            return Encoding.Latin1.GetBytes(sb.ToString());
        }
    }
}
