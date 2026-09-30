using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontWeightResolver"/> - CSS Fonts 4 §2.2.1's bolder/lighter step table,
    /// keyword/numeric passthrough. See <c>FontShorthandIntegrationTests.FontWeight_BolderLighter_StepsRelativeToRealParentWeight</c>
    /// and <c>FontFaceMatchingOrderIntegrationTests.AFractionalWeightInherits_AndBolderAndLighterStepFromIt</c>
    /// for the equivalent coverage through the real cascade (parent → child weight resolution).
    /// </summary>
    public class FontWeightResolverTests
    {
        [Theory]
        [InlineData("400", 400)]
        [InlineData("100", 100)]
        [InlineData("999", 999)]
        public void NumericValue_PassesThroughUnchanged(string value, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve(value, parentWeight: 400));
        }

        [Theory]
        [InlineData("350.5", 350.5)]
        [InlineData("1.25", 1.25)]
        [InlineData(" 999.75 ", 999.75)]
        public void FractionalValue_PassesThroughUnchanged(string value, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve(value, parentWeight: 400));
        }

        [Theory]
        [InlineData(300.5, 400)]
        [InlineData(450.5, 700)]
        [InlineData(600.5, 900)]
        [InlineData(800.5, 900)]
        public void Bolder_StepsFromAFractionalParent(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(300.5, 100)]
        [InlineData(600.5, 400)]
        [InlineData(800.5, 700)]
        public void Lighter_StepsFromAFractionalParent(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }

        // A fractional weight landing inside a band where the CSS2.1 table this resolver used to follow
        // and the CSS Fonts 4 §2.2.1 table it now follows disagree (see the *_DivergesFromTheCss21Table
        // tests below for the whole-number boundaries of these same bands).
        [Theory]
        [InlineData(380.5, 700)] // CSS2.1 would give 400 (< 400); Fonts 4's [350,550) band gives 700.
        [InlineData(520.5, 700)] // CSS2.1 would give 900 (> 500); Fonts 4's [350,550) band still gives 700.
        public void Bolder_StepsFromAFractionalParent_InABandWhereTheTablesDisagree(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(520.5, 100)] // CSS2.1 would give 400 (<= 700); Fonts 4's [100,550) band gives 100.
        [InlineData(720.5, 400)] // CSS2.1 would give 700 (> 700); Fonts 4's [550,750) band gives 400.
        public void Lighter_StepsFromAFractionalParent_InABandWhereTheTablesDisagree(double parentWeight, double expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }

        [Theory]
        [InlineData("350.5", true, 350.5)]
        [InlineData(" 12 ", true, 12)]
        [InlineData("-0.25", true, -0.25)]
        [InlineData("1e2", true, 100)]
        [InlineData("bold", false, 0)]
        [InlineData("", false, 0)]
        [InlineData("NaN", false, 0)]
        [InlineData("1e999", false, 0)]
        [InlineData("1,5", false, 0)]
        public void TryParseNumber_ReadsAFiniteNumberWithTheInvariantCulture(string text, bool expectedSuccess, double expected)
        {
            Assert.Equal(expectedSuccess, CssValueParser.TryParseNumber(text, out var result));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Bold_Resolves700_RegardlessOfParent()
        {
            Assert.Equal(700, FontWeightResolver.Resolve("bold", parentWeight: 100));
        }

        [Fact]
        public void UnrecognizedKeyword_ResolvesToNormal400()
        {
            Assert.Equal(400, FontWeightResolver.Resolve("normal", parentWeight: 900));
        }

        // The multiples of 100 that CSS2.1 §15.6's worked table tabulated are exactly where it and CSS
        // Fonts 4 §2.2.1's table agree (only the bands strictly between them, covered above and by the
        // dedicated divergence tests below, differ), so these whole-number assertions are unchanged from
        // before this resolver switched tables.
        [Theory]
        [InlineData(100, 400)]
        [InlineData(200, 400)]
        [InlineData(300, 400)]
        [InlineData(400, 700)]
        [InlineData(500, 700)]
        [InlineData(600, 900)]
        [InlineData(700, 900)]
        [InlineData(800, 900)]
        [InlineData(900, 900)]
        public void Bolder_MatchesCssFonts4WorkedTable(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(100, 100)]
        [InlineData(200, 100)]
        [InlineData(300, 100)]
        [InlineData(400, 100)]
        [InlineData(500, 100)]
        [InlineData(600, 400)]
        [InlineData(700, 400)]
        [InlineData(800, 700)]
        [InlineData(900, 700)]
        public void Lighter_MatchesCssFonts4WorkedTable(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }

        // Whole-number boundaries of the bands where CSS Fonts 4 §2.2.1 disagrees with the CSS2.1 table
        // this resolver used to follow (see the fractional-weight tests above for the same bands hit by
        // a fractional parent).
        [Theory]
        [InlineData(380, 700)] // CSS2.1: < 400 => 400. Fonts 4: [350,550) => 700.
        [InlineData(520, 700)] // CSS2.1: > 500 => 900. Fonts 4: [350,550) => 700.
        public void Bolder_DivergesFromTheCss21Table(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("bolder", parentWeight));
        }

        [Theory]
        [InlineData(520, 100)] // CSS2.1: <= 700 => 400. Fonts 4: [100,550) => 100.
        [InlineData(720, 400)] // CSS2.1: > 700 => 700. Fonts 4: [550,750) => 400.
        public void Lighter_DivergesFromTheCss21Table(int parentWeight, int expected)
        {
            Assert.Equal(expected, FontWeightResolver.Resolve("lighter", parentWeight));
        }

        // CSS Fonts 4 §2.2.1's table gives "no change" for a parent weight under 100 with `lighter` - but,
        // perhaps counterintuitively, NOT for `bolder`, which still steps a sub-100 weight up to 400 (the
        // same cell the [100,350) band resolves to). Below 100 is the one band where the two keywords'
        // rows genuinely differ from each other, not just from CSS2.1 (which has no concept of a weight
        // under 100 at all).
        [Fact]
        public void Bolder_BelowOneHundred_StepsTo400_NotNoChange()
        {
            Assert.Equal(400, FontWeightResolver.Resolve("bolder", parentWeight: 50));
        }

        [Fact]
        public void Lighter_BelowOneHundred_ReturnsNoChange()
        {
            Assert.Equal(50, FontWeightResolver.Resolve("lighter", parentWeight: 50));
        }

        [Fact]
        public void Lighter_BelowOneHundred_ReturnsNoChange_ForAFractionalParent()
        {
            Assert.Equal(12.5, FontWeightResolver.Resolve("lighter", parentWeight: 12.5));
        }

        // `bolder`'s table has a top clamp too, but it is "no change" (the inherited weight itself, which
        // can be above 900 since weights run up to 1000), not a fixed ceiling of 900.
        [Fact]
        public void Bolder_AtOrAboveNineHundred_ReturnsNoChange()
        {
            Assert.Equal(950, FontWeightResolver.Resolve("bolder", parentWeight: 950));
        }
    }
}
