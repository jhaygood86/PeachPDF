using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.GetMarginBoxRect"/> — the sizing step for
    /// @page margin boxes (<c>@top-center</c>, <c>@left-middle</c>, etc.). Issue #155: relative
    /// <c>width</c>/<c>height</c>/<c>min-*</c>/<c>max-*</c> values must resolve per css-page-3 §8
    /// (<c>%</c> against the margin-area dimension the box sits in, <c>em</c>/<c>ex</c> against the
    /// box's own computed font size, <c>rem</c> against the root), not be silently ignored and sized
    /// as <c>auto</c>. Units with no page context (<c>vw</c>/<c>vh</c>/<c>vmin</c>/<c>vmax</c>/<c>ch</c>)
    /// still resolve to <c>auto</c>.
    /// </summary>
    public class MarginBoxRendererSizingTests
    {
        // A 600×800 sheet with 60pt left/right and 40pt top/bottom margins:
        //   contentWidth  = 600 - 60 - 60 = 480 (the span shared by the top/bottom row's three boxes)
        //   contentHeight = 800 - 40 - 40 = 720 (the span shared by a left/right column's three boxes)
        private static readonly XSize Page = new(600, 800);
        private const double ML = 60, MT = 40, MR = 60, MB = 40;
        private const double RemPt = 16;

        [Fact]
        public void PercentWidth_ResolvesAgainstMarginAreaWidth()
        {
            var margins = Margins("@top-center { content: \"x\"; width: 50%; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            // 50% of contentWidth (480) = 240.
            Assert.Equal(240.0, rect.Width, 3);
        }

        [Fact]
        public void EmWidth_ResolvesAgainstBoxOwnFontSize()
        {
            var margins = Margins("@top-center { content: \"x\"; font-size: 20pt; width: 3em; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            // 3em against the box's own 20pt font = 60pt.
            Assert.Equal(60.0, rect.Width, 3);
        }

        [Fact]
        public void PercentHeight_ResolvesAgainstMarginAreaHeight()
        {
            var margins = Margins("@left-middle { content: \"x\"; height: 25%; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("left-middle", Page, ML, MT, MR, MB, margins, null, RemPt);

            // 25% of contentHeight (720) = 180.
            Assert.Equal(180.0, rect.Height, 3);
        }

        [Fact]
        public void ViewportUnitWidth_HasNoPageContext_SizesAsAuto()
        {
            // vw has no page-sheet basis: the declaration is dropped and the box sizes as auto. With
            // all three top-row boxes auto, the span splits into equal thirds (480 / 3 = 160).
            var margins = Margins("@top-center { content: \"x\"; width: 10vw; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            Assert.Equal(160.0, rect.Width, 3);
        }

        [Fact]
        public void RelativeMinWidth_ClampsResolvedWidthUpward()
        {
            // width 5% of 480 = 24, clamped up to min-width 20% of 480 = 96 — exercises the relative
            // min-width path directly.
            var margins = Margins("@top-center { content: \"x\"; width: 5%; min-width: 20%; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            Assert.Equal(96.0, rect.Width, 3);
        }

        [Fact]
        public void RelativeMaxWidth_ClampsResolvedWidthDownward()
        {
            // width 50% of 480 = 240, clamped down to max-width 25% of 480 = 120 — exercises the
            // relative max-width path directly.
            var margins = Margins("@top-center { content: \"x\"; width: 50%; max-width: 25%; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            Assert.Equal(120.0, rect.Width, 3);
        }

        [Fact]
        public void AbsoluteWidth_StillHonored()
        {
            // Regression guard: absolute lengths keep working through the new resolution path.
            var margins = Margins("@top-center { content: \"x\"; width: 120pt; }");

            var rect = MarginBoxRenderer.GetMarginBoxRect("top-center", Page, ML, MT, MR, MB, margins, null, RemPt);

            Assert.Equal(120.0, rect.Width, 3);
        }

        private static IReadOnlyList<MarginStyleRule> Margins(string marginBoxCss) =>
            new StylesheetParser().Parse($"@page {{ {marginBoxCss} }}")
                .Rules.OfType<PageRule>().Single().Margins.ToList();
    }
}
