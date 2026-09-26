using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts;

namespace PeachPDF.Tests.Fonts
{
    /// <summary>
    /// The verified-Chromium generic-family mapping per platform. Uses explicit platform booleans (mirroring
    /// <c>FontResolver.DiscoverSupportedFonts</c>'s own precedent) so every platform's table is exercised regardless of the
    /// host OS actually running these tests.
    /// </summary>
    public class GenericFamilyTableTests
    {
        [Theory]
        [InlineData(GenericFamily.Serif, "Times New Roman")]
        [InlineData(GenericFamily.SansSerif, "Arial")]
        [InlineData(GenericFamily.Monospace, "Consolas")]
        [InlineData(GenericFamily.Cursive, "Comic Sans MS")]
        [InlineData(GenericFamily.Fantasy, "Impact")]
        public void Windows_ResolvesToVerifiedChromiumDefaults(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.PlatformDefault(generic, isWindows: true, isMacOS: false, isAndroid: false));
        }

        [Theory]
        [InlineData(GenericFamily.Serif, "Times")]
        [InlineData(GenericFamily.SansSerif, "Helvetica")]
        [InlineData(GenericFamily.Monospace, "Menlo")]
        [InlineData(GenericFamily.Cursive, "Apple Chancery")]
        [InlineData(GenericFamily.Fantasy, "Papyrus")]
        public void MacOS_ResolvesToVerifiedChromiumDefaults(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.PlatformDefault(generic, isWindows: false, isMacOS: true, isAndroid: false));
        }

        [Theory]
        [InlineData(GenericFamily.Serif, "Noto Serif")]
        [InlineData(GenericFamily.SansSerif, "Roboto")]
        [InlineData(GenericFamily.Monospace, "Droid Sans Mono")]
        [InlineData(GenericFamily.Cursive, "Dancing Script")]
        [InlineData(GenericFamily.Fantasy, "Dancing Script")]
        public void Android_ResolvesToVerifiedChromiumDefaults(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.PlatformDefault(generic, isWindows: false, isMacOS: false, isAndroid: true));
        }

        [Fact]
        public void Android_TakesPriorityOverWindows_WhenBothFlagsSomehowTrue()
        {
            // Android is Linux-kernel-based; callers must check it before any other flag. Confirm the
            // table itself enforces that priority even if a caller passed both.
            Assert.Equal("Roboto", GenericFamilyTable.PlatformDefault(GenericFamily.SansSerif, isWindows: true, isMacOS: false, isAndroid: true));
        }

        [Theory]
        [InlineData(GenericFamily.Monospace)]
        [InlineData(GenericFamily.SystemUi)]
        [InlineData(GenericFamily.Math)]
        public void NoPlatformFlagSet_HasNoHardcodedAnswer(GenericFamily generic)
        {
            // Linux is delegated to fontconfig by the caller, and system-ui and math have no per-platform name
            // at all: the table has nothing to say, and the caller substitutes its own default.
            Assert.Null(GenericFamilyTable.PlatformDefault(generic, isWindows: false, isMacOS: false, isAndroid: false));
        }

        [Theory]
        [InlineData(GenericFamily.Serif, "serif")]
        [InlineData(GenericFamily.SansSerif, "sans-serif")]
        [InlineData(GenericFamily.Monospace, "monospace")]
        [InlineData(GenericFamily.Cursive, "cursive")]
        [InlineData(GenericFamily.Fantasy, "fantasy")]
        [InlineData(GenericFamily.SystemUi, "system-ui")]
        [InlineData(GenericFamily.Math, "math")]
        public void CssName_IsTheKeywordFontconfigIsAskedFor(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.CssName(generic));
        }

        [Fact]
        public void CssName_RejectsAValueThatIsNotAGenericFamily()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GenericFamilyTable.CssName((GenericFamily)99));
        }

        [Theory]
        [InlineData(true, false, false, "Latin Modern Math", "Latin Modern Math")]
        [InlineData(true, false, false, "Cambria Math", "Cambria Math")]
        [InlineData(false, true, false, "STIX Two Math", "STIX Two Math")]
        [InlineData(false, false, true, "Noto Sans Math", "Noto Sans Math")]
        [InlineData(false, false, false, "DejaVu Math TeX Gyre", "DejaVu Math TeX Gyre")]
        public void Math_ResolvesToTheInstalledPlatformCandidate(bool isWindows, bool isMacOS, bool isAndroid, string installed, string expected)
        {
            var resolved = GenericFamilyTable.ResolveMathFamily(isWindows, isMacOS, isAndroid, family => family == installed);

            Assert.Equal(expected, resolved);
        }

        [Fact]
        public void Math_PrefersLatinModernMathOverCambriaMath_WhenBothInstalledOnWindows()
        {
            // Chromium's own choice first; Cambria Math is only the fallback that ships with Windows.
            Assert.Equal("Latin Modern Math", GenericFamilyTable.ResolveMathFamily(isWindows: true, isMacOS: false, isAndroid: false, _ => true));
        }

        [Fact]
        public void Math_PrefersTheAndroidChain_WhenAndroidAndWindowsFlagsBothTrue()
        {
            Assert.Equal("Noto Sans Math", GenericFamilyTable.ResolveMathFamily(isWindows: true, isMacOS: false, isAndroid: true, _ => true));
        }

        [Fact]
        public void Math_ResolvesToNull_WhenNoCandidateIsInstalled()
        {
            Assert.Null(GenericFamilyTable.ResolveMathFamily(false, false, false, _ => false));
        }

        [Fact]
        public void Resolve_UsesTheOperatingSystemsAnswerBeforeTheTable_WhenItIsAvailable()
        {
            Assert.Equal("FreeSans", GenericFamilyTable.Resolve(GenericFamily.SystemUi, "FreeSans", false, false, false, _ => true));
            Assert.Equal("FreeSans", GenericFamilyTable.Resolve(GenericFamily.SansSerif, "FreeSans", isWindows: true, isMacOS: false, isAndroid: false, _ => true));
        }

        [Fact]
        public void Resolve_FallsBackToTheTable_WhenTheOperatingSystemHasNoAnswer()
        {
            Assert.Equal("Consolas", GenericFamilyTable.Resolve(GenericFamily.Monospace, null, isWindows: true, isMacOS: false, isAndroid: false, _ => true));
        }

        [Fact]
        public void Resolve_ReportsNothing_WhenTheAnswerIsNotAvailable()
        {
            // A family can be named that this process cannot load; the caller then substitutes its own default.
            Assert.Null(GenericFamilyTable.Resolve(GenericFamily.SystemUi, "PeachPDF Test Family That Is Not Installed", false, false, false, _ => false));
            Assert.Null(GenericFamilyTable.Resolve(GenericFamily.SystemUi, null, false, false, false, _ => true));
        }

        [Fact]
        public void Resolve_ResolvesMathThroughItsChain()
        {
            Assert.Equal("Cambria Math", GenericFamilyTable.Resolve(GenericFamily.Math, "ignored", isWindows: true, isMacOS: false, isAndroid: false, family => family == "Cambria Math"));
        }
    }
}
