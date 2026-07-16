using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    // NOTE: this repo's CSS identifier tokenizer normalizes every keyword value to lowercase
    // at parse time (ValueExtensions.ToIdentifier), same as every other keyword property here
    // (e.g. Hyphens/display). CssBox.PdfTagType is therefore always lowercase regardless of the
    // casing an author or the UA stylesheet writes - assertions below compare case-insensitively,
    // and downstream consumers (StructureTagMapper) must re-resolve the value through
    // Map.PdfTagTypes (also case-insensitive) to recover the canonical PDF /S structure-type
    // spelling rather than trusting the stored string's casing.
    public class PdfTagTypeIntegrationTests
    {
        [Fact]
        public async Task PdfTagType_DefaultsToAuto()
        {
            // <cite> has no default-stylesheet -peachpdf-pdf-tag-type rule (unlike <span>, which
            // does), so it stays at the property's own initial value at the pure-cascade level.
            var box = await FindByIdAsync("<cite id='p'>text</cite>");
            Assert.Equal("auto", box.PdfTagType, ignoreCase: true);
        }

        [Theory]
        [InlineData("h1", "H1")]
        [InlineData("h2", "H2")]
        [InlineData("h3", "H3")]
        [InlineData("h4", "H4")]
        [InlineData("h5", "H5")]
        [InlineData("h6", "H6")]
        [InlineData("p", "P")]
        [InlineData("div", "Div")]
        [InlineData("span", "Span")]
        [InlineData("ul", "L")]
        [InlineData("ol", "L")]
        [InlineData("li", "LI")]
        [InlineData("dl", "DL")]
        [InlineData("dt", "DT")]
        [InlineData("dd", "DD")]
        [InlineData("table", "Table")]
        [InlineData("tr", "TR")]
        [InlineData("th", "TH")]
        [InlineData("td", "TD")]
        [InlineData("thead", "THead")]
        [InlineData("tbody", "TBody")]
        [InlineData("tfoot", "TFoot")]
        [InlineData("caption", "Caption")]
        [InlineData("figcaption", "Caption")]
        [InlineData("img", "Figure")]
        [InlineData("figure", "Figure")]
        [InlineData("blockquote", "BlockQuote")]
        [InlineData("q", "Quote")]
        [InlineData("article", "Art")]
        [InlineData("section", "Sect")]
        [InlineData("nav", "Sect")]
        [InlineData("aside", "Sect")]
        [InlineData("header", "Div")]
        [InlineData("footer", "Div")]
        [InlineData("main", "Div")]
        [InlineData("hr", "Artifact")]
        [InlineData("code", "Code")]
        public async Task PdfTagType_DefaultStylesheet_MapsTag(string tag, string expected)
        {
            var html = tag switch
            {
                "img" => Wrap("<img id='p' src='x.png' />"),
                "hr" => Wrap("<hr id='p' />"),
                "tr" => Wrap("<table><tbody><tr id='p'><td>x</td></tr></tbody></table>"),
                "th" or "td" => Wrap(
                    $"<table><tbody><tr><{tag} id='p'>x</{tag}></tr></tbody></table>"),
                "thead" => Wrap("<table><thead id='p'><tr><td>H</td></tr></thead><tbody><tr><td>x</td></tr></tbody></table>"),
                "tbody" => Wrap("<table><tbody id='p'><tr><td>x</td></tr></tbody></table>"),
                "tfoot" => Wrap("<table><tbody><tr><td>x</td></tr></tbody><tfoot id='p'><tr><td>F</td></tr></tfoot></table>"),
                _ => Wrap($"<{tag} id='p'>text</{tag}>")
            };

            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal(expected, box.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task PdfTagType_LinkAppliesOnlyToAnchorsWithHref()
        {
            var withHref = await FindByIdAsync("<a id='p' href='https://example.com'>link</a>");
            Assert.Equal("Link", withHref.PdfTagType, ignoreCase: true);

            var withoutHref = await FindByIdAsync("<a id='p' name='anchor'>anchor</a>");
            Assert.Equal("auto", withoutHref.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task PdfTagType_ParsesNone()
        {
            var box = await FindByIdAsync("<div id='p' style='-peachpdf-pdf-tag-type:none'>text</div>");
            Assert.Equal("none", box.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task PdfTagType_AuthorRuleOverridesUaDefault()
        {
            var box = await FindByIdAsync("<div id='p' style='-peachpdf-pdf-tag-type:BlockQuote'>text</div>");
            Assert.Equal("BlockQuote", box.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task PdfTagType_IsNotInherited()
        {
            var html = Wrap("<div style='-peachpdf-pdf-tag-type:BlockQuote'><span id='p'>text</span></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            // Child resolves via its own UA default (Span), not the parent's override.
            Assert.Equal("Span", box.PdfTagType, ignoreCase: true);
        }

        [Fact]
        public async Task PdfTagType_InvalidValueFallsBackToAuto()
        {
            // An invalid keyword fails the property's Converter at parse time, so the whole
            // declaration is dropped - the UA stylesheet's own "div" rule still applies underneath,
            // exactly as if the invalid inline style had never been written.
            var box = await FindByIdAsync("<div id='p' style='-peachpdf-pdf-tag-type:NotARealTag'>text</div>");
            Assert.Equal("Div", box.PdfTagType, ignoreCase: true);
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
