using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Direct unit tests for <see cref="FontSizeResolver"/>, extracted from
    /// <c>CssBoxProperties.ActualFont</c>'s original inline switch so in-flow content and
    /// <c>MarginBoxRenderer.BuildFont</c> (@page margin boxes) share one implementation. Regression
    /// coverage for the gap where margin-box font-size keywords/relative units (unlike plain absolute
    /// units, which <c>DomParser.ParseLengthToPdfPoints</c> already handled) silently fell back to a
    /// hardcoded default instead of resolving.
    /// </summary>
    public class FontSizeResolverTests
    {
        [Theory]
        [InlineData("medium", 0)]
        [InlineData("xx-small", -4)]
        [InlineData("x-small", -3)]
        [InlineData("small", -2)]
        [InlineData("large", 2)]
        [InlineData("x-large", 3)]
        [InlineData("xx-large", 4)]
        public void AbsoluteKeyword_ResolvesRelativeToCssConstantsFontSize(string keyword, double offset)
        {
            var result = FontSizeResolver.Resolve(keyword, parentSize: 999, remSize: 999);

            Assert.Equal(CssConstants.FontSize + offset, result);
        }

        [Fact]
        public void Smaller_ResolvesRelativeToParentSize()
        {
            var result = FontSizeResolver.Resolve("smaller", parentSize: 20, remSize: 999);

            Assert.Equal(18, result);
        }

        [Fact]
        public void Larger_ResolvesRelativeToParentSize()
        {
            var result = FontSizeResolver.Resolve("larger", parentSize: 20, remSize: 999);

            Assert.Equal(22, result);
        }

        [Fact]
        public void AbsoluteLength_ParsesDirectly()
        {
            var result = FontSizeResolver.Resolve("14pt", parentSize: 999, remSize: 999);

            Assert.Equal(14, result);
        }

        [Fact]
        public void Percentage_ResolvesRelativeToParentSize()
        {
            var result = FontSizeResolver.Resolve("150%", parentSize: 20, remSize: 999);

            Assert.Equal(30, result);
        }

        [Fact]
        public void Em_ResolvesRelativeToParentSize()
        {
            var result = FontSizeResolver.Resolve("2em", parentSize: 10, remSize: 999);

            Assert.Equal(20, result);
        }

        [Fact]
        public void Rem_ResolvesRelativeToRemSize()
        {
            var result = FontSizeResolver.Resolve("2rem", parentSize: 999, remSize: 8);

            Assert.Equal(16, result);
        }

        [Fact]
        public void ZeroFontSize_IsHonoredNotReplacedWithDefault()
        {
            // Regression: font-size: 0 is a legitimate, deliberate CSS declaration (some sites use it to
            // visually hide text while keeping it selectable/accessible) - it must not be silently
            // replaced with the medium default.
            var result = FontSizeResolver.Resolve("0pt", parentSize: 999, remSize: 999);

            Assert.Equal(0, result);
        }

        [Fact]
        public void VerySmallFontSize_IsHonoredNotReplacedWithDefault()
        {
            var result = FontSizeResolver.Resolve("0.5pt", parentSize: 999, remSize: 999);

            Assert.Equal(0.5, result);
        }

        [Fact]
        public void NegativeComputedValue_ClampsToZero_NotToDefault()
        {
            // A negative computed font-size (e.g. from calc(-1px)) is spec-clamped to zero, not
            // silently replaced with the medium default either.
            var result = FontSizeResolver.Resolve("calc(-5pt)", parentSize: 999, remSize: 999);

            Assert.Equal(0, result);
        }
    }
}
