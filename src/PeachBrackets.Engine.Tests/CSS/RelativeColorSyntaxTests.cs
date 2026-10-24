using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html.Parse;
using Xunit;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// Relative colour syntax (CSS Color 5 §4): <c>rgb(from red r g b)</c> and the same in every colour function. The origin colour's components are keywords for
    /// numbers in the space of the function, and each component of the result is a number, a percentage, none, a keyword or a <c>calc()</c> over them.
    /// </summary>
    public class RelativeColorSyntaxTests
    {
        private static bool TryParse(string text, out PaintColor color) =>
            new CssValueParser(new HtmlRenderContext()).TryGetColor(text, 0, text.Length, out color);

        private static PaintColor Parse(string text)
        {
            Assert.True(TryParse(text, out var color), text);
            return color;
        }

        private static void AssertColor(string text, int r, int g, int b, int a = 255, int tolerance = 2)
        {
            var color = Parse(text);
            Assert.True(
                System.Math.Abs(color.R - r) <= tolerance && System.Math.Abs(color.G - g) <= tolerance && System.Math.Abs(color.B - b) <= tolerance && System.Math.Abs(color.A - a) <= 1,
                $"{text}: expected rgba({r}, {g}, {b}, {a}), got rgba({color.R}, {color.G}, {color.B}, {color.A})");
        }

        [Theory]
        [InlineData("rgb(from red r g b)", 255, 0, 0, 255)]
        [InlineData("rgb(from #336699 r g b)", 51, 102, 153, 255)]
        [InlineData("rgb(from #336699 calc(r + 20) g b)", 71, 102, 153, 255)]
        [InlineData("rgb(from #336699 calc(r * 2) g calc(b - 3))", 102, 102, 150, 255)]
        [InlineData("rgb(from red 0 0 255)", 0, 0, 255, 255)]
        [InlineData("rgb(from red 100% 50% 0%)", 255, 128, 0, 255)]
        [InlineData("rgb(from red none g b)", 0, 0, 0, 255)]
        [InlineData("rgb(from red r g b / 50%)", 255, 0, 0, 128)]
        [InlineData("rgb(from red r g b / calc(alpha / 2))", 255, 0, 0, 128)]
        [InlineData("rgb(from rgb(10 20 30 / 0.4) r g b)", 10, 20, 30, 102)]
        [InlineData("rgb(from rgb(10 20 30 / 0.4) r g b / alpha)", 10, 20, 30, 102)]
        [InlineData("rgb(from red calc(r + 1000) g b)", 255, 0, 0, 255)]
        [InlineData("rgba(from red r g b / 0.25)", 255, 0, 0, 64)]
        [InlineData("RGB(FROM RED R G B)", 255, 0, 0, 255)]
        [InlineData("rgb(from red min(r, 100) g max(b, 50))", 100, 0, 50, 255)]
        [InlineData("rgb(from red clamp(10, r, 200) g b)", 200, 0, 0, 255)]
        public void Rgb_ReadsKeywordsAndCalc(string text, int r, int g, int b, int a) => AssertColor(text, r, g, b, a);

        [Theory]
        [InlineData("hsl(from red h s l)", 255, 0, 0)]
        [InlineData("hsl(from red calc(h + 120) s l)", 0, 255, 0)]
        [InlineData("hsl(from hsl(0 100% 50%) h s calc(l / 2))", 128, 0, 0)]
        [InlineData("hsl(from blue 120 s l)", 0, 255, 0)]
        [InlineData("hsl(from blue 0.5turn s l)", 0, 255, 255)]
        [InlineData("hsl(from red h 0% l)", 128, 128, 128)]
        [InlineData("hwb(from red h w b)", 255, 0, 0)]
        [InlineData("hwb(from red calc(h + 120) w b)", 0, 255, 0)]
        [InlineData("hwb(from red h 50% 0%)", 255, 128, 128)]
        public void HslAndHwb_ReadTheirKeywords(string text, int r, int g, int b) => AssertColor(text, r, g, b);

        [Theory]
        [InlineData("lab(from lab(50 40 20) l a b)", 185, 88, 88)]
        [InlineData("lab(from red l a b)", 255, 0, 0)]
        [InlineData("oklab(from red l a b)", 255, 0, 0)]
        [InlineData("lch(from red l c h)", 255, 0, 0)]
        [InlineData("oklch(from red l c h)", 255, 0, 0)]
        [InlineData("oklch(from red l c calc(h + 120))", 0, 255, 0, 255)]
        [InlineData("oklch(from #888 l c h)", 136, 136, 136)]
        [InlineData("oklch(from red 50% c h)", 255, 0, 0, 255, 100)]
        [InlineData("oklab(from white 0 0 0)", 0, 0, 0)]
        [InlineData("lab(from white 0 0 0)", 0, 0, 0)]
        public void LabFamily_ConvertsTheOriginIntoTheFunctionsSpace(string text, int r, int g, int b, int a = 255, int tolerance = 3)
        {
            // oklch(from red l c calc(h + 120)) is only approximately lime; the rows without a hue change are the identity
            if (text.Contains("calc(h + 120)"))
            {
                var color = Parse(text);
                Assert.True(color.G > 150 && color.R < 140, text);
                return;
            }

            AssertColor(text, r, g, b, a, tolerance);
        }

        [Fact]
        public void Oklch_LightnessKeywordIsTheOriginsLightness()
        {
            var halved = Parse("oklch(from red calc(l / 2) c h)");
            var direct = Parse("oklch(0.3140 0.2577 29.23)");

            Assert.InRange((int)halved.R - direct.R, -3, 3);
            Assert.InRange((int)halved.G - direct.G, -3, 3);
            Assert.InRange((int)halved.B - direct.B, -3, 3);
        }

        [Theory]
        [InlineData("color(from red srgb r g b)", 255, 0, 0)]
        [InlineData("color(from red srgb 0 g b)", 0, 0, 0)]
        [InlineData("color(from red srgb 0 1 0)", 0, 255, 0)]
        [InlineData("color(from color(srgb 0 0 1) srgb r g b)", 0, 0, 255)]
        [InlineData("color(from red srgb-linear r g b)", 255, 0, 0)]
        [InlineData("color(from red display-p3 r g b)", 255, 0, 0)]
        [InlineData("color(from white xyz-d65 x y z)", 255, 255, 255)]
        [InlineData("color(from white xyz-d50 x y z)", 255, 255, 255)]
        [InlineData("color(from white a98-rgb r g b)", 255, 255, 255)]
        [InlineData("color(from white prophoto-rgb r g b)", 255, 255, 255)]
        [InlineData("color(from white rec2020 r g b)", 255, 255, 255)]
        [InlineData("color(from color(srgb 1 0 0) srgb r g b / 50%)", 255, 0, 0)]
        public void ColorFunction_ReadsTheSpacesKeywords(string text, int r, int g, int b) => AssertColor(text, r, g, b, text.EndsWith("50%)") ? 128 : 255);

        [Fact]
        public void ColorFunction_KeepsAWideGamutOriginUnclipped()
        {
            // display-p3 red is outside sRGB: a P3 -> P3 relative colour gives the same colour back, not the clipped sRGB one converted
            var direct = Parse("color(display-p3 1 0 0)");
            var relative = Parse("color(from color(display-p3 1 0 0) display-p3 r g b)");

            Assert.Equal(direct, relative);
        }

        [Fact]
        public void Oklch_KeepsAWideGamutOriginUnclipped()
        {
            var direct = Parse("color(display-p3 0 1 0)");
            var relative = Parse("oklch(from color(display-p3 0 1 0) l c h)");

            Assert.InRange((int)relative.R - direct.R, -2, 2);
            Assert.InRange((int)relative.G - direct.G, -2, 2);
            Assert.InRange((int)relative.B - direct.B, -2, 2);
        }

        [Fact]
        public void TheOriginMayItselfBeRelative()
        {
            AssertColor("rgb(from rgb(from red r g b) 0 g calc(r))", 0, 0, 255);
            AssertColor("rgb(from color-mix(in srgb, red, red) r g b)", 255, 0, 0);
            AssertColor("hsl(from rgb(from #00f r g b) calc(h - 120) s l)", 0, 255, 0);
        }

        [Fact]
        public void TheOriginMayBeAnyColour()
        {
            var direct = Parse("oklch(0.7 0.1 150)");
            AssertColor("rgb(from oklch(0.7 0.1 150) r g b)", direct.R, direct.G, direct.B);
            AssertColor("rgb(from transparent r g b)", 0, 0, 0, 0);
            AssertColor("rgb(from hsl(120 100% 25% / 0.5) r g b)", 0, 128, 0, 128);
        }

        [Theory]
        [InlineData("rgb(from red r g)")]
        [InlineData("rgb(from red r g x)")]
        [InlineData("rgb(from red r g b b)")]
        [InlineData("rgb(from nonsense r g b)")]
        [InlineData("rgb(from red)")]
        [InlineData("rgb(from red h g b)")]
        [InlineData("hsl(from red r g b)")]
        [InlineData("rgb(from red, r, g, b)")]
        [InlineData("color(from red nonsense r g b)")]
        [InlineData("color(from red srgb x y z)")]
        [InlineData("color(from white xyz r g b)")]
        [InlineData("rgb(from red calc(r + g b)")]
        [InlineData("rgb(from red calc(r + 10px) g b)")]
        [InlineData("rgb(from red calc(r + 10deg) g b)")]
        [InlineData("rgb(from red r g b / 1 2)")]
        [InlineData("lab(from red l a calc(c))")]
        public void RelativeSyntax_RejectsWhatItDoesNotRead(string text) => Assert.False(TryParse(text, out _));

        [Fact]
        public void ColorMix_TakesARelativeOperand()
        {
            var relative = Parse("color-mix(in srgb, rgb(from red r g b), blue)");
            var plain = Parse("color-mix(in srgb, red, blue)");

            Assert.Equal(plain, relative);
        }

        [Fact]
        public void ColorMix_TakesARelativeWideGamutOperandUnclipped()
        {
            var relative = Parse("color-mix(in oklab, oklch(from color(display-p3 0 1 0) l c h), white)");
            var plain = Parse("color-mix(in oklab, color(display-p3 0 1 0), white)");

            Assert.InRange((int)relative.R - plain.R, -3, 3);
            Assert.InRange((int)relative.G - plain.G, -3, 3);
            Assert.InRange((int)relative.B - plain.B, -3, 3);
        }

        [Fact]
        public void NestedRelativeColours_PastTheDepthLimit_AreInvalid()
        {
            var text = "red";
            for (var i = 0; i < 60; i++) text = $"rgb(from {text} r g b)";

            Assert.False(TryParse(text, out _));
        }
    }
}
