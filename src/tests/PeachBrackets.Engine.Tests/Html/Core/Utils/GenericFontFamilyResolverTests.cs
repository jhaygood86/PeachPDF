using PeachDrawing.Text;
using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary>
    /// The CSS keyword side of the generic-family mapping: which keywords <see cref="GenericFontFamilyResolver"/> maps to a
    /// real family, and the engine's name for each. The per-platform answers themselves are tested with the engine's table.
    /// </summary>
    public class GenericFontFamilyResolverTests
    {
        [Theory]
        [InlineData(Keywords.Serif, GenericFamily.Serif)]
        [InlineData(Keywords.SansSerif, GenericFamily.SansSerif)]
        [InlineData(Keywords.Monospace, GenericFamily.Monospace)]
        [InlineData(Keywords.Cursive, GenericFamily.Cursive)]
        [InlineData(Keywords.Fantasy, GenericFamily.Fantasy)]
        [InlineData(Keywords.Math, GenericFamily.Math)]
        [InlineData(Keywords.UiSerif, GenericFamily.UiSerif)]
        [InlineData(Keywords.UiSansSerif, GenericFamily.UiSansSerif)]
        [InlineData(Keywords.UiMonospace, GenericFamily.UiMonospace)]
        [InlineData(Keywords.UiRounded, GenericFamily.UiRounded)]
        public void EveryMappedKeyword_NamesItsEngineFamily(string keyword, GenericFamily expected)
        {
            Assert.Contains((keyword, expected), GenericFontFamilyResolver.Generics);
        }

        [Fact]
        public void SystemUi_IsNotOneOfTheMappedGenerics_BecauseItIsHandledSeparately()
        {
            Assert.DoesNotContain(GenericFontFamilyResolver.Generics, entry => entry.Family == GenericFamily.SystemUi);
        }

        [Fact]
        public void TheSystemUiAliases_AreNamesOfTheSystemFont_OnlyOnAnApplePlatform()
        {
            var family = FontDefaults.DefaultFamily;

            var apple = new PeachPDF.Rendering.HtmlRenderContext();
            apple.RegisterSystemUiAliases(applePlatform: true, family);
            Assert.True(apple.IsFontExists("-apple-system"));
            Assert.True(apple.IsFontExists("BlinkMacSystemFont"));

            // Elsewhere a stack such as "-apple-system, Roboto" has to go on to its next name, so the alias is not registered.
            // (A host that is itself an Apple platform has registered it already, in the constructor.)
            if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsTvOS()))
            {
                var other = new PeachPDF.Rendering.HtmlRenderContext();
                other.RegisterSystemUiAliases(applePlatform: false, family);
                Assert.False(other.IsFontExists("-apple-system"));
            }
        }

        [Fact]
        public void TheSystemUiAliases_AreTheOnesSafariAndChromeAccept()
        {
            Assert.Equal(["-apple-system", "BlinkMacSystemFont"], GenericFontFamilyResolver.SystemUiAliases);
            Assert.Equal(GenericFontFamilyResolver.SystemUiAliases, FontDefaults.SystemUiAliases);
        }
    }
}
