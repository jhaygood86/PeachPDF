using PeachPDF.Text.Bidi;
using Xunit;

namespace PeachPDF.Tests.Text.Bidi
{
    /// <summary>
    /// Unit tests for <see cref="BidiMirrorResolver.ApplyMirroring"/>: UAX #9 L4 (mirrored characters) plus
    /// the within-run L2 character reversal a caller needs before painting one RTL run left-to-right (see
    /// the class doc comment for why both are folded into one call).
    /// </summary>
    public class BidiMirrorResolverTests
    {
        [Fact]
        public void ApplyMirroring_EvenLevel_ReturnsTextUnchanged()
        {
            Assert.Equal("(abc)", BidiMirrorResolver.ApplyMirroring("(abc)", 0));
        }

        [Fact]
        public void ApplyMirroring_OddLevel_ReversesAndMirrorsBrackets()
        {
            // Logical "(abc)" read right-to-left visually reverses the enclosed content to "cba", but the
            // outer parens land back where they started once each is also swapped for its mirror image:
            // reversing puts the original "(" on the right and the original ")" on the left, and mirroring
            // then swaps each of those in place - so the two effects compose to "(cba)", not ")cba(".
            var result = BidiMirrorResolver.ApplyMirroring("(abc)", 1);
            Assert.Equal("(cba)", result);
        }

        [Fact]
        public void ApplyMirroring_OddLevel_NoMirrorableCharacters_JustReverses()
        {
            Assert.Equal("cba", BidiMirrorResolver.ApplyMirroring("abc", 1));
        }

        [Fact]
        public void ApplyMirroring_OddLevel_MismatchedBrackets_EachMirrorsIndependently()
        {
            // "(" and "]" don't form a matching pair, so unlike the symmetric case above the reverse+mirror
            // composition doesn't cancel out - this proves mirroring is applied per-character, independent
            // of any bracket-pairing logic.
            Assert.Equal("[a)", BidiMirrorResolver.ApplyMirroring("(a]", 1));
        }

        [Fact]
        public void ApplyMirroring_EmptyString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, BidiMirrorResolver.ApplyMirroring(string.Empty, 1));
        }
    }
}
