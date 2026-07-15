using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for real automatic hyphenation (<c>hyphens: auto</c>). Previously there was
    /// no hyphenation engine at all — <c>auto</c> behaved identically to <c>manual</c> (soft-hyphen-only,
    /// see <see cref="HyphensIntegrationTests"/>). Per the CSS Text spec, automatic hyphenation requires
    /// knowing the text's language; a document with no <c>lang</c> and no
    /// <see cref="PdfGenerateConfig.DefaultLanguage"/> fallback correctly does NOT get automatic
    /// hyphenation — this is intentional spec-compliant behavior, not a gap, so several tests here
    /// specifically confirm the no-language case stays a no-op.
    /// </summary>
    public class AutoHyphenationIntegrationTests
    {
        [Fact]
        public async Task HyphensAuto_WithKnownLanguage_LongWordSplitsWithHyphenGlyph()
        {
            // "antidisestablishmentarianism" doesn't fit in a narrow column at all; with a known
            // language it must hyphenate (multiple fragments, one ending in a literal "-") instead of
            // just overflowing.
            var box = await FindWordsBoxAsync(
                "<html lang=\"en\"><body><p id=\"p\" style=\"width:80px;hyphens:auto\">antidisestablishmentarianism</p></body></html>");

            Assert.True(box.Words.Count > 1, "expected the word to be split into multiple fragments");
            Assert.Contains(box.Words, w => w.Text!.EndsWith('-'));
        }

        [Fact]
        public async Task HyphensAuto_BreakPointIsAnActualHyphenationEngineCandidate()
        {
            var box = await FindWordsBoxAsync(
                "<html lang=\"en\"><body><p id=\"p\" style=\"width:80px;hyphens:auto\">antidisestablishmentarianism</p></body></html>");

            var candidates = HyphenationEngine.FindHyphenationPoints("antidisestablishmentarianism", "en");
            var firstFragmentLength = box.Words[0].Text!.TrimEnd('-').Length;

            Assert.Contains(firstFragmentLength, candidates);
        }

        [Fact]
        public async Task HyphensAuto_NoLanguageAnywhere_DoesNotHyphenate_Regression()
        {
            // Per spec: hyphens:auto with an unknown language must behave like manual, i.e. no
            // algorithmic hyphenation. This is the exact shape of css4.pub's real dictionary page (no
            // <html lang>, hyphens:auto, zero soft hyphens) — confirms it's a deliberate no-op, not a bug.
            var box = await FindWordsBoxAsync(
                "<html><body><p id=\"p\" style=\"width:80px;hyphens:auto\">antidisestablishmentarianism</p></body></html>");

            Assert.Single(box.Words);
            Assert.Equal("antidisestablishmentarianism", box.Words[0].Text);
        }

        [Fact]
        public async Task HyphensAuto_ShortWordThatFits_IsNeverSplit()
        {
            var box = await FindWordsBoxAsync(
                "<html lang=\"en\"><body><p id=\"p\" style=\"width:400px;hyphens:auto\">hyphenation</p></body></html>");

            Assert.Single(box.Words);
            Assert.Equal("hyphenation", box.Words[0].Text);
        }

        [Fact]
        public async Task HyphensManual_NoSoftHyphen_IgnoresDocumentLanguage_Regression()
        {
            // hyphens:manual must never consult HyphenationEngine, even with a known document language -
            // only an explicit soft hyphen is a break opportunity in manual mode.
            var box = await FindWordsBoxAsync(
                "<html lang=\"en\"><body><p id=\"p\" style=\"width:80px;hyphens:manual\">antidisestablishmentarianism</p></body></html>");

            Assert.Single(box.Words);
            Assert.Equal("antidisestablishmentarianism", box.Words[0].Text);
        }

        // ── HtmlContainerInt.DocumentLanguage ────────────────────────────────────

        [Theory]
        [InlineData("<html lang=\"is\">", "is")]
        [InlineData("<html lang=\"en-US\">", "en-US")]
        [InlineData("<html>", null)]
        public async Task DocumentLanguage_ReflectsHtmlLangAttribute(string htmlOpenTag, string? expected)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml($"{htmlOpenTag}<body><p>text</p></body></html>", null);

            Assert.Equal(expected, container.DocumentLanguage);
        }

        // ── PdfGenerateConfig.DefaultLanguage fallback (PdfGenerator.SetContent) ─

        [Fact]
        public async Task DefaultLanguage_FillsInOnlyWhenDocumentDeclaresNone()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainer(adapter);
            var config = new PdfGenerateConfig { DefaultLanguage = "en-US" };

            await PdfGenerator.SetContent(container, config, "<html><body>text</body></html>", null, new XSize(600, 800));

            Assert.Equal("en-US", container.HtmlContainerInt.DocumentLanguage);
        }

        [Fact]
        public async Task DefaultLanguage_DoesNotOverrideDocumentsOwnLang()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainer(adapter);
            var config = new PdfGenerateConfig { DefaultLanguage = "en-US" };

            await PdfGenerator.SetContent(container, config, "<html lang=\"is\"><body>text</body></html>", null, new XSize(600, 800));

            Assert.Equal("is", container.HtmlContainerInt.DocumentLanguage);
        }

        [Fact]
        public async Task DefaultLanguage_Unset_LeavesLanguageNull_WhenDocumentDeclaresNone()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainer(adapter);
            var config = new PdfGenerateConfig();

            await PdfGenerator.SetContent(container, config, "<html><body>text</body></html>", null, new XSize(600, 800));

            Assert.Null(container.HtmlContainerInt.DocumentLanguage);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> FindWordsBoxAsync(string html)
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
            var element = FindById(container.Root!, "p")!;
            return element.Words.Count > 0 ? element : element.Boxes.First(b => b.Words.Count > 0);
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
