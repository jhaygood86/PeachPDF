using PeachPDF.PdfSharpCore.Pdf;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An <c>overflow: hidden</c> <c>inline-block</c> clips its content to its own padding edge, so what
    /// that clip is built from decides whether the box paints its text or a blank bordered rectangle.
    /// </summary>
    /// <remarks>
    /// These assert on the page's own content stream rather than on <c>CssBox</c>/<c>CssRect</c>
    /// geometry, because this is precisely the gap CLAUDE.md's testing conventions warn about: the
    /// box's layout geometry can be entirely correct — every word inside its rectangle, to three
    /// decimal places — while the clip built from <c>CssBox.Bounds</c> lands somewhere else on the page
    /// and the rendered box comes out empty. A purely numeric test of this fixture passes either way;
    /// both PDFium and MuPDF show the difference plainly.
    /// </remarks>
    public class InlineBlockOverflowClipPaintTests
    {
        private const string Html = """
            <!DOCTYPE html>
            <html><body style='font:10pt Arial,sans-serif;margin:0'>
            <div>before <span style='display:inline-block;overflow:hidden;padding:2pt 4pt;border:1pt solid'>inside</span> after Agy</div>
            </body></html>
            """;

        [Fact]
        public async Task ClippedInlineBlock_ClipsAroundItsOwnText_NotSomewhereElseOnThePage()
        {
            var content = await PageContentAsync(Html);

            // An adjacency assertion, not a token-presence one: take the clip path the box pushes and
            // the text drawn inside it, and check the one encloses the other. The failing shape passes
            // every "is there a clip" check going - it is a well-formed rectangle with real area, just
            // 8x4pt at the page's top-left corner, being the padding edge of a border box that was
            // never positioned. Only its relationship to the text it governs tells the two apart.
            var clipped = Regex.Match(content,
                @"q\s+([-\d.]+) ([-\d.]+) m\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+h\s+W\*? n\s+BT\s+([-\d.]+) ([-\d.]+) Td");

            Assert.True(clipped.Success, "the box must push a clip path and draw its text inside it");

            double N(int group) => double.Parse(clipped.Groups[group].Value, CultureInfo.InvariantCulture);

            var left = Math.Min(N(1), N(5));
            var right = Math.Max(N(1), N(5));
            var bottom = Math.Min(N(2), N(6));
            var top = Math.Max(N(2), N(6));
            double textX = N(9), textY = N(10);

            Assert.True(textX >= left && textX <= right && textY >= bottom && textY <= top,
                $"the text is drawn at ({textX}, {textY}), outside the clip " +
                $"({left}..{right}, {bottom}..{top}) its own box pushed around it");
        }

        [Fact]
        public async Task ClippedInlineBlock_DrawsTheTextInsideIt()
        {
            var content = await PageContentAsync(Html);

            // "before", "inside", " after" and "Agy" - four runs. The box painted its border and clipped
            // its own word away entirely, leaving three.
            var shows = Regex.Matches(content, @"Tj").Count;

            Assert.Equal(4, shows);
        }

        private static async Task<string> PageContentAsync(string html)
        {
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf(html, PageSize.A4, margin: 0);

            return PageContent(document.PdfDocument);
        }

        /// <summary>
        /// The first page's content stream, read from the live object graph (the streams are only
        /// filtered when the document is saved), the same way the interactive-forms paint tests read
        /// theirs.
        /// </summary>
        private static string PageContent(PdfDocument document)
        {
            using var bytes = new MemoryStream();

            foreach (var content in document.Pages[0].Contents)
            {
                bytes.Write(content.Stream.Value, 0, content.Stream.Value.Length);
            }

            return Encoding.Latin1.GetString(bytes.ToArray());
        }
    }
}
