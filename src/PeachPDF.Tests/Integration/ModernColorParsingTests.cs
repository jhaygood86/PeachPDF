using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Color 4/5 function forms the CSS-OM color converter doesn't handle - <c>oklch()</c>,
    /// <c>oklab()</c>, <c>lab()</c>, <c>lch()</c>, and <c>color-mix()</c> - now resolve to real sRGB
    /// colors instead of silently falling back to black. (These are what a Tailwind v4 default palette
    /// and its opacity modifiers are authored in.)
    /// </summary>
    public class ModernColorParsingTests
    {
        private static RColor Parse(string value) => new CssValueParser(new PdfSharpAdapter()).GetActualColor(value);

        // ── hsl()/hwb() as solid colors (previously stack-overflowed at resolve time) ──

        [Theory]
        [InlineData("hsl(120, 100%, 50%)")]
        [InlineData("hsl(120 100% 50%)")]
        public void Hsl_ResolvesToGreen(string value)
        {
            var c = Parse(value);
            Assert.Equal(0, c.R);
            Assert.Equal(255, c.G);
            Assert.Equal(0, c.B);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void Hsla_SlashAndCommaAlpha_AreApplied()
        {
            Assert.InRange(Parse("hsla(0, 100%, 50%, 0.5)").A, 127, 128);
            Assert.InRange(Parse("hsl(0 100% 50% / 50%)").A, 127, 128);
        }

        [Fact]
        public void Hwb_ResolvesToPrimary()
        {
            var c = Parse("hwb(240 0% 0%)");
            Assert.Equal(0, c.R);
            Assert.Equal(0, c.G);
            Assert.Equal(255, c.B);
        }

        [Fact]
        public void Gray_Resolves()
        {
            var c = Parse("gray(128)");
            Assert.Equal(128, c.R);
            Assert.Equal(128, c.G);
            Assert.Equal(128, c.B);
        }

        [Theory]
        [InlineData("oklch(0.7 0.15 0.25turn)")] // 0.25turn == 90deg
        [InlineData("oklch(0.7 0.15 100grad)")]  // 100grad == 90deg
        [InlineData("oklch(0.7 0.15 1.5707963rad)")] // ~90deg
        public void Oklch_HueAngleUnits_MatchDegrees(string value)
        {
            var expected = Parse("oklch(0.7 0.15 90deg)");
            var c = Parse(value);
            Assert.InRange(c.R, expected.R - 1, expected.R + 1);
            Assert.InRange(c.G, expected.G - 1, expected.G + 1);
            Assert.InRange(c.B, expected.B - 1, expected.B + 1);
        }

        [Fact]
        public void NoneComponents_ResolveToZero()
        {
            // `none` resolves to 0: oklab(1 none none) == oklab(1 0 0) == white.
            var c = Parse("oklab(1 none none)");
            Assert.InRange(c.R, 252, 255);
            Assert.InRange(c.G, 252, 255);
            Assert.InRange(c.B, 252, 255);
        }

        [Fact]
        public void ColorMix_InPolarSpace_WithHueMethod_Resolves()
        {
            // A polar interpolation space plus an explicit hue-interpolation method.
            var c = Parse("color-mix(in oklch longer hue, oklch(0.7 0.2 20), oklch(0.7 0.2 200))");
            Assert.False(c.R == 0 && c.G == 0 && c.B == 0, "expected a real mixed color, not black");
        }

        // ── Lightness extremes (exact, space-independent) ─────────────────────────

        [Theory]
        [InlineData("oklab(0 0 0)")]
        [InlineData("lab(0 0 0)")]
        [InlineData("oklch(0 0 0)")]
        [InlineData("lch(0 0 0)")]
        public void Lightness0_IsBlack(string value)
        {
            var c = Parse(value);
            Assert.Equal(0, c.R);
            Assert.Equal(0, c.G);
            Assert.Equal(0, c.B);
            Assert.Equal(255, c.A);
        }

        [Theory]
        [InlineData("oklab(1 0 0)")]
        [InlineData("lab(100 0 0)")]
        [InlineData("oklch(1 0 0)")]
        [InlineData("lch(100 0 0)")]
        public void MaxLightness_NoChroma_IsWhite(string value)
        {
            var c = Parse(value);
            Assert.InRange(c.R, 252, 255);
            Assert.InRange(c.G, 252, 255);
            Assert.InRange(c.B, 252, 255);
        }

        // ── Chroma / hue actually take effect (not a no-op) ───────────────────────

        [Fact]
        public void Oklch_RedHue_IsReddish()
        {
            // ~sRGB red sits near oklch(0.63 0.26 29deg); assert the channel ordering, not exact bytes.
            var c = Parse("oklch(0.63 0.26 29)");
            Assert.True(c.R > c.G && c.R > c.B, $"expected reddish, got {c.R},{c.G},{c.B}");
            Assert.True(c.R > 200, $"expected a strong red channel, got R={c.R}");
        }

        [Fact]
        public void Oklch_PercentAndAngleUnits_AreHonored()
        {
            // 70% lightness == 0.7; 240deg is a blue-ish hue.
            var c = Parse("oklch(70% 0.15 240deg)");
            Assert.True(c.B > c.R, $"expected blue-dominant, got {c.R},{c.G},{c.B}");
        }

        [Fact]
        public void Oklch_SlashAlpha_IsApplied()
        {
            var c = Parse("oklch(0 0 0 / 0.5)");
            Assert.InRange(c.A, 127, 128);
        }

        // ── color-mix() ───────────────────────────────────────────────────────────

        [Fact]
        public void ColorMix_Srgb_WhiteBlack_IsMidGray()
        {
            var c = Parse("color-mix(in srgb, white, black)");
            Assert.InRange(c.R, 127, 128);
            Assert.InRange(c.G, 127, 128);
            Assert.InRange(c.B, 127, 128);
            Assert.Equal(255, c.A);
        }

        [Fact]
        public void ColorMix_Srgb_RedBlue_IsPurple()
        {
            var c = Parse("color-mix(in srgb, red, blue)");
            Assert.InRange(c.R, 127, 128);
            Assert.Equal(0, c.G);
            Assert.InRange(c.B, 127, 128);
        }

        [Fact]
        public void ColorMix_Srgb_WeightedPercentages_ShiftTowardHeavierColor()
        {
            // 25% white + (implicit) 75% black -> 0.25 gray.
            var c = Parse("color-mix(in srgb, white 25%, black)");
            Assert.InRange(c.R, 63, 64);
        }

        [Fact]
        public void ColorMix_WithTransparent_IsOpacityModifier()
        {
            // The Tailwind v4 opacity-modifier shape: color at 50%, mixed with transparent -> same
            // color at half alpha.
            var c = Parse("color-mix(in oklab, blue 50%, transparent)");
            Assert.True(c.B > 250, $"expected blue preserved, got B={c.B}");
            Assert.InRange(c.R, 0, 4);
            Assert.InRange(c.G, 0, 4);
            Assert.InRange(c.A, 126, 129);
        }

        [Fact]
        public void ColorMix_PercentageBeforeColor_IsAccepted()
        {
            // Per CSS Color 5 §3.1 the percentage may precede the color; `30% white` == `white 30%`.
            var before = Parse("color-mix(in srgb, 30% white, black)");
            var after = Parse("color-mix(in srgb, white 30%, black)");
            Assert.Equal(after.R, before.R);
            Assert.Equal(after.G, before.G);
            Assert.Equal(after.B, before.B);
            Assert.InRange(before.R, 76, 77); // 0.30 gray
        }

        [Fact]
        public void ColorMix_PercentagesBelow100_ScaleResultAlpha()
        {
            // 30% red + 30% blue: components mix 50/50, but the 60% total scales alpha to 0.6.
            var c = Parse("color-mix(in srgb, red 30%, blue 30%)");
            Assert.InRange(c.A, 152, 154); // 0.6 * 255 ≈ 153
        }

        [Fact]
        public void ColorMix_WithNestedOklchOperand_Resolves()
        {
            // The Tailwind v4 opacity-modifier shape with an oklch operand (rather than a plain named
            // color): the nested function must resolve through the full render-layer resolver.
            var c = Parse("color-mix(in oklab, oklch(0.62 0.2 265) 50%, transparent)");
            Assert.True(c.B > c.R, $"expected blue-dominant, got {c.R},{c.G},{c.B}");
            Assert.InRange(c.A, 126, 129);
        }

        // ── End-to-end: the value survives the cascade and paints ────────────────

        [Fact]
        public async Task Oklch_ViaBackgroundShorthand_ExpandsToBackgroundColor()
        {
            // Regression: the `background` shorthand must accept a CSS Color 4/5 function and carry it to
            // the background-color longhand (a strict-only Layer A color grammar dropped it, leaving the
            // box with no background).
            var html = "<!DOCTYPE html><html><head><style>div { background: oklch(0.63 0.26 29); }</style></head><body><div>x</div></body></html>";
            var box = await BuildBox(html, "div");
            Assert.NotEqual("transparent", box.BackgroundColor);
            Assert.False(string.IsNullOrEmpty(box.BackgroundColor));
            // And the carried value resolves to a real (reddish) color, not black.
            var resolved = new CssValueParser(new PdfSharpAdapter()).GetActualColor(box.BackgroundColor);
            Assert.True(resolved.R > resolved.G && resolved.R > resolved.B, $"expected reddish, got {resolved.R},{resolved.G},{resolved.B}");
        }

        private static async Task<CssBox> BuildBox(string html, string tag)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);
            var box = DomUtils.GetBoxByTagName(container.Root!, tag);
            Assert.NotNull(box);
            return box!;
        }

        [Fact]
        public async Task Oklch_Color_ResolvesOnElement_NotBlack()
        {
            var html = "<!DOCTYPE html><html><head><style>p { color: oklch(0.63 0.26 29); }</style></head><body><p>x</p></body></html>";
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var p = DomUtils.GetBoxByTagName(container.Root!, "p");
            Assert.NotNull(p);
            Assert.NotEqual("rgb(0, 0, 0)", p!.Color);
        }
    }
}
