using PeachPDF.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for <c>font-variant-numeric</c>: parses/cascades all 8 keywords and
    /// activates real GSUB substitution on a font that has the corresponding tag - Source Sans 3
    /// (confirmed via direct byte inspection to carry real <c>onum</c>/<c>pnum</c>/<c>frac</c>/
    /// <c>ordn</c>/<c>zero</c> data, though not <c>lnum</c>/<c>tnum</c>/<c>afrc</c>). No capability
    /// gating/synthesis exists for this property (unlike caps) - a tag the font lacks is simply inert.
    /// </summary>
    public class FontVariantNumericIntegrationTests
    {
        [Fact]
        public async Task OldstyleNums_ActuallySubstitutesGlyphs_OnAFontWithOnum()
        {
            // Source Sans 3 has real "onum" GSUB data (confirmed via direct byte inspection) - proves
            // the resolved feature reaches real glyph substitution, not just CSS parsing/cascading.
            // Compares actual glyph indices (unambiguous) rather than measured width, since a
            // well-designed font's oldstyle figures aren't guaranteed to differ in advance width from
            // the default ones.
            var box = await FindWordsBox("<b id=\"w\" style=\"font-variant-numeric:oldstyle-nums\">0123456789</b>");
            var descriptor = ((PeachPDF.Adapters.FontAdapter)box.ActualFont).Font.Descriptor;

            var defaultShaped = descriptor.Shape("0123456789", TextShapingFeatures.Default);
            var oldstyleShaped = descriptor.Shape("0123456789", box.ActualTextShapingFeatures);

            Assert.NotEqual(
                defaultShaped.Select(g => g.GlyphIndex),
                oldstyleShaped.Select(g => g.GlyphIndex));
        }

        [Theory]
        [InlineData("lining-nums", "LiningNums")]
        [InlineData("oldstyle-nums", "OldstyleNums")]
        [InlineData("proportional-nums", "ProportionalNums")]
        [InlineData("tabular-nums", "TabularNums")]
        [InlineData("diagonal-fractions", "DiagonalFractions")]
        [InlineData("stacked-fractions", "StackedFractions")]
        [InlineData("ordinal", "Ordinal")]
        [InlineData("slashed-zero", "SlashedZero")]
        public async Task SingleAxisKeyword_ResolvesToTheMatchingFlag(string keyword, string expectedFlagName)
        {
            var box = await FindWordsBox($"<b id=\"w\" style=\"font-variant-numeric:{keyword}\">1234</b>");

            Assert.Equal(expectedFlagName, box.ActualTextShapingFeatures.Numeric.ToString());
        }

        [Fact]
        public async Task MultiAxisCombination_CombinesFlags()
        {
            var box = await FindWordsBox(
                "<b id=\"w\" style=\"font-variant-numeric:oldstyle-nums slashed-zero\">1234</b>");

            var numeric = box.ActualTextShapingFeatures.Numeric;
            Assert.True(numeric.HasFlag(NumericFeatures.OldstyleNums));
            Assert.True(numeric.HasFlag(NumericFeatures.SlashedZero));
        }

        [Fact]
        public async Task UnsupportedTag_OnThisFont_IsSilentlyInert_NotAnException()
        {
            // Neither bundled font has lnum - no capability gate exists for numeric (unlike caps), so
            // requesting it must simply produce zero active lookups, never throw or affect layout.
            var box = await FindWordsBox("<b id=\"w\" style=\"font-variant-numeric:lining-nums\">1234</b>");

            Assert.Equal("LiningNums", box.ActualTextShapingFeatures.Numeric.ToString());
            Assert.Single(box.Words);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static readonly string FontFaceBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));

        private static string Wrap(string body) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SS3Num'; src: url('data:font/truetype;base64,{FontFaceBase64}') format('truetype'); }}
body {{ font-family: 'SS3Num'; width: 400px; }}
</style></head><body>{body}</body></html>";

        private static async Task<CssBox> FindWordsBox(string body)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(Wrap(body), null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            var element = FindById(container.Root!, "w")!;
            if (element.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
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

        private static GraphicsAdapter MeasureGraphics()
        {
            var adapter = new PdfSharpAdapter();
            var size = new XSize(595, 842);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            return new GraphicsAdapter(adapter, measure, 1.0);
        }
    }
}
