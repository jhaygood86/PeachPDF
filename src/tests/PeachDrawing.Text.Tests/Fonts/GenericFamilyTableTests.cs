using PeachDrawing.Text;
using PeachDrawing.Text.Internal.Fonts;

namespace PeachDrawing.Text.Tests.Fonts
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
        [InlineData(GenericFamily.Monospace, "Courier")]
        [InlineData(GenericFamily.Cursive, "Apple Chancery")]
        [InlineData(GenericFamily.Fantasy, "Papyrus")]
        public void MacOS_ResolvesToWebKitDefaults(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.PlatformDefault(generic, isWindows: false, isMacOS: true, isAndroid: false));
        }

        [Theory]
        [InlineData(GenericFamily.Serif, "Times New Roman", "Times New Roman")]
        [InlineData(GenericFamily.SansSerif, "Helvetica Neue", "Helvetica Neue")]
        [InlineData(GenericFamily.Monospace, "Courier New", "Courier New")]
        [InlineData(GenericFamily.Monospace, "Menlo", "Menlo")]
        [InlineData(GenericFamily.Cursive, "Snell Roundhand", "Snell Roundhand")]
        [InlineData(GenericFamily.Fantasy, "Marker Felt", "Marker Felt")]
        [InlineData(GenericFamily.SystemUi, "Helvetica", "Helvetica")]
        [InlineData(GenericFamily.UiMonospace, "Menlo", "Menlo")]
        [InlineData(GenericFamily.UiRounded, "Helvetica", "Helvetica")]
        public void Apple_FallsThroughItsChainToTheFirstInstalledFamily(GenericFamily generic, string installed, string expected)
        {
            // The chain is tried in order and only for a name the caller says is available - on macOS and on iOS alike.
            Assert.Equal(expected, GenericFamilyTable.Resolve(generic, null, isWindows: false, isMacOS: true, isAndroid: false, family => family == installed));
            Assert.Equal(expected, GenericFamilyTable.Resolve(generic, null, isWindows: false, isMacOS: false, isAndroid: false, family => family == installed, isIOS: true));
        }

        [Theory]
        [InlineData(GenericFamily.Serif)]
        [InlineData(GenericFamily.SansSerif)]
        [InlineData(GenericFamily.Monospace)]
        [InlineData(GenericFamily.Cursive)]
        [InlineData(GenericFamily.Fantasy)]
        [InlineData(GenericFamily.SystemUi)]
        [InlineData(GenericFamily.UiSerif)]
        [InlineData(GenericFamily.UiSansSerif)]
        [InlineData(GenericFamily.UiMonospace)]
        [InlineData(GenericFamily.UiRounded)]
        public void Apple_WithNothingInstalled_HasNoAnswer(GenericFamily generic)
        {
            Assert.Null(GenericFamilyTable.Resolve(generic, null, isWindows: false, isMacOS: true, isAndroid: false, _ => false));
            Assert.Null(GenericFamilyTable.Resolve(generic, null, isWindows: false, isMacOS: false, isAndroid: false, _ => false, isIOS: true));
        }

        [Fact]
        public void Apple_PrefersTheSystemsOwnAnswerWhenItIsAvailable_AndOtherwiseUsesItsTable()
        {
            Assert.Equal("System Font", GenericFamilyTable.Resolve(GenericFamily.SystemUi, "System Font", false, true, false, _ => true));
            Assert.Equal("Helvetica Neue", GenericFamilyTable.Resolve(GenericFamily.SystemUi, "System Font", false, true, false, family => family == "Helvetica Neue"));
            Assert.Equal(".New York", GenericFamilyTable.Resolve(GenericFamily.UiSerif, ".New York", false, false, false, _ => true, isIOS: true));
        }

        [Theory]
        [InlineData(GenericFamily.UiSerif, GenericFamily.Serif)]
        [InlineData(GenericFamily.UiMonospace, GenericFamily.Monospace)]
        [InlineData(GenericFamily.UiSansSerif, GenericFamily.SystemUi)]
        [InlineData(GenericFamily.UiRounded, GenericFamily.SystemUi)]
        [InlineData(GenericFamily.Serif, GenericFamily.Serif)]
        [InlineData(GenericFamily.Math, GenericFamily.Math)]
        public void Equivalent_IsTheGenericAUiFamilyResembles(GenericFamily generic, GenericFamily expected)
        {
            Assert.Equal(expected, GenericFamilyTable.Equivalent(generic));
        }

        [Fact]
        public void UiGenerics_OffTheApplePlatforms_AreTheGenericsTheyResemble()
        {
            Assert.Equal("Times New Roman", GenericFamilyTable.Resolve(GenericFamily.UiSerif, null, true, false, false, _ => true));
            Assert.Equal("Consolas", GenericFamilyTable.Resolve(GenericFamily.UiMonospace, null, true, false, false, _ => true));
            Assert.Equal("Droid Sans Mono", GenericFamilyTable.Resolve(GenericFamily.UiMonospace, null, false, false, true, _ => true));
            // fontconfig is asked for the generic they resemble, so its answer is the one given for them.
            Assert.Equal("FreeSans", GenericFamilyTable.Resolve(GenericFamily.UiSansSerif, "FreeSans", false, false, false, _ => true));
            Assert.Null(GenericFamilyTable.Resolve(GenericFamily.UiRounded, null, true, false, false, _ => true));
        }

        [Theory]
        [InlineData(GenericFamily.UiSerif, "ui-serif")]
        [InlineData(GenericFamily.UiSansSerif, "ui-sans-serif")]
        [InlineData(GenericFamily.UiMonospace, "ui-monospace")]
        [InlineData(GenericFamily.UiRounded, "ui-rounded")]
        public void CssName_OfTheUiGenerics_IsTheirKeyword(GenericFamily generic, string expected)
        {
            Assert.Equal(expected, GenericFamilyTable.CssName(generic));
        }

        [Theory]
        [InlineData(GenericFamily.SystemUi, 0)]
        [InlineData(GenericFamily.UiSansSerif, 0)]
        [InlineData(GenericFamily.UiSerif, 1)]
        [InlineData(GenericFamily.UiMonospace, 2)]
        [InlineData(GenericFamily.UiRounded, 3)]
        public void AppleUiDesignOf_NamesTheDesignOfAnInterfaceGeneric(GenericFamily generic, int design)
        {
            Assert.Equal((PeachDrawing.Text.Internal.Fonts.Apple.AppleUiDesign)design, GenericFamilyTable.AppleUiDesignOf(generic));
        }

        [Fact]
        public void AppleUiDesignOf_AnOrdinaryGeneric_HasNone()
        {
            Assert.Null(GenericFamilyTable.AppleUiDesignOf(GenericFamily.Serif));
        }

        [Fact]
        public void Apple_Math_UsesTheMacChain()
        {
            Assert.Equal("STIX Two Math", GenericFamilyTable.Resolve(GenericFamily.Math, null, false, false, false, family => family == "STIX Two Math", isIOS: true));
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
