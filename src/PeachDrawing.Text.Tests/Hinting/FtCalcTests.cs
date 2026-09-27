using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The fixed-point arithmetic of FreeType, ported bit for bit: <c>FT_MulFix</c>, <c>FT_DivFix</c>, <c>FT_MulDiv</c>, <c>FT_MulDiv_No_Round</c>
    /// and the vector length, against what FreeType 2.14.3 returns for the corner cases of a 32-bit <c>FT_Long</c> and for random values in
    /// the ranges the interpreter works in (<c>assets/fonts/generate_hinting_arithmetic_golden.py</c>).
    /// </summary>
    public class FtCalcTests
    {
        private static readonly Lazy<ArithmeticGolden> Golden = new(() =>
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "HintingArithmetic.golden.json.gz"));
            using var zip = new GZipStream(stream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<ArithmeticGolden>(zip)!;
        });

        [Fact]
        public void MulFixIsFreeTypesMulFix()
        {
            foreach (var c in Golden.Value.MulFix)
                Assert.True(FtCalc.MulFix(c[0], c[1]) == c[2], $"MulFix({c[0]}, {c[1]}) = {FtCalc.MulFix(c[0], c[1])}, FreeType has {c[2]}");
        }

        [Fact]
        public void DivFixIsFreeTypesDivFix()
        {
            foreach (var c in Golden.Value.DivFix)
                Assert.True(FtCalc.DivFix(c[0], c[1]) == c[2], $"DivFix({c[0]}, {c[1]}) = {FtCalc.DivFix(c[0], c[1])}, FreeType has {c[2]}");
        }

        [Fact]
        public void MulDivIsFreeTypesMulDiv()
        {
            foreach (var c in Golden.Value.MulDiv)
                Assert.True(FtCalc.MulDiv(c[0], c[1], c[2]) == c[3], $"MulDiv({c[0]}, {c[1]}, {c[2]}) = {FtCalc.MulDiv(c[0], c[1], c[2])}, FreeType has {c[3]}");
        }

        [Fact]
        public void HypotIsFreeTypesVectorLength()
        {
            foreach (var c in Golden.Value.Length)
                Assert.True(FtTrigon.Hypot(c[0], c[1]) == c[2], $"Hypot({c[0]}, {c[1]}) = {FtTrigon.Hypot(c[0], c[1])}, FreeType has {c[2]}");
        }

        [Fact]
        public void TheReferenceHasEnoughCases()
        {
            Assert.True(Golden.Value.MulFix.Count > 3000 && Golden.Value.DivFix.Count > 3000 && Golden.Value.MuLDivCount > 5000 && Golden.Value.Length.Count > 4000);
        }

        // Behaviour FreeType documents in ftcalc.c and freetype.h

        [Theory]
        [InlineData(0x10000, 12345, 12345)]       // multiplying by 1.0 is the identity
        [InlineData(0x10000, -12345, -12345)]
        [InlineData(0x8000, 3, 2)]                // 3 * 0.5 = 1.5, rounded away from zero
        [InlineData(0x8000, -3, -2)]              // ... symmetric for negative values
        [InlineData(0x8000, 1, 1)]                // 0.5 rounds up
        [InlineData(0x8000, -1, -1)]
        [InlineData(0, 12345, 0)]
        public void MulFixRoundsToNearestAndSymmetrically(int a, int b, int expected) => Assert.Equal(expected, FtCalc.MulFix(a, b));

        [Theory]
        [InlineData(1, 0, 0x7FFFFFFF)]            // division by zero: the largest positive value
        [InlineData(-1, 0, -0x7FFFFFFF)]
        [InlineData(0, 0, 0x7FFFFFFF)]
        [InlineData(1, 2, 0x8000)]                // 1 / 2 in 16.16
        [InlineData(-1, 2, -0x8000)]
        [InlineData(3, 2, 0x18000)]
        public void DivFixDividesInto16Dot16(int a, int b, int expected) => Assert.Equal(expected, FtCalc.DivFix(a, b));

        [Theory]
        [InlineData(7, 3, 2, 11)]                 // 21 / 2 = 10.5, rounded to 11
        [InlineData(-7, 3, 2, -11)]
        [InlineData(7, -3, 2, -11)]
        [InlineData(1, 1, 0, 0x7FFFFFFF)]         // c == 0
        [InlineData(0x7FFFFFFF, 0x7FFFFFFF, 0x7FFFFFFF, 0x7FFFFFFF)] // the product needs 62 bits
        public void MulDivRoundsAndSurvivesAHugeProduct(int a, int b, int c, int expected) => Assert.Equal(expected, FtCalc.MulDiv(a, b, c));

        [Theory]
        [InlineData(7, 3, 2, 10)]                 // the quotient is truncated
        [InlineData(-7, 3, 2, -10)]
        [InlineData(1, 1, 0, 0x7FFFFFFF)]
        public void MulDivNoRoundTruncates(int a, int b, int c, int expected) => Assert.Equal(expected, FtCalc.MulDivNoRound(a, b, c));

        [Theory]
        [InlineData(0u, 0)]
        [InlineData(1u, 0)]
        [InlineData(2u, 1)]
        [InlineData(0x40u, 6)]
        [InlineData(0x7FFFFFFFu, 30)]
        [InlineData(0x80000000u, 31)]
        [InlineData(uint.MaxValue, 31)]
        public void MsbFindsTheHighestSetBitAndIsZeroForZero(uint value, int expected) => Assert.Equal(expected, FtCalc.Msb(value));

        [Fact]
        public void VectorNormLenGivesAUnitVectorAndTheLength()
        {
            // (3, 4) has length 5: the vector becomes (0.6, 0.8) in 16.16, up to the precision of the iteration
            int x = 3 * 64, y = 4 * 64;
            uint length = FtCalc.VectorNormLen(ref x, ref y);

            Assert.Equal(5u * 64, length);
            Assert.InRange(x, (int)(0.6 * 65536) - 2, (int)(0.6 * 65536) + 2);
            Assert.InRange(y, (int)(0.8 * 65536) - 2, (int)(0.8 * 65536) + 2);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(100, 0)]
        [InlineData(0, -7)]
        public void VectorNormLenOnTheAxesIsExactAndAZeroVectorIsLeftAlone(int x0, int y0)
        {
            int x = x0, y = y0;
            uint length = FtCalc.VectorNormLen(ref x, ref y);

            Assert.Equal((uint)Math.Abs(x0 + y0), length);
            Assert.Equal(x0 == 0 ? 0 : Math.Sign(x0) * 0x10000, x);
            Assert.Equal(y0 == 0 ? 0 : Math.Sign(y0) * 0x10000, y);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(-33, 0, 33)]
        [InlineData(0, 5, 5)]
        public void HypotIsExactOnTheAxes(int x, int y, int expected) => Assert.Equal(expected, FtTrigon.Hypot(x, y));

        [Fact]
        public void PixelRoundingMacrosRoundLikeFreeTypesDo()
        {
            Assert.Equal(64, FtCalc.PixRound(32));
            Assert.Equal(0, FtCalc.PixRound(31));
            Assert.Equal(-64, FtCalc.PixRound(-33));   // -33 + 32 = -1, floored to -64
            Assert.Equal(-0, FtCalc.PixRound(-32));
            Assert.Equal(0, FtCalc.PixFloor(63));
            Assert.Equal(-64, FtCalc.PixFloor(-1));
            Assert.Equal(64, FtCalc.PixCeil(1));
            Assert.Equal(0, FtCalc.PixCeil(0));
            Assert.Equal(64, FtCalc.PadRound(32, 64));
        }

        private sealed class ArithmeticGolden
        {
            [JsonPropertyName("mulfix")]
            public List<int[]> MulFix { get; set; } = [];

            [JsonPropertyName("divfix")]
            public List<int[]> DivFix { get; set; } = [];

            [JsonPropertyName("muldiv")]
            public List<int[]> MulDiv { get; set; } = [];

            [JsonPropertyName("length")]
            public List<int[]> Length { get; set; } = [];

            public int MuLDivCount => MulDiv.Count;
        }
    }
}
