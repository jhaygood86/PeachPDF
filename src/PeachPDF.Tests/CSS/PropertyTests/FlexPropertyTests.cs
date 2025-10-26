using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
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

            var info = stylesheet.StyleRules.First() as StyleRule;

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
            var info = stylesheet.StyleRules.First() as StyleRule;
            Assert.Equal(@"1 1 auto", info.Style.Flex);
        }
    }
}








