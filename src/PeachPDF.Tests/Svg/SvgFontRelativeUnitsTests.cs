using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Font-relative units (CSS Values and Units 4 §6.1) in SVG: <c>em</c>/<c>rem</c> follow the element's own /
    /// the root's <c>font-size</c> instead of a fixed 16px, and <c>ex</c>/<c>ch</c>/<c>cap</c>/<c>ic</c>/<c>lh</c>
    /// (with their root-element variants) are measured from the used font. Source Code Pro is monospace, so its
    /// "0" advance is exactly 0.6em and the expected geometry is literal.
    /// </summary>
    public class SvgFontRelativeUnitsTests
    {
        private const string Mono = "SvgTestMono";

        private static async Task<SvgDocument> Build(string markup)
        {
            var adapter = new PdfSharpAdapter();
            await BundledFonts.RegisterFont(adapter, BundledFonts.Otf, Mono);
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), adapter);
        }

        private static string Svg(string attributes, string body) =>
            $"""<svg xmlns="http://www.w3.org/2000/svg" font-family="{Mono}" {attributes}>{body}</svg>""";

        private static SvgRectElement Rect(SvgDocument document) => Assert.IsType<SvgRectElement>(document.Children.First());

        [Fact]
        public async Task Ch_IsTheAdvanceOfTheZeroGlyph()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="10ch" height="1"/>"""));
            Assert.Equal(120.0, Rect(document).Width, 6); // 10 * 0.6em * 20
        }

        [Fact]
        public async Task Ch_UsesTheElementsOwnFontSize()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="10ch" height="1" font-size="10"/>"""));
            Assert.Equal(60.0, Rect(document).Width, 6);
        }

        [Fact]
        public async Task Em_FollowsTheDeclaredFontSize_InsteadOfAFixed16()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="2em" height="1"/>"""));
            Assert.Equal(40.0, Rect(document).Width, 6);
        }

        [Fact]
        public async Task Em_WithNoFontSizeDeclaredAnywhere_KeepsTheInitial16()
        {
            var document = await Build(Svg("", """<rect width="2em" height="1"/>"""));
            Assert.Equal(32.0, Rect(document).Width, 6);
        }

        [Fact]
        public async Task Rem_FollowsTheRootFontSize_NotTheGroups()
        {
            var document = await Build(Svg("font-size=\"40\"", """<g font-size="10"><rect width="1rem" height="1"/></g>"""));
            var group = Assert.IsType<SvgGroupElement>(document.Children.First());
            Assert.Equal(40.0, Assert.IsType<SvgRectElement>(group.Children.First()).Width, 6);
        }

        [Fact]
        public async Task RootVariants_MeasureTheRootsFont()
        {
            var document = await Build(Svg("font-size=\"40\"", """<g font-size="10"><rect width="1rch" height="1"/></g>"""));
            var group = Assert.IsType<SvgGroupElement>(document.Children.First());
            Assert.Equal(24.0, Assert.IsType<SvgRectElement>(group.Children.First()).Width, 6); // 0.6 * 40, not 0.6 * 10
        }

        [Fact]
        public async Task Ex_Cap_AndLh_AreMeasuredFromTheFont()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="10ex" height="10cap" x="1lh"/>"""));
            var rect = Rect(document);
            var font = (await Build(Svg("font-size=\"20\"", "<text>Hi</text>"))).Children.OfType<SvgTextElement>().First().Font!;

            Assert.Equal(200 * FontMetricMeasurement.Ratio(font, FontMetric.Ex), rect.Width, 4);
            Assert.Equal(200 * FontMetricMeasurement.Ratio(font, FontMetric.Cap), rect.Height, 4);
            Assert.Equal(20 * FontMetricMeasurement.Ratio(font, FontMetric.Lh), rect.X, 4);
            Assert.NotEqual(100.0, rect.Width, 1); // not the 0.5em fallback
        }

        [Fact]
        public async Task Ic_WithNoIdeographInTheFont_IsOneEm()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="3ic" height="1"/>"""));
            Assert.Equal(60.0, Rect(document).Width, 6);
        }

        [Fact]
        public async Task Calc_ResolvesEmAndMeasuredUnitsAgainstTheElementsFont()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="calc(1em + 2ch)" height="calc(50% + 1px)"/>"""));
            Assert.Equal(20 + 2 * 12, Rect(document).Width, 6);
        }

        [Fact]
        public async Task StrokeWidth_CalcEm_ResolvesAgainstTheElementsFontSize()
        {
            // A calc() em term used to resolve against a fixed 16px whatever the element's own font-size.
            var document = await Build(Svg("font-size=\"20\"", """<rect width="1" height="1" stroke="black" stroke-width="calc(1em + 2px)"/>"""));
            Assert.Equal(22.0, Rect(document).StrokeWidth, 6);
        }

        [Fact]
        public async Task StrokeWidth_PlainEm_AndDashArray_FollowTheElementsFontSize()
        {
            var document = await Build(Svg("font-size=\"20\"", """<rect width="1" height="1" stroke="black" stroke-width="0.5em" stroke-dasharray="1ch 2ch"/>"""));
            var rect = Rect(document);
            Assert.Equal(10.0, rect.StrokeWidth, 6);
            Assert.Equal([12.0, 24.0], rect.StrokeDashArray);
        }

        [Fact]
        public async Task Text_FontSizeInCh_UsesTheParentsGlyphWidth()
        {
            var document = await Build(Svg("font-size=\"20\"", """<text font-size="10ch">Hi</text>"""));
            Assert.Equal(120.0, document.Children.OfType<SvgTextElement>().First().Font!.Size, 3);
        }

        [Fact]
        public async Task Text_LetterSpacingInCh_UsesTheElementsOwnFont()
        {
            var document = await Build(Svg("font-size=\"20\"", """<text letter-spacing="1ch">Hi</text>"""));
            Assert.Equal(12.0, document.Children.OfType<SvgTextElement>().First().LetterSpacing, 3);
        }

        [Fact]
        public async Task RootVariants_OnTheRootElementItself_MeasureTheRootsFont()
        {
            var document = await Build(Svg("font-size=\"20\" width=\"10rch\"", """<rect width="1" height="1" stroke="black" stroke-width="1rch"/>"""));
            Assert.Equal(120.0, document.Width!.Value, 6); // 10 * 0.6em * 20 - not the 0.5em fallback
        }

        [Fact]
        public async Task Tspan_ResolvesItsLengthsAgainstItsOwnFontSize()
        {
            var document = await Build(Svg("", """<text font-size="10" x="0" y="0"><tspan font-size="30" dy="1em">Hi</tspan></text>"""));
            var tspan = document.Children.OfType<SvgTextElement>().First().Content.OfType<SvgTextSpan>().First().Run;
            Assert.Equal(30.0, tspan.Dy, 6); // the tspan's own 30, not the enclosing text's 10
        }

        [Fact]
        public async Task Lh_UnderNonDefaultPixelsPerPoint_IsTheFontsNormalLineHeightInUserUnits()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 96.0 / 72.0 };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Otf, Mono);
            var root = XDocument.Parse(Svg("font-size=\"20\"", """<rect x="1lh" width="1" height="1"/><text>Hi</text>""")).Root!;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), adapter);

            // The font was requested at 20 user units, so its normal line height in user units is that font's
            // own NormalLineHeight whatever PixelsPerPoint is - not that divided or multiplied by it.
            var font = document.Children.OfType<SvgTextElement>().First().Font!;
            Assert.Equal(font.NormalLineHeight, Rect(document).X, 4);
        }

        [Fact]
        public async Task RootWidth_InCh_ResolvesAgainstTheRootsOwnFont()
        {
            var document = await Build(Svg("font-size=\"20\" width=\"10ch\"", "<rect width=\"1\" height=\"1\"/>"));
            Assert.Equal(120.0, document.Width!.Value, 6);
        }

        [Theory]
        [InlineData("2ex", 0.5)]
        [InlineData("2ch", 0.5)]
        [InlineData("2cap", 0.7)]
        [InlineData("2ic", 1.0)]
        [InlineData("2lh", 1.2)]
        [InlineData("2rex", 0.5)]
        [InlineData("2rch", 0.5)]
        [InlineData("2rcap", 0.7)]
        [InlineData("2ric", 1.0)]
        [InlineData("2rlh", 1.2)]
        public void ParseLength_WithNoBasis_TakesTheSpecFallbackAgainst16px(string value, double ratio)
        {
            Assert.Equal(16.0 * ratio * 2, SvgValueParsers.ParseLength(value)!.Value, 6);
        }

        [Theory]
        [InlineData("1em", 16.0)]
        [InlineData("1rem", 16.0)]
        [InlineData("2rem", 32.0)]
        public void ParseLength_EmAndRemWithNoBasis_StayAtTheInitial16px(string value, double expected)
        {
            Assert.Equal(expected, SvgValueParsers.ParseLength(value));
        }

        [Theory]
        [InlineData("1remx")]
        [InlineData("chch")]
        [InlineData("ex")]
        public void ParseLength_MalformedMeasuredUnit_IsNull(string value)
        {
            Assert.Null(SvgValueParsers.ParseLength(value));
        }
    }
}
