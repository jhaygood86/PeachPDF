using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.PdfSharpCore.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for three real bugs found while chasing why the real Acid2 fixture's
    /// alternate-stylesheet `.picture { background: none; }` override (meant to cancel an earlier
    /// `.picture { background: red; }`) never took visual effect:
    ///
    /// 1. <see cref="PeachPDF.CSS.ShorthandProperty.Export"/> left a longhand the shorthand's own
    ///    grammar didn't match any tokens for (e.g. background-color when only "none" was given)
    ///    with no declared value at all, rather than resetting it to its CSS-spec initial value -
    ///    so a later "background: none" never actually competed against an earlier same-specificity
    ///    "background: red" for the color sub-property, and the earlier rule silently won.
    /// 2. <see cref="CssValueParser.IsColorValid"/>/<c>GetActualColor</c> re-implemented CSS named-
    ///    color lookup via the render adapter, using "alpha &gt; 0" as its own "name not recognized"
    ///    signal - which also incorrectly rejected the one real color whose correct alpha is
    ///    genuinely 0: "transparent" itself.
    /// 3. Fixing bug 1 by giving the omitted longhand a real "initial" sentinel value initially also
    ///    leaked that literal word into shorthand re-serialization (e.g. "border: 1px outset" round-
    ///    tripping as "border: 1px outset initial").
    /// </summary>
    public class ShorthandResetAndColorParsingTests
    {
        [Fact]
        public void IsColorValid_Transparent_ReturnsTrue()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            Assert.True(parser.IsColorValid("transparent"));
        }

        [Fact]
        public void GetActualColor_Transparent_ResolvesToZeroAlpha()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("transparent");
            Assert.Equal(0, color.A);
        }

        // ─── CSS Color 4 space-separated rgb()/rgba() with slash-alpha ────────────

        [Fact]
        public void Rgb_SpaceSeparated_SlashPercentAlpha_FullyOpaque()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgb(0 0 0 / 100%)");
            Assert.Equal(255, color.A);
            Assert.Equal(0, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(0, color.B);
        }

        [Fact]
        public void Rgba_SpaceSeparated_SlashPercentAlpha_HalfAlpha()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgba(255 0 0 / 50%)");
            Assert.Equal(255, color.R);
            Assert.Equal(0, color.G);
            Assert.Equal(0, color.B);
            // 50% of 255 ≈ 128 (rounded).
            Assert.InRange((int)color.A, 127, 128);
        }

        [Fact]
        public void Rgb_SpaceSeparated_SlashNumberAlpha_HalfAlpha()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgb(10 20 30 / 0.5)");
            Assert.Equal(10, color.R);
            Assert.Equal(20, color.G);
            Assert.Equal(30, color.B);
            Assert.InRange((int)color.A, 127, 128);
        }

        [Fact]
        public void Rgb_SpaceSeparated_ThreeValues_NoAlpha_IsOpaque()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgb(245 245 245)");
            Assert.Equal(255, color.A);
            Assert.Equal(245, color.R);
            Assert.Equal(245, color.G);
            Assert.Equal(245, color.B);
        }

        [Fact]
        public void Rgba_LegacyCommaForm_StillParses()
        {
            // Regression guard: the legacy comma form with a decimal alpha must keep working.
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgba(240, 50, 50, 0.75)");
            Assert.Equal(240, color.R);
            Assert.Equal(50, color.G);
            Assert.Equal(50, color.B);
            // 0.75 * 255 ≈ 191.
            Assert.InRange((int)color.A, 190, 192);
        }

        [Fact]
        public void Rgb_LegacyCommaForm_NoAlpha_IsOpaque()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());
            var color = parser.GetActualColor("rgb(255, 0, 0)");
            Assert.Equal(255, color.A);
            Assert.Equal(255, color.R);
        }

        [Fact]
        public async Task BackgroundNoneShorthand_OverridesEarlierBackgroundColor_AtEqualSpecificity()
        {
            // Mirrors the real Acid2 fixture: ".picture { background: red }" (main stylesheet) then
            // ".picture { background: none }" (a later, equal-specificity rule) - the later rule must
            // win per normal cascade/source-order rules, resetting background-color to transparent,
            // not leaving the earlier rule's red untouched.
            var html = "<!DOCTYPE html><html><head><style>"
                + ".target { background: red; } .target { background: none; }"
                + "</style></head><body><div class=\"target\">x</div></body></html>";

            var target = await FindByClassAsync(html, "target");
            Assert.Equal("transparent", target.BackgroundColor);
        }

        [Fact]
        public async Task BackgroundShorthand_LaterRuleStillOverridesColorNormally()
        {
            // Sanity check that the fix didn't disturb the ordinary (non-"none") override case.
            var html = "<!DOCTYPE html><html><head><style>"
                + ".target { background: red; } .target { background: blue; }"
                + "</style></head><body><div class=\"target\">x</div></body></html>";

            var target = await FindByClassAsync(html, "target");
            Assert.Equal("rgb(0, 0, 255)", target.BackgroundColor);
        }

        [Fact]
        public void BorderShorthand_Serializes_WithoutLeakingInitialSentinel()
        {
            // Regression for the serialization side-effect (bug 3): the color sub-part, omitted from
            // "border: 1px outset", must not round-trip as a literal "initial" token.
            var parser = new StylesheetParser();
            var sheet = parser.Parse(".t { border: 1px outset }");
            Assert.Equal(".t { border: 1px outset }", sheet.ToCss());
        }


        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> FindByClassAsync(string html, string className)
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
            return FindByClass(container.Root!, className)!;
        }

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var val = box.HtmlTag?.TryGetAttribute("class", "");
            if (val != null && val.Split(' ').Contains(className))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }
            return null;
        }
    }
}
