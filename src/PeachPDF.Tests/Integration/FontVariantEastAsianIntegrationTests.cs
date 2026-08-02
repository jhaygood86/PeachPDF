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
    /// Regression coverage for <c>font-variant-east-asian</c>'s cascade/resolution into
    /// <see cref="DerivedStyle.ActualFontVariantEastAsian"/>/<see cref="CssBox.ActualTextShapingFeatures"/>.
    /// Neither bundled font (Source Sans 3, Source Code Pro - both Latin-only) carries any of the 9
    /// GSUB tags this property maps to (confirmed via direct byte inspection), so - like
    /// <c>font-variant-numeric</c>'s <c>lining-nums</c> case - every keyword here is exercised only on
    /// its "resolves to the correct flag, silently inert at shaping time" branch; there is no capability
    /// gate for this property (unlike caps), so the flag still resolves correctly regardless of font
    /// support - only the actual glyph substitution (untestable with these bundled fonts) would be a
    /// no-op.
    /// </summary>
    public class FontVariantEastAsianIntegrationTests
    {
        [Theory]
        [InlineData("jis78-forms", "Jis78")]
        [InlineData("jis83-forms", "Jis83")]
        [InlineData("jis90-forms", "Jis90")]
        [InlineData("jis04-forms", "Jis04")]
        [InlineData("simplified", "Simplified")]
        [InlineData("traditional", "Traditional")]
        [InlineData("full-width", "FullWidth")]
        [InlineData("proportional-width", "ProportionalWidth")]
        [InlineData("ruby", "Ruby")]
        public async Task SingleAxisKeyword_ResolvesToTheMatchingFlag(string keyword, string expectedFlagName)
        {
            var box = await FindWordsBox($"<b id=\"w\" style=\"font-variant-east-asian:{keyword}\">Hello</b>");

            Assert.Equal(expectedFlagName, box.ActualTextShapingFeatures.EastAsian.ToString());
        }

        [Fact]
        public async Task MultiAxisCombination_CombinesFlags()
        {
            var box = await FindWordsBox(
                "<b id=\"w\" style=\"font-variant-east-asian:jis83-forms full-width ruby\">Hello</b>");

            var eastAsian = box.ActualTextShapingFeatures.EastAsian;
            Assert.True(eastAsian.HasFlag(EastAsianFeatures.Jis83));
            Assert.True(eastAsian.HasFlag(EastAsianFeatures.FullWidth));
            Assert.True(eastAsian.HasFlag(EastAsianFeatures.Ruby));
        }

        [Fact]
        public async Task UnsupportedByEitherBundledFont_IsSilentlyInert_NotAnException()
        {
            // Neither bundled font has jp78 - no capability gate exists for east-asian (unlike caps), so
            // requesting it must simply produce zero active lookups, never throw or affect layout.
            var box = await FindWordsBox("<b id=\"w\" style=\"font-variant-east-asian:jis78-forms\">Hello</b>");

            Assert.Equal("Jis78", box.ActualTextShapingFeatures.EastAsian.ToString());
            Assert.Single(box.Words);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static readonly string FontFaceBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));

        private static string Wrap(string body) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SS3EastAsian'; src: url('data:font/truetype;base64,{FontFaceBase64}') format('truetype'); }}
body {{ font-family: 'SS3EastAsian'; width: 400px; }}
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
    }
}
