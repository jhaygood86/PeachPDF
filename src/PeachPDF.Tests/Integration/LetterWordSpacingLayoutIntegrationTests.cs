using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>letter-spacing</c> actually affects word measurement and painting - it previously
    /// parsed (see <c>CSS/FontProperty.cs</c>) but had zero layout effect, missing entirely from
    /// <c>CssUtils</c>'s cascade plumbing (<c>GetPropertyValue</c>/<c>SetPropertyValue</c>/
    /// <c>_knownPropertyNames</c>), so <c>CssBox.LetterSpacing</c> wasn't even a real settable
    /// property. Also closes the same layout-level test gap for <c>word-spacing</c>, which had only
    /// parse-level coverage despite being fully implemented.
    ///
    /// Painting is realized via the PDF <c>Tc</c> character-spacing operator (see
    /// <c>PdfGraphicsState.RealizeFont</c>/<c>XGraphicsPdfRenderer.DrawString</c>) rather than drawing
    /// character-by-character, so a letter-spaced run still reaches <see cref="RGraphics.DrawString"/>
    /// as a single call - the paint-level test below asserts exactly that (one call, carrying the
    /// resolved spacing value), not N per-character calls.
    /// </summary>
    public class LetterWordSpacingLayoutIntegrationTests
    {
        [Fact]
        public async Task LetterSpacing_AddsWidthBetweenEachAdjacentCharacterPair_NotTrailing()
        {
            // "AAAA" = 4 characters -> 3 gaps, not 4.
            var spaced = await FirstWordWidthAsync("<p id='p' style='letter-spacing:2px'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain + 3 * 2, spaced, 1);
        }

        [Fact]
        public async Task LetterSpacingNormal_LeavesWidthUnchanged()
        {
            var normal = await FirstWordWidthAsync("<p id='p' style='letter-spacing:normal'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain, normal, 1);
        }

        [Fact]
        public async Task LetterSpacingZero_LeavesWidthUnchanged()
        {
            var zero = await FirstWordWidthAsync("<p id='p' style='letter-spacing:0'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain, zero, 1);
        }

        [Fact]
        public async Task LetterSpacingNegative_ReducesWidth()
        {
            var negative = await FirstWordWidthAsync("<p id='p' style='letter-spacing:-1px'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain - 3 * 1, negative, 1);
        }

        [Fact]
        public async Task WordSpacing_IncreasesGapBetweenAdjacentWords()
        {
            var spacedGap = await InterWordGapAsync("<p id='p' style='word-spacing:5px'>AA BB</p>");
            var plainGap = await InterWordGapAsync("<p id='p'>AA BB</p>");

            Assert.Equal(plainGap + 5, spacedGap, 1);
        }

        [Fact]
        public async Task LetterSpacing_ProducesSingleDrawStringCall_CarryingResolvedSpacing()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='letter-spacing:2px'>AAAA</p>"));
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var call = Assert.Single(g.DrawStringCalls);
            Assert.Equal("AAAA", call.Text);
            Assert.Equal(2, call.LetterSpacing, 1);
        }

        [Fact]
        public async Task LetterSpacingNormal_ProducesSingleDrawStringCall_WithZeroSpacing()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'>AAAA</p>"));
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var call = Assert.Single(g.DrawStringCalls);
            Assert.Equal(0, call.LetterSpacing, 1);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<double> FirstWordWidthAsync(string bodyHtml)
        {
            var (root, _) = await BuildAndLayout(Wrap(bodyHtml));
            var p = FindById(root, "p")!;
            return CssBox.FirstWordOccurence(p, p.LineBoxes[0])!.Width;
        }

        private static async Task<double> InterWordGapAsync(string bodyHtml)
        {
            var (root, _) = await BuildAndLayout(Wrap(bodyHtml));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words.OrderBy(w => w.Left).ToList();
            Assert.Equal(2, words.Count);
            return words[1].Left - words[0].Right;
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
