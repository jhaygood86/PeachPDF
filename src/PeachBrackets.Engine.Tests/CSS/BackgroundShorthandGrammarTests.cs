using PeachBrackets.Css;
using System.Linq;
using Xunit;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// The grammar of the <c>background</c> shorthand's layer: <c>&lt;bg-position&gt; [ / &lt;bg-size&gt; ]?</c> needs the position before a slash, and one
    /// <c>&lt;box&gt;</c> sets both <c>background-origin</c> and <c>background-clip</c> (CSS Backgrounds 3 section 2.10).
    /// </summary>
    public class BackgroundShorthandGrammarTests
    {
        private static string[] Expand(string declaration, params string[] longhands)
        {
            var sheet = StylesheetParser.Default.Parse("#a { " + declaration + "; }");
            var style = sheet.Rules.OfType<IStyleRule>().Single().Style;

            return longhands.Select(name => style.GetPropertyValue(name)).ToArray();
        }

        [Theory]
        [InlineData("url(a.png) content-box", "content-box", "content-box")]
        [InlineData("url(a.png) padding-box", "padding-box", "padding-box")]
        [InlineData("url(a.png) border-box", "border-box", "border-box")]
        [InlineData("content-box url(a.png)", "content-box", "content-box")]
        [InlineData("url(a.png) content-box padding-box", "content-box", "padding-box")]
        [InlineData("url(a.png) padding-box content-box", "padding-box", "content-box")]
        [InlineData("url(a.png) content-box no-repeat padding-box", "content-box", "padding-box")]
        [InlineData("url(a.png)", "initial", "initial")]
        public void ABoxKeyword_SetsOriginAndClip_LikeABrowser(string declaration, string origin, string clip) =>
            Assert.Equal([origin, clip], Expand("background: " + declaration, "background-origin", "background-clip"));

        [Fact]
        public void ABoxKeyword_AppliesPerLayer()
        {
            Assert.Equal(["content-box, padding-box", "content-box, padding-box"],
                Expand("background: url(a.png) content-box, url(b.png) padding-box", "background-origin", "background-clip"));
            Assert.Equal(["content-box, padding-box", "content-box, border-box"],
                Expand("background: url(a.png) content-box, url(b.png)", "background-origin", "background-clip"));
        }

        [Theory]
        [InlineData("url(a.png) / 10px")]
        [InlineData("url(a.png) / 10px 20px")]
        [InlineData("/ cover")]
        [InlineData("url(a.png) no-repeat / 10px")]
        [InlineData("url(a.png) 0 0 / 10px, url(b.png) / 5px")]
        public void ASizeWithNoPositionBeforeItsSlash_DropsTheDeclaration(string declaration) =>
            Assert.Equal([""], Expand("background: " + declaration, "background-size"));

        [Theory]
        [InlineData("url(a.png) 0 0 / 10px", "10px")]
        [InlineData("url(a.png) center / 10px 20px", "10px 20px")]
        [InlineData("url(a.png) left 10px top / cover", "cover")]
        [InlineData("10px / 50% url(a.png)", "50%")]
        [InlineData("url(a.png) 10px", "initial")]
        public void ASizeAfterAPosition_IsStillAccepted(string declaration, string size) =>
            Assert.Equal([size], Expand("background: " + declaration, "background-size"));
    }
}
