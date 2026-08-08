using PeachPDF.Html.Core.Utils;
using System.Linq;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class DefaultFontResolverTests
    {
        [Fact]
        public void DefaultFont_IsNotEmpty()
        {
            Assert.NotEmpty(DefaultFontResolver.DefaultFont);
        }

        [Fact]
        public void DetermineDefaultFont_Windows_ReturnsSegoeUi()
        {
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: true, isMacOS: false, isLinux: false, isAndroid: false, isBrowser: false);

            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void DetermineDefaultFont_MacOS_ReturnsArial()
        {
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: true, isLinux: false, isAndroid: false, isBrowser: false);

            Assert.Equal("Arial", result);
        }

        [Fact]
        public void DetermineDefaultFont_Linux_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: true, isAndroid: false, isBrowser: false);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_Android_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: true, isBrowser: false);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_Android_TakesPriorityOverLinux()
        {
            // Guards against a regression where Android would be routed into the Linux
            // picker instead of its own, since isLinux may also be true on Android.
            var androidOnly = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: true, isBrowser: false);
            var androidAndLinux = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: true, isAndroid: true, isBrowser: false);

            Assert.Equal(androidOnly, androidAndLinux);
        }

        [Fact]
        public void DetermineDefaultFont_UnknownPlatform_FallsBackToSegoeUi()
        {
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: false, isBrowser: false);

            Assert.Equal("Segoe UI", result);
        }

        [Fact]
        public void DetermineDefaultFont_Browser_ReturnsNonEmptyPickFromInstalledFonts()
        {
            // Exercises the real (non-forced) GetInstalledFontFamilyNames/FontResolver.SupportedFonts
            // path directly, regardless of which OS the test itself runs on.
            var result = DefaultFontResolver.DetermineDefaultFont(isWindows: false, isMacOS: false, isLinux: false, isAndroid: false, isBrowser: true);

            Assert.NotEmpty(result);
        }

        [Fact]
        public void DetermineDefaultFont_Browser_DoesNotFallBackToSegoeUi()
        {
            // The whole point of the browser branch: without it a WebAssembly host lands on the
            // unknown-platform "Segoe UI" fallback, which nothing there can possibly satisfy, and
            // CssBoxProperties.ActualFont throws rather than rendering.
            var result = DefaultFontResolver.PickBrowserDefaultFont([]);

            Assert.Equal("Liberation Sans", result);
        }

        [Theory]
        [InlineData(new[] { "Liberation Sans", "Liberation Serif" }, "Liberation Sans")]
        [InlineData(new[] { "DejaVu Sans", "DejaVu Serif" }, "DejaVu Sans")]
        [InlineData(new[] { "Noto Sans", "Noto Serif" }, "Noto Sans")]
        public void PickBrowserDefaultFont_PrefersKnownArialAlternative(string[] installed, string expected)
        {
            var result = DefaultFontResolver.PickBrowserDefaultFont(installed);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PickBrowserDefaultFont_NoKnownAlternative_FallsBackToFirstInstalled()
        {
            var installed = new[] { "Some Obscure Font", "Another Font" };

            var result = DefaultFontResolver.PickBrowserDefaultFont(installed);

            Assert.Equal("Some Obscure Font", result);
        }

        [Fact]
        public void GetInstalledFontFamilyNames_ReturnsOnlyNonEmptyFamilyNames()
        {
            var names = DefaultFontResolver.GetInstalledFontFamilyNames().ToList();

            Assert.All(names, Assert.NotEmpty);
        }

        [Theory]
        [InlineData(new[] { "Liberation Sans", "Liberation Serif" }, "Liberation Sans")]
        [InlineData(new[] { "DejaVu Sans", "DejaVu Serif" }, "DejaVu Sans")]
        [InlineData(new[] { "Noto Sans", "Noto Serif" }, "Noto Sans")]
        public void PickLinuxDefaultFont_PrefersKnownArialAlternative(string[] installed, string expected)
        {
            var result = DefaultFontResolver.PickLinuxDefaultFont(installed);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PickLinuxDefaultFont_PrefersEarlierAlternativeWhenMultipleMatch()
        {
            // Liberation Sans is listed before DejaVu Sans in preference order.
            var installed = new[] { "DejaVu Sans", "Liberation Sans" };

            var result = DefaultFontResolver.PickLinuxDefaultFont(installed);

            Assert.Equal("Liberation Sans", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_IsCaseInsensitive()
        {
            // Matching is case-insensitive; the canonical (properly-cased) candidate name
            // is returned rather than whatever casing the installed font happened to report.
            var installed = new[] { "liberation sans" };

            var result = DefaultFontResolver.PickLinuxDefaultFont(installed);

            Assert.Equal("Liberation Sans", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_NoKnownAlternative_FallsBackToFirstInstalled()
        {
            var installed = new[] { "Some Obscure Font", "Another Font" };

            var result = DefaultFontResolver.PickLinuxDefaultFont(installed);

            Assert.Equal("Some Obscure Font", result);
        }

        [Fact]
        public void PickLinuxDefaultFont_NoInstalledFonts_ReturnsNonEmptyFallback()
        {
            var result = DefaultFontResolver.PickLinuxDefaultFont([]);

            Assert.NotEmpty(result);
        }

        [Theory]
        [InlineData(new[] { "Roboto", "Roboto Condensed" }, "Roboto")]
        [InlineData(new[] { "Noto Sans", "Noto Serif" }, "Noto Sans")]
        [InlineData(new[] { "Droid Sans", "Droid Sans Mono" }, "Droid Sans")]
        public void PickAndroidDefaultFont_PrefersKnownArialAlternative(string[] installed, string expected)
        {
            var result = DefaultFontResolver.PickAndroidDefaultFont(installed);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void PickAndroidDefaultFont_PrefersEarlierAlternativeWhenMultipleMatch()
        {
            // Roboto is listed before Noto Sans in preference order.
            var installed = new[] { "Noto Sans", "Roboto" };

            var result = DefaultFontResolver.PickAndroidDefaultFont(installed);

            Assert.Equal("Roboto", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_IsCaseInsensitive()
        {
            var installed = new[] { "roboto" };

            var result = DefaultFontResolver.PickAndroidDefaultFont(installed);

            Assert.Equal("Roboto", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_NoKnownAlternative_FallsBackToFirstInstalled()
        {
            var installed = new[] { "Some Obscure Font", "Another Font" };

            var result = DefaultFontResolver.PickAndroidDefaultFont(installed);

            Assert.Equal("Some Obscure Font", result);
        }

        [Fact]
        public void PickAndroidDefaultFont_NoInstalledFonts_ReturnsNonEmptyFallback()
        {
            var result = DefaultFontResolver.PickAndroidDefaultFont([]);

            Assert.NotEmpty(result);
        }
    }
}
