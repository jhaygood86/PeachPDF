using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class ColumnGapPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [MemberData(nameof(LengthOrPercentOrGlobalTestValues))]
        public void ColumnGapLegalValues(string value)
            => TestForLegalValue<ColumnGapProperty>(PropertyNames.ColumnGap, value);

        [Fact]
        public void ColumnGapAcceptsNormalKeyword()
            => TestForLegalValue<ColumnGapProperty>(PropertyNames.ColumnGap, "normal");
    }
}
