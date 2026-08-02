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
    /// this is the actual evidence of "zero production behavior change" ahead of the SVG cutover — see
    /// CLAUDE.md's generator section. The HTML side of this equivalence proof no longer needs a
    /// dedicated test file: since the HTML cutover, <c>CssUtils.SetPropertyValue</c>/
    /// <c>GetPropertyValue</c> forward directly to <see cref="PeachPDF.Html.Core.Utils.CssPropertyRegistry"/>,
    /// so <c>CssUtilsTests</c> exercising <c>CssUtils</c>'s public API already covers it end-to-end.
    /// </summary>
    public class SvgPropertyRegistryEquivalenceTests
    {
        // A non-null diagonal, used by every length-shaped test below to prove ctx.ViewportDiagonal
        // actually threads through to percentage resolution (SvgPropertyContext used to only carry
        // Adapter/ContextColor - a percentage value would have silently resolved against null instead
        // of the real viewport, a genuine gap this migration closed).
        private const double ViewportDiagonal = 100;

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
            var ctx = new SvgPropertyContext(adapter, contextColor, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "fill", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedPaint, element.Fill);
        }

        [Theory]
        [InlineData("red")]
        [InlineData("#336699")]
        [InlineData("none")]
        [InlineData("currentColor")]
        [InlineData("not-a-color")]
        public void Stroke_Matches_SvgValueParsers(string value)
        {
            var adapter = new PdfSharpAdapter();
            var contextColor = RColor.Black;

            var expectedApplies = SvgValueParsers.TryParsePaint(value, adapter, contextColor, out var expectedPaint);

            var element = new SvgGroupElement();
            var ctx = new SvgPropertyContext(adapter, contextColor, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedPaint, element.Stroke);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("5px")]
        [InlineData("10%")]
        [InlineData("-1")]
        [InlineData("bogus")]
        [InlineData("")]
        public void StrokeWidth_Matches_SvgValueParsers(string value)
        {
            var expected = SvgValueParsers.ParseLength(value, ViewportDiagonal);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-width", value, in ctx);

            Assert.Equal(expected is not null, applied);
            if (expected is not null)
                Assert.Equal(expected.Value, element.StrokeWidth);
        }

        [Theory]
        [InlineData("4")]
        [InlineData("1")]
        [InlineData("-1")]
        [InlineData("bogus")]
        public void StrokeMiterlimit_Matches_SvgValueParsers(string value)
        {
            var expected = SvgValueParsers.ParseLength(value, ViewportDiagonal);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-miterlimit", value, in ctx);

            Assert.Equal(expected is not null, applied);
            if (expected is not null)
                Assert.Equal(expected.Value, element.StrokeMiterLimit);
        }

        [Theory]
        [InlineData("5")]
        [InlineData("5px")]
        [InlineData("10%")]
        [InlineData("-1")]
        [InlineData("bogus")]
        public void StrokeDashoffset_Matches_SvgValueParsers(string value)
        {
            var expected = SvgValueParsers.ParseLength(value, ViewportDiagonal);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-dashoffset", value, in ctx);

            Assert.Equal(expected is not null, applied);
            if (expected is not null)
                Assert.Equal(expected.Value, element.StrokeDashOffset);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("5 3")]
        [InlineData("5,3,2")]
        [InlineData("10% 5%")]
        [InlineData("-1 2")]
        [InlineData("bogus")]
        public void StrokeDasharray_Matches_SvgValueParsers(string value)
        {
            var expected = SvgValueParsers.ParseDashArray(value, ViewportDiagonal);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-dasharray", value, in ctx);

            Assert.Equal(expected is not null, applied);
            if (expected is not null)
                Assert.Equal(expected, element.StrokeDashArray);
        }

        [Theory]
        [InlineData("0.5")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("2")]
        [InlineData("bogus")]
        public void Opacity_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseOpacity(value, out var expectedOpacity);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "opacity", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedOpacity, element.Opacity);
        }

        [Theory]
        [InlineData("0.5")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("bogus")]
        public void FillOpacity_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseOpacity(value, out var expectedOpacity);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "fill-opacity", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedOpacity, element.FillOpacity);
        }

        [Theory]
        [InlineData("0.5")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("bogus")]
        public void StrokeOpacity_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseOpacity(value, out var expectedOpacity);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-opacity", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedOpacity, element.StrokeOpacity);
        }

        [Theory]
        [InlineData("nonzero")]
        [InlineData("evenodd")]
        [InlineData("NONZERO")]
        [InlineData("bogus")]
        public void FillRule_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseFillRule(value, out var expectedRule);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "fill-rule", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedRule, element.FillRule);
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
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-linecap", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedCap, element.StrokeLineCap);
        }

        [Theory]
        [InlineData("miter")]
        [InlineData("round")]
        [InlineData("bevel")]
        [InlineData("MITER")]
        [InlineData("bogus")]
        public void StrokeLinejoin_Matches_SvgValueParsers(string value)
        {
            var expectedApplies = SvgValueParsers.TryParseLineJoin(value, out var expectedJoin);

            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal, static p => p);
            var applied = SvgPropertyRegistry.TrySet(element, "stroke-linejoin", value, in ctx);

            Assert.Equal(expectedApplies, applied);
            if (expectedApplies)
                Assert.Equal(expectedJoin, element.StrokeLineJoin);
        }

        [Fact]
        public void Fill_UrlReference_IsReclassifiedByResolveUrlPaintKind()
        {
            // TryParsePaint has no document context, so a url(#id) value always comes back as
            // GradientRef - proving ctx.ResolveUrlPaintKind actually gets invoked (not just present
            // on the struct) requires a delegate that reclassifies it, distinguishable from the
            // identity function every other test above uses.
            var element = new SvgGroupElement();
            var adapter = new PdfSharpAdapter();
            var ctx = new SvgPropertyContext(adapter, RColor.Black, ViewportDiagonal,
                p => p.Kind == SvgPaintKind.GradientRef ? SvgPaint.PatternRef(p.ReferenceId!) : p);

            var applied = SvgPropertyRegistry.TrySet(element, "fill", "url(#pat1)", in ctx);

            Assert.True(applied);
            Assert.Equal(SvgPaintKind.PatternRef, element.Fill.Kind);
            Assert.Equal("pat1", element.Fill.ReferenceId);
        }
    }
}
