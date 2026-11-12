using PeachBrackets.Css;

namespace PeachBrackets.Engine.Tests.CSS.PropertyTests
{
    public class FlexPropertyTests
    {

        [Fact]
        public void JustifyAlign_Parses()
        {
            string css = """
html {
    justify-content: center;
    align-items: center;
    align-content: center;
    align-self: center;
}
""";
            var stylesheet = new StylesheetParser().Parse(css);

            var info = (StyleRule)stylesheet.StyleRules.First();

            Assert.Equal(@"center", info.Style.AlignItems);
            Assert.Equal(@"center", info.Style.AlignContent);
            Assert.Equal(@"center", info.Style.AlignSelf);
            Assert.Equal(@"center", info.Style.JustifyContent);
        }

        [Fact]
        public void FlexAuto_Parses()
        {
            string css = """
html {
    flex: auto;
}
""";

            var stylesheet = new StylesheetParser().Parse(css);
            var info = (StyleRule)stylesheet.StyleRules.First();
            Assert.Equal(@"1 1 auto", info.Style.Flex);
        }

        // css-cascade 4 §7.3: a CSS-wide keyword reaches every longhand of the shorthand as itself (#2331).
        [Theory]
        [InlineData("initial")]
        [InlineData("inherit")]
        [InlineData("unset")]
        [InlineData("revert")]
        public void FlexCssWideKeyword_ReachesAllThreeLonghands(string keyword)
        {
            var stylesheet = new StylesheetParser().Parse($"html {{ flex: {keyword}; }}");
            var info = (StyleRule)stylesheet.StyleRules.First();

            Assert.Equal(keyword, info.Style.FlexGrow);
            Assert.Equal(keyword, info.Style.FlexShrink);
            Assert.Equal(keyword, info.Style.FlexBasis);
        }
    }
}








