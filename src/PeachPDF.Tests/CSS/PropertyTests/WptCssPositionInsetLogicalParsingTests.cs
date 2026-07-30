namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Grammar coverage for the logical inset longhands (<c>inset-block-start</c>/<c>inset-block-end</c>/
    /// <c>inset-inline-start</c>/<c>inset-inline-end</c>) and their two shorthands (<c>inset-block</c>/
    /// <c>inset-inline</c>), adapted from the web-platform-tests css/css-position/parsing/inset-valid.html
    /// and inset-invalid.html fixtures.
    ///
    /// One WPT expectation deliberately isn't ported: WPT expects the unitless zero length "0" to compute
    /// to "0px" (a live getComputedStyle() convention). PeachPDF's declaration parser stores a length as
    /// declared rather than serializing a canonical computed-style string - see
    /// MarginProperty.cs's MarginZeroLegal-style tests, which already assert "0" round-trips as "0", not
    /// "0px" - so the equivalent assertion here is against "0", matching this engine's own convention
    /// rather than a browser CSSOM behavior it doesn't implement.
    /// </summary>
    public class WptCssPositionInsetLogicalParsingTests : CssConstructionFunctions
    {
        private static string DeclaredValue(string property, string value)
        {
            var sheet = ParseStyleSheet($".x {{ {property}: {value}; }}");
            var rule = sheet.Rules.OfType<StyleRule>().Single();
            return rule.Style.GetPropertyValue(property);
        }

        public static readonly TheoryData<string> InsetLonghands = new()
        {
            "inset-block-start", "inset-block-end", "inset-inline-start", "inset-inline-end",
        };

        public static readonly TheoryData<string> InsetShorthands = new()
        {
            "inset-block", "inset-inline",
        };

        // ─── Longhands: 'auto | <length-percentage>' ──────────────────────────────

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsZero(string property)
        {
            Assert.Equal("0", DeclaredValue(property, "0"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsAuto(string property)
        {
            Assert.Equal("auto", DeclaredValue(property, "auto"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsPercentage(string property)
        {
            Assert.Equal("10%", DeclaredValue(property, "10%"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsRemLength(string property)
        {
            Assert.Equal("1rem", DeclaredValue(property, "1rem"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsNegativeLength(string property)
        {
            Assert.Equal("-10px", DeclaredValue(property, "-10px"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsNegativePercentage(string property)
        {
            Assert.Equal("-20%", DeclaredValue(property, "-20%"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_AcceptsCalcExpression(string property)
        {
            Assert.False(string.IsNullOrEmpty(DeclaredValue(property, "calc(2em + 3ex)")));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_RejectsAngle(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "0deg"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_RejectsCalcAngle(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "calc(20deg)"));
        }

        [Theory]
        [MemberData(nameof(InsetLonghands))]
        public void Longhand_RejectsUnitlessNumber(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "10"));
        }

        // ─── Shorthands: same single-value grammar, plus a 1-or-2-value periodic form ──

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_SingleValue_AcceptsAuto(string property)
        {
            Assert.Equal("auto", DeclaredValue(property, "auto"));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_SingleValue_AcceptsCalcExpression(string property)
        {
            Assert.False(string.IsNullOrEmpty(DeclaredValue(property, "calc(2em + 3ex)")));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_TwoEqualValues_CollapseToOne(string property)
        {
            Assert.Equal("auto", DeclaredValue(property, "auto auto"));
            Assert.Equal("100px", DeclaredValue(property, "100px 100px"));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_TwoUnequalValues_StayTwoValues(string property)
        {
            Assert.Equal("10% -5px", DeclaredValue(property, "10% -5px"));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_RejectsAngle(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "0deg"));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_RejectsUnitlessNumber(string property)
        {
            Assert.Equal(string.Empty, DeclaredValue(property, "10"));
        }

        [Theory]
        [MemberData(nameof(InsetShorthands))]
        public void Shorthand_RejectsMixedGlobalAndOrdinaryValue(string property)
        {
            // A global keyword (inherit/initial/unset/revert) can only appear alone - CSS Cascade 4 §3.1 -
            // never combined with an ordinary value in the same multi-value shorthand.
            Assert.Equal(string.Empty, DeclaredValue(property, "inherit auto"));
            Assert.Equal(string.Empty, DeclaredValue(property, "inherit inherit"));
        }

        // ─── z-index: calc() folds to a plain rounded integer (css-position/parsing/z-index-positioned-computed.html) ──

        [Theory]
        [InlineData("calc(3 - 2)", "1")]
        [InlineData("calc(1.5 + 1.5)", "3")]
        [InlineData("calc(2 * -3)", "-6")]
        [InlineData("calc(0.5)", "1")]
        [InlineData("calc(0.4)", "0")]
        public void ZIndex_CalcExpression_FoldsToNearestInteger(string value, string expected)
        {
            Assert.Equal(expected, DeclaredValue("z-index", value));
        }

        [Fact]
        public void ZIndex_RejectsLengthCalcExpression()
        {
            // z-index's grammar is 'auto | <integer>' - a calc() is only valid when it type-checks as a
            // plain <number> (CSS Values 4 §10.9); a length-category calc() must still be rejected.
            Assert.Equal(string.Empty, DeclaredValue("z-index", "calc(1px + 1px)"));
        }
    }
}
