using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    // Regression coverage for two compounding bugs that caused css4.pub's Icelandic
    // dictionary title page to overflow onto a second physical PDF page instead of
    // fitting on one:
    //  1. GetEmHeight() resolved `1em` against a font's line-spacing metric
    //     (RFont.Height) instead of its computed font-size (RFont.Size), inflating
    //     every em-based margin/padding/line-height.
    //  2. Length.ToPixels() resolved absolute units (pt/in/cm/mm/pc) against a
    //     hardcoded 96dpi CSS-px convention, while the rest of the engine (page size
    //     from @page, font-size resolution) treats 1 internal unit as 1 point by
    //     default - inflating every explicit pt/in/cm/mm length by 96/72.
    public class TitlePageIntegrationTests
    {
        // ─── Targeted unit-resolution bugs ──────────────────────────────────────

        [Fact]
        public async Task EmMargin_ResolvesAgainstFontSize_NotFontMetricsHeight()
        {
            var html = Wrap("<div id='target' style='font-size:20pt; margin-top:1em;'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;
            Assert.Equal(20.0, target.ActualMarginTop, 0.5);
        }

        [Fact]
        public async Task EmLineHeight_ResolvesAgainstFontSize_NotFontMetricsHeight()
        {
            var html = Wrap("<div id='target' style='font-size:20pt; line-height:1.2em;'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;
            Assert.Equal(24.0, target.ActualLineHeight, 0.5);
        }

        [Fact]
        public async Task PtMargin_ResolvesDirectlyAsPoints_NotScaledFor96Dpi()
        {
            var html = Wrap("<div id='target' style='margin-top:10pt;'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;
            Assert.Equal(10.0, target.ActualMarginTop, 0.5);
        }

        [Fact]
        public async Task MmMargin_ResolvesDirectlyAsPoints_NotScaledFor96Dpi()
        {
            var html = Wrap("<div id='target' style='margin-top:25.4mm;'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var target = FindById(root, "target")!;
            Assert.Equal(72.0, target.ActualMarginTop, 0.5);
        }

        // ─── Real-world repro: css4.pub dictionary title page ───────────────────

        [Fact]
        public async Task TitlePage_MixedBlockInlineHeading_FitsOnOnePage()
        {
            // Mirrors https://css4.pub/2015/icelandic/dictionary.html's real markup/CSS:
            // <small>/<span> forced to display:block (mixed with loose inline text in the
            // h1), an explicit pt line-height on the heading (from the real page's
            // `font: 60pt/70pt Faunus` shorthand) plus em-based font-size/line-height/margin
            // on its children. Uses the system default font instead of the page's real
            // Faunus/Satyr10 fonts and forces each heading phrase onto its own single line
            // (matching the design's intent for this display heading) since font
            // substitution's width differences are an orthogonal concern from the two
            // height-resolution bugs under test here.
            var html = Wrap(@"
                <style>
                    body { widows: 1; orphans: 1; }
                    .titlepage p { text-indent: 0; text-align: center; }
                    .titlepage h1 { font-size: 60pt; line-height: 70pt; text-align: center; margin: 0.8em 0 1em; white-space: nowrap; }
                    .titlepage small { display: block; font-size: 0.4em; line-height: 0.4em; }
                    .titlepage span { display: block; font-size: 1.3em; line-height: 1.2em; }
                    .titlepage p { font-size: 15pt; }
                </style>
                <section class='titlepage'>
                <h1><small>A Concise</small><span>Dictionary</span> <small>of</small> Old Icelandic</h1>
                <p id='fonts'>Fonts by <a href='#'>Monokrom</a></p>
                <p id='pdf'>Formatting by <a href='#'>Prince</a></p>
                </section>");

            // Page content box for @page { size: 160mm 200mm; margin: 20mm } → 120mm x 160mm.
            const double mmToPt = 72.0 / 25.4;
            var pageContentHeight = 160 * mmToPt;
            var pageContentWidth = 120 * mmToPt;

            var (root, container) = await BuildAndLayout(html, pageContentWidth, pageContentHeight);

            var h1 = FindByTag(root, "h1")!;
            var lastParagraph = FindById(root, "pdf")!;

            // Each of h1's 4 children (small/span/small/anonymous-block) should stack
            // monotonically, guarding against a future regression re-inflating any one.
            double previousBottom = h1.Location.Y;
            foreach (var child in h1.Boxes)
            {
                Assert.True(child.ActualBottom > previousBottom);
                previousBottom = child.ActualBottom;
            }

            // Same computation PdfGenerator.cs uses to decide the physical page count -
            // this directly encodes "fits on one physical page", not an arbitrary pixel number.
            var totalPages = Math.Ceiling(lastParagraph.ActualBottom / container.PageSize.Height);
            Assert.Equal(1, totalPages);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(
            string html, double pageWidth = 595, double pageHeight = 842)
        {
            var adapter = new PdfSharpAdapter();
            // Match the production PdfGenerator setting: PixelsPerInch=72 → pixelsPerPoint=1.0.
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(pageWidth, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
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
