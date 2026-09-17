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

        /// <summary>
        /// A declared <c>height</c> taller than the box's one line of text must grow the clip along with
        /// the painted box (issue #1101) - not just the background/border. <c>FragmentEmitter.
        /// ClipSourceBoundsOf</c> unions the same per-line <c>CssBox.Rectangles</c> the box's own
        /// background/border read, so this proves the grown rectangle actually reaches the clip path too,
        /// rather than only the box-model assertions in <c>InlineBlockDeclaredHeightGeometryTests</c>.
        /// </summary>
        [Fact]
        public async Task ClippedInlineBlockWithADeclaredHeight_ClipsToTheDeclaredHeightNotTheContent()
        {
            const string html = """
                <!DOCTYPE html>
                <html><body style='font:10pt Arial,sans-serif;margin:0'>
                <div>before <span style='display:inline-block;overflow:hidden;height:100pt;border:1pt solid'>inside</span> after</div>
                </body></html>
                """;

            var content = await PageContentAsync(html);

            // "before" is drawn first and already selects the font, so (unlike a from-scratch dump) the
            // clipped box's own text run does not re-emit a font-selection operator before its `Td` -
            // matching the structure the other fixtures in this file rely on.
            var clipped = Regex.Match(content,
                @"q\s+([-\d.]+) ([-\d.]+) m\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+h\s+W\*? n\s+BT\s+([-\d.]+) ([-\d.]+) Td");

            Assert.True(clipped.Success, "the box must push a clip path and draw its text inside it");

            double N(int group) => double.Parse(clipped.Groups[group].Value, CultureInfo.InvariantCulture);

            var bottom = Math.Min(N(2), N(6));
            var top = Math.Max(N(2), N(6));

            // `overflow: hidden` clips to the padding edge (border excluded), so this is the declared
            // height itself - a clip still built from the one-line content height (roughly 12pt) would
            // fail this by a wide margin.
            Assert.Equal(100.0, top - bottom, 1);
        }

        /// <summary>
        /// #1169: a declared height taller than the box's content under a non-default <c>vertical-align</c>
        /// must grow the clip from the anchored edge, not always downward — <c>vertical-align: bottom</c>
        /// anchors the box's own bottom (CSS 2.1 §10.8.1), so the clip's bottom edge must stay fixed
        /// between the natural and declared-height renders while its top moves up, exactly mirroring
        /// <c>InlineBlockDeclaredHeightGeometryTests.VerticalAlignBottomGrowsUpwardKeepingTheBottomEdgeFixed</c>
        /// at the paint level, per this repo's convention that a purely numeric <c>CssBox</c> assertion is
        /// not sufficient proof for anything that also affects painting/clipping.
        /// </summary>
        [Fact]
        public async Task ClippedInlineBlockWithVerticalAlignBottom_ClipGrowsUpwardKeepingItsBottomEdgeFixed()
        {
            // Body margin-top gives the bottom-anchored, 80pt-tall box enough headroom to grow upward
            // without its top going past the page edge (which the page's own outer clip would then
            // truncate, defeating the point of this test).
            const string naturalHtml = """
                <!DOCTYPE html>
                <html><body style='font:10pt Arial,sans-serif;margin:200pt 0 0 0'>
                <div><span style='font-size:40pt'>Tall</span> <span style='display:inline-block;overflow:hidden;vertical-align:bottom;border:1pt solid'>inside</span></div>
                </body></html>
                """;
            const string grownHtml = """
                <!DOCTYPE html>
                <html><body style='font:10pt Arial,sans-serif;margin:200pt 0 0 0'>
                <div><span style='font-size:40pt'>Tall</span> <span style='display:inline-block;overflow:hidden;vertical-align:bottom;height:80pt;border:1pt solid'>inside</span></div>
                </body></html>
                """;

            var naturalClip = ClipRectOf(await PageContentAsync(naturalHtml));
            var grownClip = ClipRectOf(await PageContentAsync(grownHtml));

            Assert.Equal(naturalClip.Bottom, grownClip.Bottom, 1);
            Assert.Equal(80.0, grownClip.Top - grownClip.Bottom, 1);
            Assert.True(grownClip.Top > naturalClip.Top,
                $"growth must extend upward: natural top {naturalClip.Top}, grown top {grownClip.Top}");
        }

        /// <summary>
        /// #1167: a percentage height against a definite containing block must grow the clip too, not
        /// just <c>InlineBlockDeclaredHeightGeometryTests</c>'s <c>CssBox</c>-level assertion — proving the
        /// resolved value actually reaches <c>FragmentEmitter.ClipSourceBoundsOf</c>, per this repo's
        /// convention that a purely numeric geometry assertion isn't sufficient proof for anything that
        /// also affects painting/clipping.
        /// </summary>
        [Fact]
        public async Task ClippedInlineBlockWithAPercentageHeight_ClipsToTheResolvedHeightNotTheContent()
        {
            const string html = """
                <!DOCTYPE html>
                <html><body style='font:10pt Arial,sans-serif;margin:0'>
                <div style='height:200pt'>before <span style='display:inline-block;overflow:hidden;height:50%;border:1pt solid'>inside</span> after</div>
                </body></html>
                """;

            var content = await PageContentAsync(html);
            var clip = ClipRectOf(content);

            // The containing div's declared height is 200pt, so 50% resolves to 100pt.
            Assert.Equal(100.0, clip.Top - clip.Bottom, 1);
        }

        private static (double Bottom, double Top) ClipRectOf(string content)
        {
            // Anchored on the same BT/Td text-draw suffix the other tests in this file use, so this
            // matches the clip immediately governing the box's own drawn text - not an unrelated clip
            // (e.g. the page itself) earlier in the stream. Unlike the other fixtures here, this one's
            // preceding text ("Tall") is a different font size, so the box's own text run DOES re-emit a
            // font-selection operator between BT and Td - optionally matched rather than assumed absent.
            var clipped = Regex.Match(content,
                @"q\s+([-\d.]+) ([-\d.]+) m\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+([-\d.]+) ([-\d.]+) l\s+h\s+W\*? n\s+BT\s+(?:/\S+ [-\d.]+ Tf\s+)?([-\d.]+) ([-\d.]+) Td");

            Assert.True(clipped.Success, "the box must push a clip path and draw its text inside it");

            double N(int group) => double.Parse(clipped.Groups[group].Value, CultureInfo.InvariantCulture);

            return (Math.Min(N(2), N(6)), Math.Max(N(2), N(6)));
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
