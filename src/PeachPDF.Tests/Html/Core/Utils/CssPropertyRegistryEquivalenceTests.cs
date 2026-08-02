using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Proves the generated <see cref="CssPropertyRegistry"/>/<see cref="SvgPropertyRegistry"/> (built
    /// from css-properties.json by PeachPDF.SourceGenerators) behave identically to the hand-written
    /// <see cref="CssUtils"/> dispatch / <c>SvgValueParsers</c> calls for the 10 properties authored so
    /// far — the first, deliberately small slice this generator infrastructure change proves out before
    /// the remaining ~180 properties are migrated in a follow-up change. Both registries currently run
    /// *alongside* the hand-written dispatch (nothing production calls them yet), so this is the actual
    /// evidence of "zero production behavior change" for this change, not merely an assertion of intent.
    /// </summary>
    public class CssPropertyRegistryEquivalenceTests
    {
        private static (CssBox Box, CssValueParser Parser) NewBoxAndParser()
        {
            var adapter = new PdfSharpAdapter();
            return (new CssBox(null, null), new CssValueParser(adapter));
        }

        [Theory]
        [InlineData("10px")]
        [InlineData("1em")]
        [InlineData("0")]
        [InlineData("auto")]
        [InlineData("AUTO")]
        [InlineData("bogus")]
        [InlineData("")]
        public void BorderBottomWidth_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "border-bottom-width", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "border-bottom-width", value);

            Assert.Equal(oldBox.BorderBottomWidth, newBox.BorderBottomWidth);
            Assert.Equal(oldBox.BorderBottomWidth == value, applied);
        }

        [Theory]
        [InlineData("translate(10px)")]
        [InlineData("")]
        [InlineData("none")]
        public void Transform_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "transform", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "transform", value);

            Assert.Equal(oldBox.Transform, newBox.Transform);
            Assert.True(applied);
        }

        [Theory]
        [InlineData("border-box")]
        [InlineData("content-box")]
        [InlineData("BORDER-BOX")]
        [InlineData("bogus")]
        public void BoxSizing_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "box-sizing", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "box-sizing", value);

            Assert.Equal(oldBox.BoxSizing, newBox.BoxSizing);
            Assert.Equal(oldBox.BoxSizing == value, applied);
        }

        [Theory]
        [InlineData("ltr")]
        [InlineData("rtl")]
        [InlineData("bogus")]
        [InlineData("inherit")]
        public void Direction_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "direction", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "direction", value);

            Assert.Equal(oldBox.Direction.ToString(), newBox.Direction.ToString());
            Assert.True(applied); // CssProperty<T>.FromCssText never rejects — matches today's leniency exactly.
        }

        [Theory]
        [InlineData("always")]
        [InlineData("auto")]
        [InlineData("avoid")]
        public void PageBreakAfter_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "page-break-after", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "page-break-after", value);

            Assert.Equal(oldBox.BreakAfter, newBox.BreakAfter);
            Assert.True(applied);
            Assert.Equal(value is "always" ? "page" : value, newBox.BreakAfter);
        }

        [Theory]
        [InlineData("Arial, sans-serif")]
        [InlineData("'Times New Roman'")]
        public void FontFamily_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "font-family", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "font-family", value);

            Assert.Equal(oldBox.FontFamily, newBox.FontFamily);
            Assert.Equal(oldBox.FontFamilyList, newBox.FontFamilyList);
            Assert.True(applied);
        }

        [Theory]
        [InlineData("10px")]
        [InlineData("auto")]
        [InlineData("")]
        public void MarginBlockStart_Matches_Old_Dispatch(string value)
        {
            var (oldBox, parser) = NewBoxAndParser();
            var (newBox, _) = NewBoxAndParser();

            CssUtils.SetPropertyValue(parser, oldBox, "margin-block-start", value);
            var applied = CssPropertyRegistry.TrySet(parser, newBox, "margin-block-start", value);

            Assert.Equal(oldBox.MarginBlockStart, newBox.MarginBlockStart);
            Assert.True(applied);
        }

        [Fact]
        public void OverflowWrap_OldDispatchIsANoOp_NewDispatchCorrectlyReportsUnsupported()
        {
            // Regression guard: the old hand-written dispatch had a silent no-op lambda for this
            // recognized-but-unimplemented property (docs/html-css-support.md), which a caller couldn't
            // distinguish from success. TrySet's bool return is the whole point of this migration.
            var parser = new CssValueParser(new PdfSharpAdapter());
            var box = new CssBox(null, null);

            var applied = CssPropertyRegistry.TrySet(parser, box, "overflow-wrap", "break-word");

            Assert.False(applied);
        }

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
