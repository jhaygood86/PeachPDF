using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for <c>@media</c> feature-condition evaluation (issue #235's <c>@media</c>
    /// half). Each case parses a stylesheet whose <c>@media</c> block overrides <c>#box</c>'s width from
    /// 100pt to 300pt, renders at a chosen page size / color scheme, and asserts whether the override
    /// actually won — which fails on the pre-fix engine (where every <c>@media</c> block applied
    /// unconditionally). Page geometry and color scheme are set BEFORE <c>SetHtml</c>, mirroring
    /// <c>PdfGenerator.SetContent</c>, because the media context is built while the CSS tree is generated.
    /// </summary>
    public class MediaQueryLayoutIntegrationTests
    {
        // Page-box widths in points: 800pt ≈ 1066px, 300pt = 400px. Feature px values convert at
        // 1px = 0.75pt, so e.g. min-width:700px = 525pt (applies at 800pt, not at 300pt).
        private const double WidePt = 800;
        private const double NarrowPt = 300;

        [Theory]
        // ── width (v3 min-/max- syntax) ─────────────────────────────────────────
        [InlineData("(min-width: 700px)", WidePt, 600, false, true)]
        [InlineData("(min-width: 700px)", NarrowPt, 600, false, false)]
        [InlineData("(max-width: 600px)", NarrowPt, 600, false, true)]
        [InlineData("(max-width: 600px)", WidePt, 600, false, false)]
        // ── width (v4 range syntax) + rem against the initial 16px ──────────────
        [InlineData("(width >= 48rem)", WidePt, 600, false, true)]   // 48rem = 576pt
        [InlineData("(width >= 48rem)", NarrowPt, 600, false, false)]
        [InlineData("(width < 40rem)", NarrowPt, 600, false, true)]  // 40rem = 480pt
        [InlineData("(width < 40rem)", WidePt, 600, false, false)]
        [InlineData("(width > 40rem)", WidePt, 600, false, true)]    // strict >
        [InlineData("(width > 40rem)", NarrowPt, 600, false, false)]
        [InlineData("(width <= 40rem)", NarrowPt, 600, false, true)] // range <=
        [InlineData("(width <= 40rem)", WidePt, 600, false, false)]
        [InlineData("(device-width >= 700px)", WidePt, 600, false, true)]
        [InlineData("(inline-size >= 700px)", WidePt, 600, false, true)]
        [InlineData("(block-size >= 700px)", WidePt, 800, false, true)]
        // ── height ──────────────────────────────────────────────────────────────
        [InlineData("(min-height: 700px)", WidePt, 800, false, true)]  // 525pt ≤ 600pt page? no: 525 ≤ 800
        [InlineData("(min-height: 700px)", WidePt, 400, false, false)] // 525pt > 400pt page
        // ── orientation ───────────────────────────────────────────────────────
        [InlineData("(orientation: landscape)", WidePt, 600, false, true)]
        [InlineData("(orientation: landscape)", NarrowPt, 900, false, false)]
        [InlineData("(orientation: portrait)", NarrowPt, 900, false, true)]
        [InlineData("(orientation: portrait)", WidePt, WidePt, false, true)]   // square → portrait (MQ4 §4.1)
        [InlineData("(orientation: landscape)", WidePt, WidePt, false, false)]
        // ── aspect-ratio ─────────────────────────────────────────────────────────
        [InlineData("(min-aspect-ratio: 1/1)", WidePt, 600, false, true)]
        [InlineData("(min-aspect-ratio: 1/1)", NarrowPt, 900, false, false)]
        // ── resolution (device reports 96dpi = 1dppx) ──────────────────────────
        [InlineData("(min-resolution: 1dppx)", WidePt, 600, false, true)]
        [InlineData("(min-resolution: 2dppx)", WidePt, 600, false, false)]
        [InlineData("(max-resolution: 100dpi)", WidePt, 600, false, true)]
        [InlineData("(max-device-pixel-ratio: 1)", WidePt, 600, false, true)]
        [InlineData("(min-device-pixel-ratio: 2)", WidePt, 600, false, false)]
        // Zero-denominator ratio is unparseable → permissive (feature ignored).
        [InlineData("(min-aspect-ratio: 1/0)", WidePt, 600, false, true)]
        // ── discrete features (print device characteristics) ────────────────────
        [InlineData("(color)", WidePt, 600, false, true)]
        [InlineData("(monochrome)", WidePt, 600, false, false)]
        [InlineData("(min-monochrome: 0)", WidePt, 600, false, true)]
        [InlineData("(grid: 0)", WidePt, 600, false, true)]
        [InlineData("(grid)", WidePt, 600, false, false)]
        [InlineData("(hover: none)", WidePt, 600, false, true)]
        [InlineData("(hover: hover)", WidePt, 600, false, false)]
        [InlineData("(hover)", WidePt, 600, false, false)]
        [InlineData("(any-hover: none)", WidePt, 600, false, true)]
        [InlineData("(pointer: none)", WidePt, 600, false, true)]
        [InlineData("(any-pointer: none)", WidePt, 600, false, true)]
        [InlineData("(update: none)", WidePt, 600, false, true)]
        [InlineData("(scripting: none)", WidePt, 600, false, true)]
        [InlineData("(prefers-reduced-motion: reduce)", WidePt, 600, false, true)]
        [InlineData("(prefers-reduced-motion: no-preference)", WidePt, 600, false, false)]
        [InlineData("(prefers-reduced-motion)", WidePt, 600, false, true)]  // boolean, reduce stance
        [InlineData("(prefers-contrast: no-preference)", WidePt, 600, false, true)]
        [InlineData("(prefers-reduced-transparency: no-preference)", WidePt, 600, false, true)]
        // ── prefers-color-scheme (config-driven) ────────────────────────────────
        [InlineData("(prefers-color-scheme: dark)", WidePt, 600, true, true)]
        [InlineData("(prefers-color-scheme: dark)", WidePt, 600, false, false)]
        [InlineData("(prefers-color-scheme: light)", WidePt, 600, false, true)]
        // ── media type + type-with-feature ──────────────────────────────────────
        [InlineData("print", WidePt, 600, false, true)]
        [InlineData("screen and (min-width: 1px)", WidePt, 600, false, false)]
        [InlineData("print and (min-width: 700px)", WidePt, 600, false, true)]
        [InlineData("print and (min-width: 700px)", NarrowPt, 600, false, false)]
        // ── not / comma-OR ──────────────────────────────────────────────────────
        [InlineData("not all and (min-width: 100px)", WidePt, 600, false, false)]
        [InlineData("(min-width: 5000px), (max-width: 600px)", NarrowPt, 600, false, true)]
        [InlineData("(min-width: 5000px), (max-width: 600px)", WidePt, 600, false, false)]
        // ── a feature PeachPDF can't evaluate → the block is ignored (MQ4 "unknown → false") ──
        //    whether it's registered-but-unmodeled (`scan`) or an unregistered name (`color-gamut`).
        [InlineData("(scan: progressive)", WidePt, 600, false, false)]
        [InlineData("(color-gamut: p3)", WidePt, 600, false, false)]
        public async Task MediaFeature_GatesRuleApplication(
            string condition, double widthPt, double heightPt, bool dark, bool shouldApply)
        {
            var html =
                "<!DOCTYPE html><html><head><style>" +
                "#box { width: 100pt; height: 20pt; }" +
                "@media " + condition + " { #box { width: 300pt; } }" +
                "</style></head><body><div id='box'></div></body></html>";

            var scheme = dark ? PdfColorScheme.Dark : PdfColorScheme.Light;
            var root = await BuildAtPage(html, widthPt, heightPt, scheme);
            var box = FindById(root, "box")!;

            if (shouldApply)
                Assert.InRange(box.ActualBoxSizingWidth, 250, 350);
            else
                Assert.InRange(box.ActualBoxSizingWidth, 50, 150);
        }

        [Fact]
        public async Task SameStylesheet_DifferentPageWidths_SelectDifferentBreakpoint()
        {
            // A mobile-first stylesheet: base 1 column, ≥700px 3 columns. The only thing that changes
            // between the two renders is the page width — proving width now actually gates.
            var html =
                "<!DOCTYPE html><html><head><style>" +
                "#grid { display: grid; grid-template-columns: 1fr; width: 100%; }" +
                "@media (min-width: 700px) { #grid { grid-template-columns: 1fr 1fr 1fr; } }" +
                "</style></head><body><div id='grid'>" +
                "<div class='cell'>a</div><div class='cell'>b</div><div class='cell'>c</div>" +
                "</div></body></html>";

            var wide = await BuildAtPage(html, WidePt, 600, PdfColorScheme.Light);
            var narrow = await BuildAtPage(html, NarrowPt, 600, PdfColorScheme.Light);

            // Wide: three columns → the three cells sit on one row (same Y, increasing X).
            var wideCells = FindAllByClass(wide, "cell");
            Assert.Equal(3, wideCells.Count);
            Assert.Equal(wideCells[0].Location.Y, wideCells[2].Location.Y, 1.0);
            Assert.True(wideCells[0].Location.X < wideCells[2].Location.X);

            // Narrow: one column → the cells stack (increasing Y).
            var narrowCells = FindAllByClass(narrow, "cell");
            Assert.Equal(3, narrowCells.Count);
            Assert.True(narrowCells[0].Location.Y < narrowCells[2].Location.Y);
        }

        // ─── Harness (sets page geometry + color scheme BEFORE SetHtml) ─────────

        private static async Task<CssBox> BuildAtPage(string html, double widthPt, double heightPt, PdfColorScheme scheme)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);

            var size = new XSize(widthPt, heightPt);
            // Set the media inputs before the CSS tree is generated - the MediaQueryContext is built
            // during SetHtml from these values (as PdfGenerator.SetContent does).
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.PreferredColorScheme = scheme;

            await container.SetHtml(html, null);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
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

        private static System.Collections.Generic.List<CssBox> FindAllByClass(CssBox box, string className)
        {
            var results = new System.Collections.Generic.List<CssBox>();
            Recurse(box, className, results);
            return results;

            static void Recurse(CssBox b, string cls, System.Collections.Generic.List<CssBox> acc)
            {
                var val = b.HtmlTag?.TryGetAttribute("class", "");
                if (val != null && System.Linq.Enumerable.Contains(val.Split(' '), cls))
                    acc.Add(b);
                foreach (var child in b.Boxes)
                    Recurse(child, cls, acc);
            }
        }
    }
}
