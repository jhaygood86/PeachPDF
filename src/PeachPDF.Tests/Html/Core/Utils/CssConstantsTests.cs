using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class CssConstantsTests
    {
        [Fact]
        public void DefaultFont_IsNotEmpty()
        {
            Assert.NotEmpty(CssConstants.DefaultFont);
        }

        [Theory]
        [InlineData(new[] { "Liberation Sans", "Liberation Serif" }, "Liberation Sans")]
        [InlineData(new[] { "DejaVu Sans", "DejaVu Serif" }, "DejaVu Sans")]
        [InlineData(new[] { "Noto Sans", "Noto Serif" }, "Noto Sans")]
        public void PickLinuxDefaultFont_PrefersKnownArialAlternative(string[] installed, string expected)
        {
            var result = CssConstants.PickLinuxDefaultFont(installed);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PickLinuxDefaultFont_PrefersEarlierAlternativeWhenMultipleMatch()
        {
            // Liberation Sans is listed before DejaVu Sans in preference order.
            var installed = new[] { "DejaVu Sans", "Liberation Sans" };

            var result = CssConstants.PickLinuxDefaultFont(installed);

            Assert.Equal("Liberation Sans", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_IsCaseInsensitive()
        {
            var installed = new[] { "liberation sans" };

            var result = CssConstants.PickLinuxDefaultFont(installed);

            Assert.Equal("liberation sans", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_NoKnownAlternative_FallsBackToFirstInstalled()
        {
            var installed = new[] { "Some Obscure Font", "Another Font" };

            var result = CssConstants.PickLinuxDefaultFont(installed);

            Assert.Equal("Some Obscure Font", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_NoInstalledFonts_ReturnsNonEmptyFallback()
        {
            var result = CssConstants.PickLinuxDefaultFont([]);

            Assert.NotEmpty(result);
        }
    }
}
