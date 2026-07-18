using PeachPDF.Adapters;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.GetFontFamilyByName"/>'s not-found behavior.
    /// Regression: it used to return the literal string "inherit" as a not-found sentinel, confusable
    /// with the real CSS <c>inherit</c> keyword downstream in <c>CssBox.FontFamily</c>.
    /// </summary>
    public class CssValueParserFontFamilyTests
    {
        [Fact]
        public void KnownInstalledFamily_ReturnsThatFamily()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());

            var result = parser.GetFontFamilyByName(CssConstants.DefaultFont);

            Assert.Equal(CssConstants.DefaultFont, result);
        }

        [Fact]
        public void NoCandidateInstalled_ReturnsNull_NotTheLiteralInheritString()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());

            var result = parser.GetFontFamilyByName("__DefinitelyNotARealFontFamily__");

            Assert.Null(result);
        }
    }
}
