using PeachPDF.MathML;
using Xunit;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// Coverage for <see cref="MathOperatorDictionary"/> - values spot-checked against MathML Core
    /// Appendix B's own Figure 25/26 tables (read independently from
    /// <c>https://w3c.github.io/mathml-core/#operator-dictionary</c>, not derived from this
    /// implementation), representing a spread of categories/forms.
    /// </summary>
    public class MathOperatorDictionaryTests
    {
        static double Em(MathLength length) => length.Unit == MathLengthUnit.Em ? length.Value : double.NaN;

        [Fact]
        public void Classify_PlusSign_IsMediumMathSpaceInfix_CategoryB()
        {
            var entry = MathOperatorDictionary.Classify("+", "infix");
            Assert.Equal(4.0 / 18, Em(entry.LSpace), 6);
            Assert.Equal(4.0 / 18, Em(entry.RSpace), 6);
            Assert.False(entry.Stretchy);
        }

        [Fact]
        public void Classify_Arrow_IsThickMathSpaceInfix_Stretchy_CategoryA()
        {
            var entry = MathOperatorDictionary.Classify("→", "infix"); // RIGHTWARDS ARROW
            Assert.Equal(5.0 / 18, Em(entry.LSpace), 6);
            Assert.True(entry.Stretchy);
        }

        [Fact]
        public void Classify_OpenParen_PrefixFence_IsStretchySymmetric_ZeroSpace_CategoryF()
        {
            var entry = MathOperatorDictionary.Classify("(", "prefix");
            Assert.Equal(0, Em(entry.LSpace));
            Assert.Equal(0, Em(entry.RSpace));
            Assert.True(entry.Stretchy);
            Assert.True(entry.Symmetric);
        }

        [Fact]
        public void Classify_Integral_PrefixLargeOp_CategoryH()
        {
            var entry = MathOperatorDictionary.Classify("∫", "prefix"); // INTEGRAL
            Assert.Equal(3.0 / 18, Em(entry.LSpace), 6);
            Assert.True(entry.LargeOp);
            Assert.True(entry.Symmetric);
            Assert.False(entry.MovableLimits);
        }

        [Fact]
        public void Classify_Sum_PrefixLargeOpMovableLimits_CategoryJ()
        {
            var entry = MathOperatorDictionary.Classify("∑", "prefix"); // N-ARY SUMMATION
            Assert.True(entry.LargeOp);
            Assert.True(entry.MovableLimits);
        }

        [Fact]
        public void Classify_Comma_InfixSeparator_ThinMathSpaceRspaceOnly_CategoryM()
        {
            var entry = MathOperatorDictionary.Classify(",", "infix");
            Assert.Equal(0, Em(entry.LSpace));
            Assert.Equal(3.0 / 18, Em(entry.RSpace), 6);
        }

        [Fact]
        public void Classify_TwoAsciiCharOperator_LogicalAnd_IsRecognized()
        {
            // "&&" is one of Operators_2_ascii_chars (Figure 24), at index 2 - remapped to the synthetic
            // codepoint U+0320+2 = U+0322 before category classification, which category B's own range
            // table happens to separately list (alongside +/-/±/÷) - confirming the remap step actually
            // participates in classification, not just a no-op fallback to Default.
            var entry = MathOperatorDictionary.Classify("&&", "infix");
            Assert.Equal(4.0 / 18, Em(entry.LSpace), 6);
        }

        [Fact]
        public void Classify_UnrecognizedCharacter_FallsBackToDefault_ThickMathSpaceBothSides()
        {
            var entry = MathOperatorDictionary.Classify("Q", "infix");
            Assert.Equal(5.0 / 18, Em(entry.LSpace), 6);
            Assert.Equal(5.0 / 18, Em(entry.RSpace), 6);
            Assert.False(entry.Stretchy);
        }

        [Fact]
        public void Classify_MultiCharacterOperator_FallsBackToDefault()
        {
            var entry = MathOperatorDictionary.Classify("lim", "prefix");
            Assert.Equal(5.0 / 18, Em(entry.LSpace), 6);
        }

        [Fact]
        public void Classify_SingleCharacterInAlwaysDefaultRange_FallsBackToDefault()
        {
            // U+0320-U+03FF is unconditionally excluded from classification for single-character content
            // (MathML Core Appendix B.1 step 2) - it's reachable only via the 2-character combining
            // overlay / two-ASCII-char remap paths, never as a bare single character.
            var entry = MathOperatorDictionary.Classify(((char)0x0331).ToString(), "postfix"); // COMBINING MACRON BELOW
            Assert.Equal(5.0 / 18, Em(entry.LSpace), 6);
        }

        [Fact]
        public void Classify_CombiningNegationOverlay_ClassifiesByBaseCharacter()
        {
            // "=" (category C when alone) followed by U+0338 COMBINING LONG SOLIDUS OVERLAY (e.g. "≠"
            // authored as a base character plus overlay) classifies by the base character per the
            // 2-character remap step.
            var withOverlay = MathOperatorDictionary.Classify("≠", "infix");
            var baseOnly = MathOperatorDictionary.Classify("=", "infix");
            Assert.Equal(Em(baseOnly.LSpace), Em(withOverlay.LSpace), 6);
        }

        [Fact]
        public void Classify_ArabicSurrogatePairSpecialCase_ResolvesToCategoryI()
        {
            // U+1EEF0 ARABIC MATHEMATICAL OPERATOR MEEM WITH HAH WITH TATWEEL, as postfix - the spec's
            // one hardcoded special case, bypassing the general range lookup entirely.
            var arabicOperator = char.ConvertFromUtf32(0x1EEF0);
            var entry = MathOperatorDictionary.Classify(arabicOperator, "postfix");
            Assert.True(entry.Stretchy); // category I is Stretchy
        }

        [Fact]
        public void Classify_UnrecognizedTwoCharacterContent_FallsBackToDefault()
        {
            var entry = MathOperatorDictionary.Classify("ab", "infix");
            Assert.Equal(5.0 / 18, Em(entry.LSpace), 6);
        }

        [Fact]
        public void Classify_SameCharacterDifferentForm_CanResolveDifferentCategories()
        {
            // "+" as infix (category B, mediummathspace) vs prefix (category D, zero space) - the
            // classification genuinely depends on form, not just content.
            var infix = MathOperatorDictionary.Classify("+", "infix");
            var prefix = MathOperatorDictionary.Classify("+", "prefix");
            Assert.Equal(4.0 / 18, Em(infix.LSpace), 6);
            Assert.Equal(0, Em(prefix.LSpace));
        }
    }
}
