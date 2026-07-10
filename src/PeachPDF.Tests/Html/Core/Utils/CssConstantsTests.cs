using PeachPDF.Html.Core.Utils;
using System.Linq;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class CssConstantsTests
    {
        [Fact]
        public void DefaultFont_IsNotEmpty()
        {
            Assert.NotEmpty(CssConstants.DefaultFont);
        }

        [Fact]
        public void DetermineDefaultFont_Windows_ReturnsSegoeUi()
        {
            var result = CssConstants.DetermineDefaultFont(isWindows: true, isMacOS: false, isLinux: false);

            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void DetermineDefaultFont_MacOS_ReturnsArial()
        {
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: true, isLinux: false);

            Assert.Equal("Arial", result);
        }

        [Fact]
        public void DetermineDefaultFont_Linux_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: true);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_UnknownPlatform_FallsBackToSegoeUi()
        {
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false);

            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void GetInstalledFontFamilyNames_ReturnsOnlyNonEmptyFamilyNames()
        {
            var names = CssConstants.GetInstalledFontFamilyNames().ToList();

            Assert.All(names, Assert.NotEmpty);
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
            // Matching is case-insensitive; the canonical (properly-cased) candidate name
            // is returned rather than whatever casing the installed font happened to report.
            var installed = new[] { "liberation sans" };

            var result = CssConstants.PickLinuxDefaultFont(installed);

            Assert.Equal("Liberation Sans", result);
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
