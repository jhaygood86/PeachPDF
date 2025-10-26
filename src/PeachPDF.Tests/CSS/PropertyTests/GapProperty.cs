using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class GapPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [MemberData(nameof(GapTestValues))]
        public void GapLegalValues(string value)
            => TestForLegalValue<GapProperty>(PropertyNames.Gap, value);

        [Theory]
        [MemberData(nameof(GapExpandedTestValues))]
        public void GapShorthandValueExpanded(string propertyValue, string expectedRowGap, string expectedColumnGap)
        {
            var source = $".test {{ gap: {propertyValue}; }}";
            var styleSheet = ParseStyleSheet(source);
            var rule = styleSheet.StyleRules.First() as StyleRule;

            Assert.Equal(rule.Style.Gap, propertyValue);
            Assert.Equal(rule.Style.RowGap, expectedRowGap);
            Assert.Equal(rule.Style.ColumnGap, expectedColumnGap);
        }

        public static IEnumerable<object[]> GapTestValues
        {
            get
            {
                return new[]
                {
                    new object[] { "20px 10px" },
                    new object[] { "1em 0.5em" },
                    new object[] { "3vmin 2vmax" },
                    new object[] { "3vmin" },
                    new object[] { "0.5cm" },
                    new object[] { "0.5cm 2mm" },
                    new object[] { "16% 100%" },
                    new object[] { "21px 82%" }
                }.Union(LengthOrPercentOrGlobalTestValues.Union(GlobalKeywordTestValues.ToObjectArray(), ObjectArrayComparer.Instance), ObjectArrayComparer.Instance);
            }
        }

        public static IEnumerable<object[]> GapExpandedTestValues
        {
            get
            {
                return new[]
                {
                    new object[] { "20px 10px", "20px", "10px" },
                    new object[] { "1em 0.5em", "1em", "0.5em" },
                    new object[] { "3vmin 2vmax", "3vmin", "2vmax" },
                    new object[] { "3vmin", "3vmin", "3vmin" },
                    new object[] { "0.5cm", "0.5cm", "0.5cm" },
                    new object[] { "0.5cm 2mm", "0.5cm", "2mm" },
                    new object[] { "16% 100%", "16%", "100%" },
                    new object[] { "21px 82%", "21px", "82%" },
                    new object[] { "initial inherit", "initial", "inherit" },
                    new object[] { "unset revert-layer", "unset", "revert-layer" },
                };
            }
        }
    }
}








