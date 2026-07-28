using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tests for correct handling of HTML character references (entities).
    /// Regression tests for the double-decoding bug where &amp;nbsp; would render
    /// as a space instead of the literal text &amp;nbsp;.
    /// 
    /// The fix: MimeKit's HtmlTokenizer already decodes entities once, so word-splitting
    /// code should NOT decode again. Text in CssBox.Text is always pre-decoded.
    /// </summary>
    public class HtmlEntityDecodingIntegrationTests
    {
        #region Double-Escaped Entities (Should Render as Literal Entity References)

        [Fact]
        public async Task DoubleEscapedNbsp_RendersAsLiteralEntityReference()
        {
            // &amp;nbsp; should render as the literal text "&nbsp;" not as a non-breaking space
            var html = Wrap("<p id='p'>&amp;nbsp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            // The DOM should have "&nbsp;" (decoded once by tokenizer)
            Assert.Equal("&nbsp;", box.Text);

            // The rendered word should also be "&nbsp;" (not decoded again)
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("&nbsp;", word.Text);
        }

        [Fact]
        public async Task DoubleEscapedAmp_RendersAsLiteralEntityReference()
        {
            // &amp;amp; should render as the literal text "&amp;" not as "&"
            var html = Wrap("<p id='p'>&amp;amp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&amp;", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("&amp;", word.Text);
        }

        [Fact]
        public async Task DoubleEscapedLt_RendersAsLiteralEntityReference()
        {
            // &amp;lt; should render as the literal text "&lt;" not as "<"
            var html = Wrap("<p id='p'>&amp;lt;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&lt;", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("&lt;", word.Text);
        }

        [Fact]
        public async Task DoubleEscapedNumericEntity_RendersAsLiteralEntityReference()
        {
            // &amp;#65; should render as the literal text "&#65;" not as "A"
            var html = Wrap("<p id='p'>&amp;#65;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&#65;", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("&#65;", word.Text);
        }

        #endregion

        #region Single-Escaped Entities (Should Render as Decoded Characters)

        [Fact]
        public async Task SingleEscapedNbsp_RendersAsNonBreakingSpace()
        {
            // &nbsp; should render as U+00A0 (non-breaking space character)
            var html = Wrap("<p id='p'>&nbsp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            // The DOM should have U+00A0 (decoded by tokenizer)
            Assert.Equal("\u00A0", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("\u00A0", word.Text);
        }

        [Fact]
        public async Task SingleEscapedAmp_RendersAsAmpersand()
        {
            // &amp; should render as "&"
            var html = Wrap("<p id='p'>&amp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("&", word.Text);
        }

        [Fact]
        public async Task SingleEscapedLt_RendersAsLessThan()
        {
            // &lt; should render as "<"
            var html = Wrap("<p id='p'>&lt;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("<", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("<", word.Text);
        }

        [Fact]
        public async Task SingleEscapedNumericEntity_RendersAsCharacter()
        {
            // &#65; should render as "A"
            var html = Wrap("<p id='p'>&#65;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("A", box.Text);
            Assert.Single(box.Words);
            var word = box.Words[0] as CssRectWord;
            Assert.NotNull(word);
            Assert.Equal("A", word.Text);
        }

        #endregion

        #region Code Blocks and Pre Elements

        [Fact]
        public async Task DoubleEscapedEntitiesInCodeBlock_RenderCorrectly()
        {
            // Common use case: showing HTML markup in a <code> block
            var html = Wrap("<code id='c'>Use &amp;nbsp; for spaces</code>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.Equal("Use &nbsp; for spaces", box.Text);
            // Should have 4 words: "Use", "&nbsp;", "for", "spaces"
            var words = box.Words.OfType<CssRectWord>().Where(w => w.Text.Length > 0).ToList();
            Assert.Contains(words, w => w.Text == "&nbsp;");
        }

        [Fact]
        public async Task DoubleEscapedEntitiesInPreElement_RenderCorrectly()
        {
            // Pre-formatted text should preserve entity references
            var html = Wrap("<pre id='p'>&amp;lt;html&amp;gt;</pre>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&lt;html&gt;", box.Text);
            var words = box.Words.OfType<CssRectWord>().ToList();
            var text = string.Concat(words.Select(w => w.Text));
            Assert.Equal("&lt;html&gt;", text);
        }

        [Fact]
        public async Task MixedEntitiesInParagraph_RenderCorrectly()
        {
            // Mixed single and double escaped entities
            var html = Wrap("<p id='p'>A &amp; B &amp;amp; C</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            // Should be: "A & B &amp; C"
            Assert.Equal("A & B &amp; C", box.Text);
        }

        #endregion

        #region CSS Content Strings

        [Fact]
        public async Task CssContentWithCssEscape_RendersLiterally()
        {
            // CSS uses \26 escape syntax, not HTML entity syntax
            // content: "\26" should render as literal "&" character
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
p::before { content: ""\26""; }
</style>
</head>
<body>
<p id='p'>text</p>
</body>
</html>";
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            // Find the ::before pseudo-element
            var beforeBox = box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);
            Assert.NotNull(beforeBox);

            // CSS escape \26 should produce "&" character (not decoded to something else)
            Assert.Equal("&", beforeBox.Text);
            if (beforeBox.Words.Count > 0)
            {
                var word = beforeBox.Words[0] as CssRectWord;
                Assert.NotNull(word);
                Assert.Equal("&", word.Text);
            }
        }

        [Fact]
        public async Task CssContentWithCssEscapeInString_RendersLiterally()
        {
            // Another CSS escape test: \3C is "<" in Unicode
            var html = @"<!DOCTYPE html>
<html>
<head>
<style>
p::after { content: "" \3C tag\3E ""; }
</style>
</head>
<body>
<p id='p'>text</p>
</body>
</html>";
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            var afterBox = box.Boxes.FirstOrDefault(b => b.IsAfterPseudoElement);
            Assert.NotNull(afterBox);

            // Should contain < and > characters from CSS escapes
            Assert.Contains("<", afterBox.Text);
            Assert.Contains(">", afterBox.Text);
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task WhitespacePreservation_WithEntities()
        {
            // white-space: pre should preserve entities
            var html = Wrap("<p id='p' style='white-space:pre'>&amp;nbsp;  test</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            Assert.Equal("&nbsp;  test", box.Text);
        }

        [Fact]
        public async Task MultipleDoubleEscapedEntities_InSentence()
        {
            // Real-world scenario: documentation showing multiple entity examples
            var html = Wrap("<p id='p'>Entities: &amp;lt;, &amp;gt;, &amp;amp;, &amp;nbsp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            var expected = "Entities: &lt;, &gt;, &amp;, &nbsp;";
            Assert.Equal(expected, box.Text);
        }

        [Fact]
        public async Task TripleEscapedEntity_RendersWithTwoLevels()
        {
            // Edge case: &amp;amp;nbsp; should be "&amp;nbsp;" (decoded twice becomes once-escaped)
            var html = Wrap("<p id='p'>&amp;amp;nbsp;</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            // Tokenizer decodes &amp; to & once, so we get "&amp;nbsp;"
            Assert.Equal("&amp;nbsp;", box.Text);
        }

        [Fact]
        public async Task EntityInAttributeValue_DecodedCorrectly()
        {
            // Attributes are also decoded by the tokenizer
            var html = Wrap("<p id='p' title='A &amp; B'>text</p>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;

            var title = box.GetAttribute("title", "");
            Assert.Equal("A & B", title);
        }

        #endregion

        #region Helper Methods

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

        #endregion
    }
}
