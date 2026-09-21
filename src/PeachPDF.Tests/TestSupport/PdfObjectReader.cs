using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Just enough of a PDF reader to follow indirect references through a saved file's bytes. PeachPDF's PDF
    /// core is write-only, and a test that only greps for a token proves little - these helpers let a test
    /// assert on structure instead (for example that two entries point at the very same object).
    /// </summary>
    internal static class PdfObjectReader
    {
        public static string Save(PeachPdfDocument document)
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        public static async System.Threading.Tasks.Task<string> GeneratePdf(string html, PdfGenerateConfig config) =>
            Save(await new PdfGenerator().GeneratePdf(html, config));

        public static int RootObject(string pdf) => int.Parse(Regex.Match(pdf, @"/Root\s+(\d+)\s+0\s+R").Groups[1].Value);

        /// <summary>The dictionary text of indirect object <paramref name="number"/> (everything before its stream data).</summary>
        public static string Body(string pdf, int number)
        {
            var match = Regex.Match(pdf, $@"(?:^|[\r\n]){number} 0 obj(.*?)endobj", RegexOptions.Singleline);
            Assert.True(match.Success, $"object {number} not found");

            var body = match.Groups[1].Value;
            var streamStart = body.IndexOf("stream", StringComparison.Ordinal);
            return streamStart >= 0 ? body[..streamStart] : body;
        }

        public static List<int> References(string text) =>
            Regex.Matches(text, @"(\d+)\s+0\s+R").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        /// <summary>The object numbers the catalog's <c>/AF</c> array lists, in order.</summary>
        public static List<int> AssociatedFiles(string pdf)
        {
            var catalog = Body(pdf, RootObject(pdf));
            var array = Regex.Match(catalog, @"/AF\s*\[([^\]]*)\]");
            return array.Success ? References(array.Groups[1].Value) : [];
        }

        /// <summary>The body of the first file specification in the file.</summary>
        public static int FileSpecObject(string pdf)
        {
            var match = Regex.Match(pdf, @"(?:^|[\r\n])(\d+) 0 obj(?:(?!endobj).)*?/Type\s*/Filespec", RegexOptions.Singleline);
            Assert.True(match.Success, "no /Filespec object");
            return int.Parse(match.Groups[1].Value);
        }

        public static int EmbeddedStreamObject(string pdf)
        {
            var match = Regex.Match(pdf, @"(?:^|[\r\n])(\d+) 0 obj(?:(?!endobj).)*?/Type\s*/EmbeddedFile", RegexOptions.Singleline);
            Assert.True(match.Success, "no /EmbeddedFile object");
            return int.Parse(match.Groups[1].Value);
        }

        /// <summary>The raw (still filtered) bytes of the stream of indirect object <paramref name="number"/>.</summary>
        public static byte[] StreamBytes(string pdf, int number)
        {
            var match = Regex.Match(pdf, $@"(?:^|[\r\n]){number} 0 obj(.*?)endobj", RegexOptions.Singleline);
            var body = match.Groups[1].Value;
            var length = int.Parse(Regex.Match(body, @"/Length\s+(\d+)").Groups[1].Value);
            var start = body.IndexOf("stream", StringComparison.Ordinal) + "stream".Length;
            if (body[start] == '\r') start++;
            if (body[start] == '\n') start++;

            return Encoding.Latin1.GetBytes(body.Substring(start, length));
        }

        public static byte[] Inflate(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        /// <summary>The document's XMP packet, parsed.</summary>
        public static XDocument XmpPacket(string pdf)
        {
            var match = Regex.Match(pdf, @"<x:xmpmeta.*?</x:xmpmeta>", RegexOptions.Singleline);
            Assert.True(match.Success, "No XMP packet found in generated PDF.");
            return XDocument.Parse(match.Value);
        }
    }
}
