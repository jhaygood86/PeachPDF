using PeachDrawing.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
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
        public void EveryMappedKeyword_NamesItsEngineFamily(string keyword, GenericFamily expected)
        {
            Assert.Contains((keyword, expected), GenericFontFamilyResolver.Generics);
        }

        [Fact]
        public void SystemUi_IsNotOneOfTheMappedGenerics_BecauseItIsHandledSeparately()
        {
            Assert.DoesNotContain(GenericFontFamilyResolver.Generics, entry => entry.Family == GenericFamily.SystemUi);
        }
    }
}
