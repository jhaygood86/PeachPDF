using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Proves the generated <see cref="SvgPropertyRegistry"/> (built from css-properties.json by
    /// PeachPDF.SourceGenerators) behaves identically to the hand-written <c>SvgValueParsers</c> calls
    /// that <c>SvgTreeBuilder.ApplyCommon</c> still uses in production. <see cref="SvgPropertyRegistry"/>
    /// currently runs *alongside* the hand-written SVG dispatch (nothing production calls it yet), so
    /// this is the actual evidence of "zero production behavior change" ahead of the eventual SVG
    /// cutover — see CLAUDE.md's generator section. The HTML side of this equivalence proof no longer
    /// needs a dedicated test file: since the HTML cutover, <c>CssUtils.SetPropertyValue</c>/
    /// <c>GetPropertyValue</c> forward directly to <see cref="PeachPDF.Html.Core.Utils.CssPropertyRegistry"/>,
    /// so <c>CssUtilsTests</c> exercising <c>CssUtils</c>'s public API already covers it end-to-end.
    /// </summary>
    public class SvgPropertyRegistryEquivalenceTests
    {
        [Theory]
        [InlineData("red")]
        [InlineData("#336699")]
        [InlineData("none")]
        [InlineData("currentColor")]
        [InlineData("not-a-color")]
        public void Fill_Matches_SvgValueParsers(string value)
        {
            var adapter = new PdfSharpAdapter();
            var contextColor = RColor.Black;

            var expectedApplies = SvgValueParsers.TryParsePaint(value, adapter, contextColor, out var expectedPaint);

            var element = new SvgGroupElement();
            var ctx = new SvgPropertyContext(adapter, contextColor);
            var applied = SvgPropertyRegistry.TrySet(element, "fill", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedPaint, element.Fill);
        }

        [Theory]
        [InlineData("butt")]
        [InlineData("round")]
        [InlineData("square")]
        [InlineData("BUTT")]
        [InlineData("bogus")]
        public void StrokeLinecap_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseLineCap(value, out var expectedCap);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-linecap", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedCap, element.StrokeLineCap);
        }
    }
}
