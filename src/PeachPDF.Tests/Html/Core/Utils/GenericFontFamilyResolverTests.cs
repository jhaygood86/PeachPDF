using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="GenericFontFamilyResolver"/> - verified-Chromium generic-family mapping
    /// per platform. Uses explicit platform booleans (mirroring
    /// <c>PdfSharpCore.Utils.FontResolver.DiscoverSupportedFonts</c>'s own precedent) so every platform's
    /// table is exercised regardless of the host OS actually running these tests.
    /// </summary>
    public class GenericFontFamilyResolverTests
    {
        [Theory]
        [InlineData(CssConstants.Serif, "Times New Roman")]
        [InlineData(CssConstants.SansSerif, "Arial")]
        [InlineData(CssConstants.Monospace, "Consolas")]
        [InlineData(CssConstants.Cursive, "Comic Sans MS")]
        [InlineData(CssConstants.Fantasy, "Impact")]
        public void Windows_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: true, isMacOS: false, isAndroid: false));
        }

        [Theory]
        [InlineData(CssConstants.Serif, "Times")]
        [InlineData(CssConstants.SansSerif, "Helvetica")]
        [InlineData(CssConstants.Monospace, "Menlo")]
        [InlineData(CssConstants.Cursive, "Apple Chancery")]
        [InlineData(CssConstants.Fantasy, "Papyrus")]
        public void MacOS_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: false, isMacOS: true, isAndroid: false));
        }

        [Theory]
        [InlineData(CssConstants.Serif, "Noto Serif")]
        [InlineData(CssConstants.SansSerif, "Roboto")]
        [InlineData(CssConstants.Monospace, "Droid Sans Mono")]
        [InlineData(CssConstants.Cursive, "Dancing Script")]
        [InlineData(CssConstants.Fantasy, "Dancing Script")]
        public void Android_ResolvesToVerifiedChromiumDefaults(string generic, string expected)
        {
            Assert.Equal(expected, GenericFontFamilyResolver.ResolvePlatformDefault(generic, isWindows: false, isMacOS: false, isAndroid: true));
        }

        [Fact]
        public void Android_TakesPriorityOverWindows_WhenBothFlagsSomehowTrue()
        {
            // Android is Linux-kernel-based; callers must check it before any other flag. Confirm the
            // resolver itself enforces that priority even if a caller passed both.
            Assert.Equal("Roboto", GenericFontFamilyResolver.ResolvePlatformDefault(CssConstants.SansSerif, isWindows: true, isMacOS: false, isAndroid: true));
        }

        [Fact]
        public void NoPlatformFlagSet_ReturnsGenericNameUnchanged()
        {
            // Linux (delegated to fontconfig by the caller) and any other unhandled platform - the
            // resolver itself has no table for these, and returns the input unchanged so the caller's own
            // installed-family verification step can substitute a real fallback.
            Assert.Equal(CssConstants.Monospace, GenericFontFamilyResolver.ResolvePlatformDefault(CssConstants.Monospace, isWindows: false, isMacOS: false, isAndroid: false));
        }
    }
}
