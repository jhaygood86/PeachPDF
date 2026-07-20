using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>white-space</c> actually affects whitespace-collapsing and line-wrapping - it was
    /// fully implemented already (<c>CssBox.ParseToWords</c>/<c>CssLayoutEngine.FlowBox</c>) but had
    /// zero dedicated tests anywhere in the suite before this batch.
    /// </summary>
    public class WhiteSpaceLayoutIntegrationTests
    {
        [Fact]
        public async Task Pre_PreservesMultipleConsecutiveSpacesAsLiteralWord()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='white-space:pre'>A     B</p>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.Contains(words, w => w.Text == "     ");
        }

        [Fact]
        public async Task Normal_CollapsesConsecutiveSpaces_NoLiteralSpaceWord()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'>A     B</p>"));
            var p = FindById(root, "p")!;
            var words = p.LineBoxes[0].Words;

            Assert.DoesNotContain(words, w => w.Text != null && w.Text.Length > 0 && w.Text.All(char.IsWhiteSpace));
        }

        [Fact]
        public async Task Pre_TreatsExplicitNewlineAsForcedLineBreak()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p' style='white-space:pre'>A\nB</p>"));
            var p = FindById(root, "p")!;

            Assert.Equal(2, p.LineBoxes.Count);
        }

        [Fact]
        public async Task Normal_IgnoresEmbeddedNewline_NoForcedBreak()
        {
            var (root, _) = await BuildAndLayout(Wrap("<p id='p'>A\nB</p>"));
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);
        }

        [Fact]
        public async Task NoWrap_PreventsWrapping_EvenWhenNarrowerThanContent()
        {
            var html = Wrap("<p id='p' style='white-space:nowrap; width:50px'>a long run of unwrapped text here</p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);
        }

        [Fact]
        public async Task Normal_WrapsAtNarrowWidth_ForContrastWithNoWrap()
        {
            var html = Wrap("<p id='p' style='width:50px'>a long run of unwrapped text here</p>");
            var (root, _) = await BuildAndLayout(html);
            var p = FindById(root, "p")!;

            Assert.True(p.LineBoxes.Count > 1);
        }

        // ─── &nbsp; (U+00A0) is significant, non-collapsible, non-breaking content - unlike ordinary
        // whitespace, which stays collapsible/breakable (CSS2.1 §16.4.1) ───────────

        [Fact]
        public async Task Nbsp_OnlyContent_ProducesNonZeroHeight_MatchingRealText()
        {
            var (nbspRoot, _) = await BuildAndLayout(Wrap("<div id='b'>&nbsp;</div>"));
            var (textRoot, _) = await BuildAndLayout(Wrap("<div id='b'>A</div>"));
            var nbspBox = FindById(nbspRoot, "b")!;
            var textBox = FindById(textRoot, "b")!;

            var nbspHeight = nbspBox.ActualBottom - nbspBox.Location.Y;
            var textHeight = textBox.ActualBottom - textBox.Location.Y;

            Assert.True(nbspHeight > 0, $"Expected non-zero height for nbsp-only content, got {nbspHeight}");
            Assert.InRange(nbspHeight, textHeight - 1, textHeight + 1);
        }

        [Fact]
        public async Task OrdinaryWhitespaceOnlyContent_StillProducesZeroHeight_NoRegression()
        {
            var (root, _) = await BuildAndLayout(Wrap("<div id='b'>   </div>"));
            var box = FindById(root, "b")!;

            Assert.InRange(box.ActualBottom - box.Location.Y, 0, 0.5);
        }

        [Fact]
        public async Task Nbsp_BetweenTokens_PreventsLineWrap_ContrastOrdinarySpace()
        {
            // Narrow enough that an ordinary space between "10" and "km" wraps to two lines, but a
            // non-breaking space between them must never be treated as a break opportunity.
            // "10 km" as one unbreakable unit is ~30pt wide (measured separately) - 35pt comfortably
            // fits it on one line without any wrap. 15pt is narrower than that but still wider than "10"
            // alone (~12pt), so the breakable version must wrap after "10".
            var (nbspRoot, _) = await BuildAndLayout(Wrap("<p id='p' style='width:35pt'>10&nbsp;km</p>"));
            var pNbsp = FindById(nbspRoot, "p")!;

            var (spaceRoot, _) = await BuildAndLayout(Wrap("<p id='p' style='width:15pt'>10 km</p>"));
            var pSpace = FindById(spaceRoot, "p")!;

            Assert.Single(pNbsp.LineBoxes);
            Assert.True(pSpace.LineBoxes.Count > 1,
                "expected ordinary space to still allow wrapping, for contrast with nbsp");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

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
