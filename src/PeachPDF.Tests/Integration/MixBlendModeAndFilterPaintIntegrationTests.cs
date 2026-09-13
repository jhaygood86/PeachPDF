using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>mix-blend-mode</c> and the native <c>filter</c> functions
    /// (<c>opacity()</c>/<c>brightness()</c>/<c>contrast()</c>/<c>invert()</c>) actually reach the real
    /// PDF-writing layer, not just that they parse and route through <c>FragmentPainter</c>'s tile path -
    /// per this repo's testing convention that a content-stream-substring match alone is not proof of
    /// correct rendering (a token can appear while the composited result is wrong). Each test also checks
    /// the structural adjacency CLAUDE.md calls for: the <c>gs</c> operator that activates the relevant
    /// ExtGState and the <c>Do</c> that paints the affected Form XObject must appear on the same
    /// <c>cm</c>-prefixed content-stream line (<see cref="XGraphicsPdfRenderer.DrawImageWithOpacity"/>/
    /// <c>DrawImageWithColorMatrix</c>'s own emitted format), not merely somewhere in the same document.
    /// Dual-rasterization (PDFium + MuPDF) was done manually for these cases per CLAUDE.md - MuPDF is a
    /// documented, expected non-match for anything going through <c>/TR</c> (it does not implement ExtGState
    /// transfer functions at all - see <c>XGraphicsPdfRenderer.DrawImageWithColorMatrix</c>'s remarks) - so
    /// only PDFium is authoritative there; <c>/BM</c> (mix-blend-mode) is verified as agreeing on both.
    /// </summary>
    public class MixBlendModeAndFilterPaintIntegrationTests
    {
        private static readonly Regex GsThenDoOnSameCmLine =
            new(@"cm\s+/GS\d+\s+gs\s+/Fm\d+\s+Do", RegexOptions.Compiled);

        [Theory]
        [InlineData("multiply", "Multiply")]
        [InlineData("screen", "Screen")]
        [InlineData("overlay", "Overlay")]
        [InlineData("darken", "Darken")]
        [InlineData("lighten", "Lighten")]
        [InlineData("color-dodge", "ColorDodge")]
        [InlineData("color-burn", "ColorBurn")]
        [InlineData("hard-light", "HardLight")]
        [InlineData("soft-light", "SoftLight")]
        [InlineData("difference", "Difference")]
        [InlineData("exclusion", "Exclusion")]
        [InlineData("hue", "Hue")]
        [InlineData("saturation", "Saturation")]
        [InlineData("color", "Color")]
        [InlineData("luminosity", "Luminosity")]
        public async Task MixBlendMode_ProducesTheMatchingBlendModeExtGState_AdjacentToItsDo(string cssKeyword, string pdfBlendModeName)
        {
            var html = $$"""
                <!DOCTYPE html><html><body>
                <div style="position:relative; width: 60pt; height: 60pt; background:#ff0000;"></div>
                <div style="position:relative; top:-40pt; width: 60pt; height: 60pt; background:#00ff00; mix-blend-mode: {{cssKeyword}};"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains($"/BM /{pdfBlendModeName}", pdfText);
            Assert.Matches(GsThenDoOnSameCmLine, pdfText);
        }

        [Fact]
        public async Task MixBlendModeNormal_ProducesNoBlendModeExtGState()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; mix-blend-mode: normal;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("/BM /", pdfText);
        }

        [Theory]
        [InlineData("brightness(1.5)")]
        [InlineData("contrast(0.5)")]
        [InlineData("invert(1)")]
        public async Task ChannelIndependentFilterFunction_ProducesATransferFunctionExtGState_AdjacentToItsDo(string filter)
        {
            var html = $$"""
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; filter: {{filter}};"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/TR", pdfText);
            Assert.Matches(GsThenDoOnSameCmLine, pdfText);
        }

        [Theory]
        [InlineData("grayscale(50%)")]
        [InlineData("sepia(1)")]
        [InlineData("saturate(2)")]
        [InlineData("hue-rotate(90deg)")]
        [InlineData("blur(4px)")]
        public async Task CrossChannelOrBlurFilterFunction_ProducesNoTransferFunction_DocumentedNoOp(string filter)
        {
            // The exact pitfall CLAUDE.md warns about (a prior gradient spreadMethod bug shipped as a
            // silent no-op with only a parser test) - confirms these genuinely contribute nothing at
            // paint time, not just that they parse.
            var html = $$"""
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; filter: {{filter}};"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("/TR", pdfText);
        }

        [Fact]
        public async Task FilterOpacity_ComposesMultiplicativelyWithTheOpacityProperty()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; opacity: 0.5; filter: opacity(0.5);"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // opacity: 0.5 * filter: opacity(0.5) = 0.25 - ExtGState /ca carries the product as one value.
            Assert.Contains("/ca 0.25", pdfText);
        }

        [Fact]
        public async Task FilterNone_ProducesNoTransferFunctionAndNoExtraOpacityExtGState()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; filter: none;"></div>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("/TR", pdfText);
        }

        private static readonly Regex CmThenGsThenDoWithValues =
            new(@"(?<a>-?[\d.]+)\s+(?<b>-?[\d.]+)\s+(?<c>-?[\d.]+)\s+(?<d>-?[\d.]+)\s+(?<e>-?[\d.]+)\s+(?<f>-?[\d.]+)\s+cm\s+/GS\d+\s+gs\s+/Fm\d+\s+Do",
                RegexOptions.Compiled);

        /// <summary>
        /// Regression test for the tile-compositing double-scale bug: <c>GraphicsAdapter.CreateTile</c>
        /// used to size a tile's Form XObject <c>/BBox</c> directly from its caller's width/height, which
        /// arrive in the engine's own "inflated" (pre-<c>PixelsPerPoint</c>-division) layout-unit space -
        /// the same space <c>FragmentPainter.PaintWithOpacity</c> also uses, unconverted, for the
        /// <c>destRect</c> it later composites that same tile with. Content painted INSIDE the tile was
        /// already correctly divided by <c>PixelsPerPoint</c> (every <see cref="GraphicsAdapter"/> draw
        /// call does that), so as long as <c>PixelsPerPoint == 1</c> (the common case - no
        /// <c>ShrinkToFit</c>/<c>ScaleToPageSize</c> rescale, default <c>PixelsPerInch</c>) the mismatch
        /// was invisible. Under a real rescale (<c>PixelsPerPoint != 1</c>, exactly what
        /// <c>PdfGenerateConfig.ShrinkToFit</c> produces whenever content needs shrinking to fit the
        /// page), the oversized <c>/BBox</c> made <c>XGraphicsPdfRenderer.DrawImage</c>'s own placement
        /// scale (<c>destRect.Width / image.PointWidth</c>) divide the whole tile - BBox AND the
        /// already-correctly-scaled content inside it together - by <c>PixelsPerPoint</c> A SECOND TIME,
        /// visibly shrinking and mispositioning every box painted through the tile path (opacity&lt;1,
        /// and now filter/mix-blend-mode) relative to any sibling painted directly (which never went
        /// through a tile and so was never subject to the second division) - see
        /// .claude/recent-fixes/ for the full repro this was found from. Asserting the placement <c>cm</c>
        /// immediately before the filtered box's own <c>Do</c> is an identity scale is what actually
        /// catches a regression here: <c>PaintWithOpacity</c> always sizes its tile to exactly the same
        /// rect it later composites with (the whole page-visible clip), so that <c>cm</c> must be
        /// identity regardless of <c>PixelsPerPoint</c> - before the fix it read <c>1/PixelsPerPoint</c>
        /// (0.5 here) instead.
        /// </summary>
        [Fact]
        public async Task ShrinkToFitRescale_FilteredBoxTilePlacement_StaysIdentityScale()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div style="width: 60pt; height: 60pt; background:#ff0000; filter: brightness(1.5);"></div>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                ShrinkToFit = true,
                // Forces a real rescale (PixelsPerPoint = MinContentWidth / page content width = 2,
                // independent of whether this HTML's own content happens to overflow) without relying on
                // a fragile "wide enough to overflow" fixture - PdfGenerator.cs's own
                // minPixelsPerPoint/candidatePixelsPerPoint computation floors the effective
                // PixelsPerPoint at MinContentWidth / PageSize.Width.
                MinContentWidth = 1200,
            };
            config.SetMargins(20);

            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var pdfText = Encoding.Latin1.GetString(ms.ToArray());

            var match = CmThenGsThenDoWithValues.Match(pdfText);
            Assert.True(match.Success, $"Expected a 'cm /GSn gs /Fmn Do' sequence in the content stream:\n{pdfText}");

            var a = double.Parse(match.Groups["a"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var d = double.Parse(match.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture);

            Assert.True(Math.Abs(a - 1.0) < 0.01, $"Expected the tile's placement cm x-scale to be ~1 (identity - the tile is always sized to exactly the rect it's composited with), was {a}");
            Assert.True(Math.Abs(d - 1.0) < 0.01, $"Expected the tile's placement cm y-scale to be ~1 (identity - the tile is always sized to exactly the rect it's composited with), was {d}");
        }

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }
    }
}
