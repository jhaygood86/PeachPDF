using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The font-relative units that are measured from the used font rather than being a multiple of
    /// its size (CSS Values and Units 4 §6.1): <c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/<c>lh</c> and their
    /// root-element counterparts. Asserts the laid-out box geometry, not just that layout completes.
    /// <see cref="BundledFonts.Otf"/> (Source Code Pro) is used because it is monospace, so its "0" advance is
    /// exactly 0.6em - deliberately not the 0.5em fallback - and the expected widths are literal.
    /// </summary>
    public class FontRelativeUnitsIntegrationTests
    {
        private const string Mono = "TestMono";
        private const string Sans = "TestSans";

        private static async Task<(CssBox Root, HtmlContainerInt Container)> Layout(string body, string css = "", double pixelsPerPoint = 1.0)
        {
            var html = $"<html><head><style>body {{ margin:0; font-family:{Mono}; font-size:20pt; }} {css}</style></head><body>{body}</body></html>";
            var adapter = new PdfSharpAdapter { PixelsPerPoint = pixelsPerPoint };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Otf, Mono);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Sans);
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, pixelsPerPoint);
            await container.PerformLayout(graphics);
            return (container.Root!, container);
        }

        private static CssBox ById(CssBox box, string id)
        {
            if (box.GetAttribute("id", null) == id) return box;
            foreach (var child in box.Boxes)
                if (ById(child, id) is { } found) return found;
            return null!;
        }

        private static async Task<CssBox> Box(string body, string id = "t", string css = "", double pixelsPerPoint = 1.0)
        {
            var (root, _) = await Layout(body, css, pixelsPerPoint);
            var box = ById(root, id);
            Assert.NotNull(box);
            return box;
        }

        [Fact]
        public async Task Ch_IsTheAdvanceOfTheZeroGlyph_NotHalfAnEm()
        {
            // Source Code Pro's "0" advances 600/1000 em: 10ch at 20pt is 120pt, not the 0.5em fallback's 100pt.
            var box = await Box("<div id='t' style='width:10ch'></div>");
            Assert.Equal(120.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Ch_ScalesWithTheElementsOwnFontSize()
        {
            var box = await Box("<div id='t' style='width:10ch; font-size:10pt'></div>");
            Assert.Equal(60.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Ch_UsesTheBoxsFontFamily()
        {
            // Source Sans 3's "0" is not 0.6em wide - the measurement follows font-family, not a constant.
            var mono = await Box("<div id='t' style='width:10ch'></div>");
            var sans = await Box($"<div id='t' style='width:10ch; font-family:{Sans}'></div>");
            Assert.NotEqual(mono.Size.Width, sans.Size.Width, 1);
            Assert.InRange(sans.Size.Width, 80, 130);
        }

        [Fact]
        public async Task Ch_UnderNonDefaultPixelsPerPoint_KeepsItsProportionToTheFontSize()
        {
            // The PixelsPerPoint direction is the trap this measurement was called out for: a wrong direction
            // would land off by pixelsPerPoint^2. Width and font-size both live in the same inflated space.
            var box = await Box("<div id='t' style='width:10ch'></div>", pixelsPerPoint: 96.0 / 72.0);
            Assert.Equal(120.0 * 96.0 / 72.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Ex_IsTheFontsRealXHeight()
        {
            var box = await Box("<div id='t' style='width:10ex'></div>");
            var ratio = box.ActualFont.XHeightEm;
            Assert.NotNull(ratio);
            Assert.Equal(10 * 20 * ratio!.Value, box.Size.Width, 1);
            Assert.NotEqual(100.0, box.Size.Width, 1); // not the 0.5em fallback
        }

        [Fact]
        public async Task Cap_IsTheFontsCapHeight()
        {
            var box = await Box("<div id='t' style='width:10cap'></div>");
            var ratio = box.ActualFont.CapHeightEm;
            Assert.NotNull(ratio);
            Assert.Equal(10 * 20 * ratio!.Value, box.Size.Width, 1);
        }

        [Fact]
        public async Task Ic_WithNoIdeographInAnyFont_IsOneEm()
        {
            // Neither bundled test face covers U+6C34, so the spec's 1em fallback applies.
            var box = await Box("<div id='t' style='width:3ic'></div>");
            Assert.Equal(60.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Lh_IsTheUsedLineHeight_ForAUnitlessLineHeight()
        {
            var box = await Box("<div id='t' style='width:2lh; line-height:1.5'></div>");
            Assert.Equal(2 * 1.5 * 20, box.Size.Width, 1);
        }

        [Fact]
        public async Task Lh_IsTheUsedLineHeight_ForALengthLineHeight()
        {
            var box = await Box("<div id='t' style='width:2lh; line-height:30pt'></div>");
            Assert.Equal(60.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Lh_IsTheFontsNormalLineHeight_ForNormal()
        {
            var box = await Box("<div id='t' style='width:1lh'></div>");
            Assert.Equal(box.ActualFont.NormalLineHeight, box.Size.Width, 1);
        }

        [Fact]
        public async Task Lh_InTheLineHeightPropertyItself_IsTheParentsLineHeight()
        {
            // line-height: 2lh on a child means twice the PARENT's line-height (30pt), not a self-reference.
            var (root, _) = await Layout("<div style='line-height:30pt'><div id='t' style='line-height:2lh'></div></div>");
            Assert.Equal(60.0, ById(root, "t").ActualLineHeight, 1);
        }

        [Fact]
        public async Task Lh_InTheLineHeightProperty_DoesNotCompoundDownTheTree()
        {
            // line-height is inherited: the child's 2lh must reach its own descendants as the 60pt it computed to,
            // not as another "2lh" each of them re-resolves against its own parent (which would give 120pt, 240pt).
            var (root, _) = await Layout(
                "<div style='line-height:30pt'><div id='mid' style='line-height:2lh'><span id='leaf'>text</span></div></div>");
            Assert.Equal(60.0, ById(root, "mid").ActualLineHeight, 1);
            Assert.Equal(60.0, ById(root, "leaf").ActualLineHeight, 1);
        }

        [Fact]
        public async Task Lh_InTheLineHeightProperty_UnderNonDefaultPixelsPerPoint_IsTwiceTheParents()
        {
            var (root, _) = await Layout(
                "<div style='line-height:30pt'><div id='t' style='line-height:2lh'></div></div>", pixelsPerPoint: 96.0 / 72.0);
            var parent = ById(root, "t").ParentBox!;
            Assert.Equal(2 * parent.ActualLineHeight, ById(root, "t").ActualLineHeight, 1);
        }

        [Fact]
        public async Task Lh_UnderNonDefaultPixelsPerPoint_MatchesTheLineHeight()
        {
            var box = await Box("<div id='t' style='width:1lh; line-height:30pt'></div>", pixelsPerPoint: 96.0 / 72.0);
            Assert.Equal(box.ActualLineHeight, box.Size.Width, 1);
        }

        [Fact]
        public async Task RootVariants_FollowTheRootElement_NotTheBox()
        {
            // html is 40pt; the div is 10pt. 1rch is the root font's "0" advance (0.6 * 40), not the div's.
            var box = await Box("<div id='t' style='width:1rch; font-size:10pt'></div>", css: "html { font-size:40pt; font-family:" + Mono + "; }");
            Assert.Equal(24.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Rlh_FollowsTheRootsLineHeight()
        {
            var box = await Box("<div id='t' style='width:1rlh; line-height:99pt'></div>", css: "html { line-height:40pt; }");
            Assert.Equal(40.0, box.Size.Width, 1);
        }

        [Fact]
        public async Task Rex_UsesTheRootFont()
        {
            var (root, _) = await Layout("<div id='t' style='width:10rex; font-size:10pt'></div>", "html { font-size:40pt; font-family:" + Mono + "; }");
            var box = ById(root, "t");
            // div -> body -> html: the container's own top box sits above <html> and carries no element.
            var rootRatio = box.ParentBox!.ParentBox!.ActualFont.XHeightEm;
            Assert.NotNull(rootRatio);
            Assert.Equal(10 * 40 * rootRatio!.Value, box.Size.Width, 1);
        }

        [Fact]
        public async Task FontSize_InCh_UsesTheParentsGlyphWidth()
        {
            // font-size: 10ch on the child = ten "0" advances of the PARENT (20pt mono, 12pt each) = 120pt.
            var (root, _) = await Layout("<div id='t' style='font-size:10ch'></div>");
            Assert.Equal(120.0, ById(root, "t").ActualFont.Size, 1);
        }

        [Fact]
        public async Task Calc_MixesMeasuredUnitsWithOtherLengths()
        {
            var box = await Box("<div id='t' style='width:calc(2ch + 10pt)'></div>");
            Assert.Equal(2 * 12 + 10, box.Size.Width, 1);
        }

        [Theory]
        [InlineData("cap", (int)Length.Unit.Cap)]
        [InlineData("ic", (int)Length.Unit.Ic)]
        [InlineData("lh", (int)Length.Unit.Lh)]
        [InlineData("rex", (int)Length.Unit.Rex)]
        [InlineData("rch", (int)Length.Unit.Rch)]
        [InlineData("rcap", (int)Length.Unit.Rcap)]
        [InlineData("ric", (int)Length.Unit.Ric)]
        [InlineData("rlh", (int)Length.Unit.Rlh)]
        public void NewUnits_ParseAsLengths(string suffix, int unit)
        {
            Assert.True(Length.TryParse("2" + suffix, out var length));
            Assert.Equal(unit, (int)length.Type);
            Assert.True(length.IsFontRelative);
        }
    }
}
