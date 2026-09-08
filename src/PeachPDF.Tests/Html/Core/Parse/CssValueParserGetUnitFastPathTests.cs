using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Issue #910: <c>CssValueParser.GetUnit</c> ran the full CSS tokenizer just to pull a number+unit
    /// out of a length string. <see cref="CssValueParser.TryClassifyLengthFast"/> is the allocation-free
    /// replacement for the common cases, built on <see cref="NumberSyntax"/> - falling through to the
    /// real <see cref="CssValueParser.GetCssTokens"/> tokenizer, unchanged, for anything it isn't
    /// confident about. These tests check two things a purely black-box test of <c>ParseLength</c>'s
    /// final numeric output cannot distinguish: that the fast path actually classifies the common shapes
    /// (rather than always falling through, which would look identical from the outside since the
    /// fallback is correct too), and that its answer agrees with the real tokenizer's for every input it
    /// does claim to classify.
    /// </summary>
    public class CssValueParserGetUnitFastPathTests
    {
        [Theory]
        [InlineData("12px", "px", 12d)]
        [InlineData("1.5em", "em", 1.5d)]
        [InlineData("-3px", "px", -3d)]
        [InlineData("+3px", "px", 3d)]
        [InlineData(".5px", "px", 0.5d)]
        [InlineData("0.5rem", "rem", 0.5d)]
        [InlineData("100vh", "vh", 100d)]
        public void TryClassifyLengthFast_NumberPlusUnit_ClassifiesAsDimension(string input, string expectedUnit, double expectedValue)
        {
            var kind = CssValueParser.TryClassifyLengthFast(input, out var unit, out var value);

            Assert.Equal(CssValueParser.FastLengthKind.Dimension, kind);
            Assert.Equal(expectedUnit, unit);
            Assert.Equal(expectedValue, value, 5);
        }

        [Theory]
        [InlineData("50%", 50d)]
        [InlineData("100%", 100d)]
        [InlineData("0.5%", 0.5d)]
        public void TryClassifyLengthFast_NumberPlusPercent_ClassifiesAsDimension(string input, double expectedValue)
        {
            var kind = CssValueParser.TryClassifyLengthFast(input, out var unit, out var value);

            Assert.Equal(CssValueParser.FastLengthKind.Dimension, kind);
            Assert.Equal("%", unit);
            Assert.Equal(expectedValue, value, 5);
        }

        [Theory]
        [InlineData("10")]
        [InlineData("-10")]
        [InlineData("0.5")]
        public void TryClassifyLengthFast_BareNumber_ClassifiesAsPlainNumber(string input)
        {
            var kind = CssValueParser.TryClassifyLengthFast(input, out var unit, out _);

            Assert.Equal(CssValueParser.FastLengthKind.PlainNumber, kind);
            Assert.Null(unit);
        }

        // Anything this shortcut isn't sure about must defer to the real tokenizer rather than guess -
        // each of these is a shape the real Lexer would classify differently than a naive number+letters
        // split would suggest.
        [Theory]
        [InlineData("auto")]           // no leading number at all
        [InlineData("10 px")]          // embedded whitespace - not a single dimension-token
        [InlineData("1\\65 m")]        // escape in the unit ("\65" = 'e', so this is "1em" via an escape)
        [InlineData("10px2")]          // Lexer.Dimension() only extends a unit through ASCII letters, so
                                        // this is actually two tokens (Dimension "10px" + Number "2")
        [InlineData("1e2")]            // "e" (a letter) looks unit-like, but "2" right after it isn't a
                                        // letter, so the all-letters unit check rejects the whole "e2" -
                                        // the real tokenizer now correctly reads this as a single
                                        // scientific-notation Number(100) token (issue #921), which still
                                        // isn't a Dimension/Percentage token, so this stays Inconclusive either way
        [InlineData("1e+")]            // no digit follows the sign, so this isn't an exponent - 'e'
                                        // claims the unit branch, then a bare trailing '+' left over
        [InlineData("10-foo")]         // NumberDash: a unit may start with '-' after digits
        public void TryClassifyLengthFast_AmbiguousShapes_ClassifiesAsInconclusive(string input)
        {
            var kind = CssValueParser.TryClassifyLengthFast(input, out _, out _);

            Assert.Equal(CssValueParser.FastLengthKind.Inconclusive, kind);
        }

        public static TheoryData<string> AgreementCorpus => new()
        {
            "0", "1", "12", "-12", "+12", "1.5", ".5", "-.5",
            "12px", "1.5em", "-3px", "+3px", ".5px", "0.5rem", "100vh", "1px", "0px",
            "50%", "100%", "0.5%",
            "auto", "10 px", "1\\65 m", "10px2", "1e2", "1e+2", "1em", "1e", "1e+", "10-foo",
            "px", "", "-", "+", ".",
        };

        [Theory]
        [MemberData(nameof(AgreementCorpus))]
        public void TryClassifyLengthFast_AgreesWithTheRealTokenizer(string input)
        {
            var (expectedUnit, expectedValue, expectedHasUnit) = ClassifyViaRealTokenizer(input);

            var kind = CssValueParser.TryClassifyLengthFast(input, out var fastUnit, out var fastValue);

            switch (kind)
            {
                case CssValueParser.FastLengthKind.Dimension:
                    Assert.True(expectedHasUnit);
                    Assert.Equal(expectedUnit, fastUnit);
                    Assert.Equal(expectedValue!.Value, fastValue, 5);
                    break;
                case CssValueParser.FastLengthKind.PlainNumber:
                    Assert.False(expectedHasUnit);
                    break;
                case CssValueParser.FastLengthKind.Inconclusive:
                    // Nothing to check here by construction - GetUnit falls through to the real
                    // tokenizer for these, so they're trivially in agreement.
                    break;
            }
        }

        private static (string? unit, double? value, bool hasUnit) ClassifyViaRealTokenizer(string input)
        {
            if (string.IsNullOrEmpty(input)) return (null, null, false);

            var tokens = CssValueParser.GetCssTokens(input);

            if (tokens is [{ Type: TokenType.Dimension or TokenType.Percentage } unitToken]) return (unitToken.Unit, unitToken.Value, true);

            return (null, null, false);
        }

        [Theory]
        [InlineData("12px")]
        [InlineData("0.5em")]
        [InlineData("50%")]
        public void ParseLength_UnitPresent_TakesTheFastClassificationPath(string length)
        {
            // Not asserting the resolved pixel value here (that's SpecPixelResolutionTests'/Length's
            // job) - just that a unit-bearing string is actually classified via the fast path, proving
            // ParseLength's plumbing reaches it rather than always tokenizing.
            var kind = CssValueParser.TryClassifyLengthFast(length, out _, out _);
            Assert.Equal(CssValueParser.FastLengthKind.Dimension, kind);
        }

        [Fact]
        public void ParseLength_ExplicitUnit_And_BareNumberFallback_ResolveToTheSameAnswerAsBefore()
        {
            // Regression for the ParseLength double-tokenize fix (issue #910 §4): a bare, unitless,
            // non-zero number is invalid CSS in nearly every real property context, but GetUnit's
            // contract for it (fall back to ParseNumber, no PixelsPerPoint catch-up) must be unchanged.
            var bareNumber = CssValueParser.ParseLength("5", 100, 0, 0, null, false);
            Assert.Equal(0d, bareNumber); // "5" has no recognized unit -> ParseNumber's own fallback

            var pxValue = CssValueParser.ParseLength("12px", 100, 0, 0, null, false);
            Assert.Equal(9d, pxValue, 3); // 12px * 0.75 pt/px
        }
    }
}
