using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// Tests for the shared <see cref="ColorInterpolationMethodGrammar"/> (the single home of the gradient
    /// <c>in &lt;color-interpolation-method&gt;</c> grammar consumed by both the CSS-OM validator and the
    /// render-time parser — issue #245).
    ///
    /// The grammar validates only the <c>in …</c> slice and returns the rest of the group as the remainder;
    /// whether that remainder is a valid gradient direction/shape is the caller's job. So e.g. a hue method
    /// on a rectangular space parses here (remainder <c>"longer hue"</c>) and is rejected downstream — that
    /// end-to-end drop is covered by <see cref="GradientTests"/>.
    /// </summary>
    public class ColorInterpolationMethodGrammarTests : CssConstructionFunctions
    {
        private static (bool Ok, bool HasIn, string Remainder) Extract(string value)
        {
            var ok = ColorInterpolationMethodGrammar.TryExtractInterpolationMethod(
                CssValueParser.GetCssTokens(value), out var remainder, out var hasIn);
            // GetCssTokens does not emit whitespace tokens, so rejoin the remainder's tokens with a space.
            var text = ok && remainder != null ? string.Join(" ", remainder.Select(t => t.ToValue())) : null;
            return (ok, hasIn, text);
        }

        [Fact]
        public void NoInClause_PassesThroughUnchanged()
        {
            var (ok, hasIn, remainder) = Extract("to right");
            Assert.True(ok);
            Assert.False(hasIn);
            Assert.Equal("to right", remainder);
        }

        [Theory]
        [InlineData("in srgb")]
        [InlineData("in srgb-linear")]
        [InlineData("in display-p3")]
        [InlineData("in lab")]
        [InlineData("in oklab")]
        [InlineData("in xyz")]
        [InlineData("in xyz-d65")]
        [InlineData("in xyz-d50")]
        [InlineData("in hsl")]
        [InlineData("in hwb")]
        [InlineData("in lch")]
        [InlineData("in oklch")]
        [InlineData("in oklch shorter hue")]
        [InlineData("in oklch longer hue")]
        [InlineData("in hwb increasing hue")]
        [InlineData("in lch decreasing hue")]
        public void ValidMethodOnly_HasInWithEmptyRemainder(string value)
        {
            var (ok, hasIn, remainder) = Extract(value);
            Assert.True(ok);
            Assert.True(hasIn);
            Assert.Equal("", remainder);
        }

        [Theory]
        [InlineData("in oklab to right", "to right")]
        [InlineData("to right in oklab", "to right")]
        [InlineData("in oklch longer hue to right", "to right")]
        // A hue method on a rectangular space is not rejected by the grammar - the leftover "longer hue"
        // is returned as the remainder for the caller's direction validation to reject.
        [InlineData("in oklab longer hue", "longer hue")]
        public void ValidMethodWithRemainder_ExtractsDirectionRemainder(string value, string expectedRemainder)
        {
            var (ok, hasIn, remainder) = Extract(value);
            Assert.True(ok);
            Assert.True(hasIn);
            Assert.Equal(expectedRemainder, remainder);
        }

        [Theory]
        [InlineData("in nonsense")]            // unknown space
        [InlineData("in")]                     // "in" with nothing after
        [InlineData("in oklch longer")]        // polar hue direction without the trailing "hue"
        [InlineData("in a98-rgb")]             // valid CSS space, unsupported by PeachPDF's interpolation
        [InlineData("in prophoto-rgb")]
        [InlineData("in rec2020")]
        public void InvalidMethod_ReturnsFalse(string value)
        {
            var ok = ColorInterpolationMethodGrammar.TryExtractInterpolationMethod(
                CssValueParser.GetCssTokens(value), out _, out _);
            Assert.False(ok);
        }

        [Theory]
        [InlineData("srgb")]
        [InlineData("oklab")]
        [InlineData("oklch")]
        public void IsColorSpace_RecognizesSupportedSpaces(string name) =>
            Assert.True(ColorInterpolationMethodGrammar.IsColorSpace(name));

        [Theory]
        [InlineData("a98-rgb")]
        [InlineData("prophoto-rgb")]
        [InlineData("banana")]
        public void IsColorSpace_RejectsUnsupportedOrUnknown(string name) =>
            Assert.False(ColorInterpolationMethodGrammar.IsColorSpace(name));

        [Theory]
        [InlineData("oklch", true)]
        [InlineData("hsl", true)]
        [InlineData("oklab", false)]
        [InlineData("srgb", false)]
        public void IsPolarColorSpace_ClassifiesPolarVsRectangular(string name, bool polar) =>
            Assert.Equal(polar, ColorInterpolationMethodGrammar.IsPolarColorSpace(name));
    }
}
