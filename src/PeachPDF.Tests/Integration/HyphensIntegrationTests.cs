using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    public class HyphensIntegrationTests
    {
        [Fact]
        public async Task Hyphens_DefaultsToManual()
        {
            var box = await FindByIdAsync("<p id='p'>text</p>");
            Assert.Equal("manual", box.Hyphens);
        }

        [Fact]
        public async Task Hyphens_ParsesNone()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphens:none'>text</p>");
            Assert.Equal("none", box.Hyphens);
        }

        [Fact]
        public async Task Hyphens_ParsesAuto()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphens:auto'>text</p>");
            Assert.Equal("auto", box.Hyphens);
        }

        [Fact]
        public async Task Hyphens_IsInherited()
        {
            var html = Wrap("<div style='hyphens:none'><p id='p'>text</p></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("none", box.Hyphens);
        }

        [Fact]
        public async Task SoftHyphen_ManualMode_IsBreakOpportunity_AndRendersHyphenWhenUsed()
        {
            // Regression for the pre-existing "hyphen glyph never renders" gap (see
            // docs/html-css-support.md's old note on this): a soft hyphen used as an actual line-break
            // point must now show a literal "-" on the line it ends. 30px sits strictly between "abc-"'s
            // measured width (~21.5) and the whole "abcdef" word's (~32.8), so the break is actually
            // used here (unlike a container so narrow even the hyphenated prefix doesn't fit, where the
            // word legitimately just overflows whole instead - see the width:10px case below).
            var box = await FindByIdAsync("<p id='p' style='width:30px'>abc­def</p>");
            var words = box.Boxes.SelectMany(b => b.Words).ToList();
            Assert.All(words, w => Assert.DoesNotContain('­', w.Text ?? ""));
            Assert.Contains(words, w => w.Text == "abc-");
            Assert.Contains(words, w => w.Text == "def");
        }

        [Fact]
        public async Task SoftHyphen_HyphensNone_IsNotABreakOpportunity()
        {
            var box = await FindByIdAsync("<p id='p' style='hyphens:none;width:10px'>abc­def</p>");
            // With hyphenation suppressed, "abc­def" stays one single word/text run (the soft
            // hyphen is inert, not a break point) - unlike the manual-mode case above, it is not split.
            var words = box.Boxes.SelectMany(b => b.Words).ToList();
            Assert.Single(words);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindById(root, "p")!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize  = PeachPDF.Utilities.Utils.Convert(size, 1.0);

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
