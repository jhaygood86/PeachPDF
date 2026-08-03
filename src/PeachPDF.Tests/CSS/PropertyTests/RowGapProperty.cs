using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.PropertyTests
{
    public class RowGapPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [MemberData(nameof(LengthOrPercentOrGlobalTestValues))]
        public void RowGapLegalValues(string value)
            => TestForLegalValue<RowGapProperty>(PropertyNames.RowGap, value);

        [Fact]
        public void RowGapAcceptsNormalKeyword()
            => TestForLegalValue<RowGapProperty>(PropertyNames.RowGap, "normal");
    }
}








