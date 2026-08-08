using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;

using PeachPDF.Fonts;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="GenericFontFamilyResolver"/> - verified-Chromium generic-family mapping
    /// per platform. Uses explicit platform booleans (mirroring
    /// <c>PeachPDF.Fonts.FontResolver.DiscoverSupportedFonts</c>'s own precedent) so every platform's
    /// table is exercised regardless of the host OS actually running these tests.
    /// </summary>
    public class GenericFontFamilyResolverTests
    {
        [Theory]
        [InlineData(Keywords.Serif, "Times New Roman")]
        [InlineData(Keywords.SansSerif, "Arial")]
        [InlineData(Keywords.Monospace, "Consolas")]
        [InlineData(Keywords.Cursive, "Comic Sans MS")]
        [InlineData(Keywords.Fantasy, "Impact")]
        public void Windows_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: true, isMacOS: false, isAndroid: false));
        }

        [Theory]
        [InlineData(Keywords.Serif, "Times")]
        [InlineData(Keywords.SansSerif, "Helvetica")]
        [InlineData(Keywords.Monospace, "Menlo")]
        [InlineData(Keywords.Cursive, "Apple Chancery")]
        [InlineData(Keywords.Fantasy, "Papyrus")]
        public void MacOS_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: false, isMacOS: true, isAndroid: false));
        }

        [Theory]
        [InlineData(Keywords.Serif, "Noto Serif")]
        [InlineData(Keywords.SansSerif, "Roboto")]
        [InlineData(Keywords.Monospace, "Droid Sans Mono")]
        [InlineData(Keywords.Cursive, "Dancing Script")]
        [InlineData(Keywords.Fantasy, "Dancing Script")]
        public void Android_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: false, isMacOS: false, isAndroid: true));
        }

        [Fact]
        public void Android_TakesPriorityOverWindows_WhenBothFlagsSomehowTrue()
        {
            // Android is Linux-kernel-based; callers must check it before any other flag. Confirm the
            // resolver itself enforces that priority even if a caller passed both.
            Assert.Equal("Roboto", GenericFontFamilyResolver.ResolvePlatformDefault(Keywords.SansSerif, isWindows: true, isMacOS: false, isAndroid: true));
        }

        [Fact]
        public void NoPlatformFlagSet_ReturnsGenericNameUnchanged()
        {
            // Linux (delegated to fontconfig by the caller) and any other unhandled platform - the
            // resolver itself has no table for these, and returns the input unchanged so the caller's own
            // installed-family verification step can substitute a real fallback.
            Assert.Equal(Keywords.Monospace, GenericFontFamilyResolver.ResolvePlatformDefault(Keywords.Monospace, isWindows: false, isMacOS: false, isAndroid: false));
        }
    }
}
