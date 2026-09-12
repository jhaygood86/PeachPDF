using PeachPDF.MathML;
using Xunit;

namespace PeachPDF.Tests.MathML
{
    /// <summary>
    /// Coverage for <see cref="MathItalicMappings"/> - values transcribed directly from MathML Core
    /// Appendix C.1 (<c>https://w3c.github.io/mathml-core/#italic-mappings</c>), spot-checked here
    /// against the same independently-read spec values rather than re-derived from this table itself.
    /// </summary>
    public class MathItalicMappingsTests
    {
        [Theory]
        [InlineData('A', 0x1D434)]
        [InlineData('Z', 0x1D44D)]
        [InlineData('a', 0x1D44E)]
        [InlineData('g', 0x1D454)]
        [InlineData('i', 0x1D456)]
        [InlineData('z', 0x1D467)]
        public void TryGetItalic_LatinRanges_MatchKnownCodepoints(char c, int expected)
        {
            Assert.True(MathItalicMappings.TryGetItalic(c, out var actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData('Α', 0x1D6E2)] // Greek capital Alpha
        [InlineData('Ω', 0x1D6FA)] // Greek capital Omega
        [InlineData('α', 0x1D6FC)] // Greek lowercase alpha
        [InlineData('ω', 0x1D714)] // Greek lowercase omega
        [InlineData('ς', 0x1D70D)] // Greek final sigma - inside the contiguous lowercase range
        public void TryGetItalic_GreekRanges_MatchKnownCodepoints(char c, int expected)
        {
            Assert.True(MathItalicMappings.TryGetItalic(c, out var actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData('h', 0x210E)]       // PLANCK CONSTANT - the one break in the Latin a-z range
        [InlineData('ı', 0x1D6A4)] // dotless i
        [InlineData('ȷ', 0x1D6A5)] // dotless j
        [InlineData('ϴ', 0x1D6F3)] // Greek capital theta symbol
        [InlineData('∇', 0x1D6FB)] // nabla
        [InlineData('∂', 0x1D715)] // partial differential
        [InlineData('ϵ', 0x1D716)] // Greek lunate epsilon symbol
        [InlineData('ϑ', 0x1D717)] // Greek theta symbol
        [InlineData('ϰ', 0x1D718)] // Greek kappa symbol
        [InlineData('ϕ', 0x1D719)] // Greek phi symbol
        [InlineData('ϱ', 0x1D71A)] // Greek rho symbol
        [InlineData('ϖ', 0x1D71B)] // Greek pi symbol
        public void TryGetItalic_NamedExceptions_MatchKnownCodepoints(char c, int expected)
        {
            Assert.True(MathItalicMappings.TryGetItalic(c, out var actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData('0')]  // digits have no italic mapping
        [InlineData('+')]  // operators have no italic mapping
        [InlineData(' ')]
        public void TryGetItalic_UnmappedCharacters_ReturnFalse(char c)
        {
            Assert.False(MathItalicMappings.TryGetItalic(c, out _));
        }
    }
}
