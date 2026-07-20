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
        public async Task LetterSpacing_AddsWidthAfterEveryCharacterIncludingTrailing()
        {
            // "AAAA" = 4 characters -> 4 gaps: the PDF Tc operator (PaintWords/RealizeFont) applies
            // after every glyph shown including the last, and CSS Text 3 §7.2 only exempts the
            // start/end of a *line*, not the end of a word - reserving only 3 (an old CSS1/2.1-era
            // assumption) undersized the word's own box relative to what actually gets painted.
            // (Uses pt so the spacing value stays 1:1 with the engine's internal unit; px would
            // resolve at 0.75x per the spec-correct 1px = 0.75pt convention.)
            var spaced = await FirstWordWidthAsync("<p id='p' style='letter-spacing:2pt'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain + 4 * 2, spaced, 1);
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
            var negative = await FirstWordWidthAsync("<p id='p' style='letter-spacing:-1pt'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p'>AAAA</p>");

            Assert.Equal(plain - 4 * 1, negative, 1);
        }

        [Fact]
        public async Task WordSpacing_IncreasesGapBetweenAdjacentWords()
        {
            var spacedGap = await InterWordGapAsync("<p id='p' style='word-spacing:5pt'>AA BB</p>");
            var plainGap = await InterWordGapAsync("<p id='p'>AA BB</p>");

            Assert.Equal(plainGap + 5, spacedGap, 1);
        }

        [Fact]
        public async Task LetterSpacing_EmValue_ResolvesAgainstFontSize_InLayoutPoints()
        {
            // letter-spacing/word-spacing/text-indent eagerly convert an em value to layout units
            // via CssBoxProperties.NoEms. GetEmHeight() is the font size in points, so 0.5em at a
            // 20pt font is 10pt per gap - and the converted string must round-trip through
            // ParseLength as points, not px (which would resolve at 0.75x and shrink each gap).
            var spaced = await FirstWordWidthAsync(
                "<p id='p' style='font-size:20pt; letter-spacing:0.5em'>AAAA</p>");
            var plain = await FirstWordWidthAsync("<p id='p' style='font-size:20pt'>AAAA</p>");

            Assert.Equal(plain + 4 * 10, spaced, 1);
        }

        // Direct regression test for the reported bug: "REMIT PAYMENT TO" style all-caps letter-spaced
        // labels rendered as "REMITPAYMENTTO" once letter-spacing reached the natural space width, since
        // each word painted one letter-spacing unit wider than its reserved box and ate into the next
        // word's gap. A letter-spacing well past any plausible natural space width must still leave a
        // real, positive gap between words.
        [Fact]
        public async Task LetterSpacing_DoesNotCollapseInterWordGap()
        {
            var gap = await InterWordGapAsync("<p id='p' style='letter-spacing:10px'>AA BB</p>");

            Assert.True(gap > 0,
                $"Inter-word gap with letter-spacing should stay positive (no collapse), but was {gap}");
        }

        // Fix (1) alone (reserving N gaps in the word's own box) makes the *visible* inter-word gap
        // completely insensitive to letter-spacing; fix (2) (CssRect.ActualWordSpacing folding in one
        // letter-spacing unit) makes it widen proportionally, matching a real UA where the space
        // character sits in the same letter-spaced run and gets its own leading/trailing edges spaced
        // too. Combined, the letter-spaced gap should equal the plain gap plus exactly one letter-spacing
        // unit, regardless of how large letter-spacing gets.
        // Uses pt so the fed value stays 1:1 with the engine's internal unit and the assertion can stay
        // literal; px would resolve at 0.75x per the spec-correct 1px = 0.75pt convention.
        [Theory]
        [InlineData(2)]
        [InlineData(10)]
        [InlineData(20)]
        public async Task LetterSpacing_InterWordGap_WidensByExactlyOneLetterSpacingUnit(double letterSpacingPt)
        {
            var spacedGap = await InterWordGapAsync($"<p id='p' style='letter-spacing:{letterSpacingPt}pt'>AA BB</p>");
            var plainGap = await InterWordGapAsync("<p id='p'>AA BB</p>");

            Assert.Equal(plainGap + letterSpacingPt, spacedGap, 1);
        }

        // The word-box-width fix (not the inter-word-gap fix, which only ever engages when
        // CssRect.HasSpaceAfter/IsImage is true) is what closes this case: two adjacent inline runs
        // with no whitespace between them at all rely entirely on the first run's own box being wide
        // enough to include its own trailing letter-spacing.
        [Fact]
        public async Task LetterSpacing_AdjacentInlineElementsWithNoWhitespace_DoNotOverlap()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='a' style='letter-spacing:10px'>AAAA</span><span id='b'>BBBB</span>"));

            var lineOwner = FindLineBoxOwner(root)!;
            var words = lineOwner.LineBoxes[0].Words.OrderBy(w => w.Left).ToList();
            Assert.Equal(2, words.Count);

            var aWord = words[0];
            var bWord = words[1];

            Assert.True(bWord.Left >= aWord.Right,
                $"'b's word (Left={bWord.Left}) should start at or after 'a's word ends (Right={aWord.Right})");
        }

        // Closes the coverage gap that let the original bug ship: existing tests only ever exercised
        // one of word-spacing/letter-spacing at a time on a multi-word string, never both together.
        [Fact]
        public async Task WordSpacingAndLetterSpacing_Combined_BothContributeToGap()
        {
            var combinedGap = await InterWordGapAsync(
                "<p id='p' style='word-spacing:5pt;letter-spacing:3pt'>AA BB</p>");
            var plainGap = await InterWordGapAsync("<p id='p'>AA BB</p>");

            Assert.Equal(plainGap + 5 + 3, combinedGap, 1);
        }

        [Fact]
        public async Task LetterSpacing_ProducesSingleDrawStringCall_CarryingResolvedSpacing()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='letter-spacing:2pt'>AAAA</p>"));
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

        private static CssBox? FindLineBoxOwner(CssBox box)
        {
            if (box.LineBoxes.Count > 0) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindLineBoxOwner(child);
                if (found != null) return found;
            }
            return null;
        }

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
