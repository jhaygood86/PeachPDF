using PeachPDF.Html.Core.Utils;
using System.Linq;

using PeachPDF.Fonts;

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
            var result = CssConstants.DetermineDefaultFont(isWindows: true, isMacOS: false, isLinux: false, isAndroid: false);

            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void DetermineDefaultFont_MacOS_ReturnsArial()
        {
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: true, isLinux: false, isAndroid: false);

            Assert.Equal("Arial", result);
        }

        [Fact]
        public void DetermineDefaultFont_Linux_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: true, isAndroid: false);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_Android_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: true);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_Android_TakesPriorityOverLinux()
        {
            // Guards against a regression where Android would be routed into the Linux
            // picker instead of its own, since isLinux may also be true on Android.
            var androidOnly = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: true);
            var androidAndLinux = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: true, isAndroid: true);

            Assert.Equal(androidOnly, androidAndLinux);
        }

        [Fact]
        public void DetermineDefaultFont_UnknownPlatform_FallsBackToSegoeUi()
        {
            var result = CssConstants.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: false);

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

        [Theory]
        [InlineData(new[] { "Roboto", "Roboto Condensed" }, "Roboto")]
        [InlineData(new[] { "Noto Sans", "Noto Serif" }, "Noto Sans")]
        [InlineData(new[] { "Droid Sans", "Droid Sans Mono" }, "Droid Sans")]
        public void PickAndroidDefaultFont_PrefersKnownArialAlternative(string[] installed, string expected)
        {
            var result = CssConstants.PickAndroidDefaultFont(installed);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PickAndroidDefaultFont_PrefersEarlierAlternativeWhenMultipleMatch()
        {
            // Roboto is listed before Noto Sans in preference order.
            var installed = new[] { "Noto Sans", "Roboto" };

            var result = CssConstants.PickAndroidDefaultFont(installed);

            Assert.Equal("Roboto", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_IsCaseInsensitive()
        {
            var installed = new[] { "roboto" };

            var result = CssConstants.PickAndroidDefaultFont(installed);

            Assert.Equal("Roboto", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_NoKnownAlternative_FallsBackToFirstInstalled()
        {
            var installed = new[] { "Some Obscure Font", "Another Font" };

            var result = CssConstants.PickAndroidDefaultFont(installed);

            Assert.Equal("Some Obscure Font", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_NoInstalledFonts_ReturnsNonEmptyFallback()
        {
            var result = CssConstants.PickAndroidDefaultFont([]);

            Assert.NotEmpty(result);
        }
    }
}
