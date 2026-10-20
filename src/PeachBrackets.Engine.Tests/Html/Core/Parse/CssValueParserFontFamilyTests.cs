using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
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
            var parser = new CssValueParser(new HtmlRenderContext());

            var result = parser.GetFontFamilyByName(DefaultFontResolver.DefaultFont);

            Assert.Equal(DefaultFontResolver.DefaultFont, result);
        }

        [Fact]
        public void NoCandidateInstalled_ReturnsNull_NotTheLiteralInheritString()
        {
            var parser = new CssValueParser(new HtmlRenderContext());

            var result = parser.GetFontFamilyByName("__DefinitelyNotARealFontFamily__");

            Assert.Null(result);
        }
    }
}
