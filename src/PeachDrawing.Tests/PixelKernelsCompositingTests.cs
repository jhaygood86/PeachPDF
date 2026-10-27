using System.Runtime.Intrinsics;

namespace PeachDrawing.Tests
{
    public class PixelKernelsCompositingTests
    {
        private static byte[] RandomPixels(Random random, int pixels)
        {
            var bytes = new byte[pixels * 4];
            random.NextBytes(bytes);
            for (var i = 0; i < pixels; i++)
            {
                // Mix in the extremes, and keep some colours above alpha: the kernels must agree on any bytes.
                switch (random.Next(6))
                {
                    case 0: bytes[i * 4 + 3] = 0; break;
                    case 1: bytes[i * 4 + 3] = 255; break;
                }
            }

            return bytes;
        }

        // The formulas FilterOps.PorterDuff used before it called the kernel, kept here as the independent reference.
        private static byte[] ReferencePorterDuff(PorterDuffOp op, byte[] a, byte[] b, bool skipTransparentSource)
        {
            var result = new byte[a.Length];
            for (var i = 0; i + 3 < a.Length; i += 4)
            {
                int aa = a[i + 3];
                int ab = b[i + 3];
                if (skipTransparentSource && aa == 0)
                {
                    Array.Copy(b, i, result, i, 4);
                    continue;
                }

                for (var c = 0; c < 4; c++)
                {
                    int ca = a[i + c];
                    int cb = b[i + c];
                    var v = op switch
                    {
                        PorterDuffOp.In => PixelKernels.Div255(ca * ab),
                        PorterDuffOp.Out => PixelKernels.Div255(ca * (255 - ab)),
                        PorterDuffOp.Atop => PixelKernels.Div255(ca * ab) + PixelKernels.Div255(cb * (255 - aa)),
                        PorterDuffOp.Xor => PixelKernels.Div255(ca * (255 - ab)) + PixelKernels.Div255(cb * (255 - aa)),
                        _ => ca + PixelKernels.Div255(cb * (255 - aa)),
                    };
                    result[i + c] = (byte)Math.Min(v, 255);
                }
            }

            return result;
        }

        public static TheoryData<int, bool> Operators()
        {
            var data = new TheoryData<int, bool>();
            foreach (var op in Enum.GetValues<PorterDuffOp>())
            {
                data.Add((int)op, false);
                data.Add((int)op, true);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public void PorterDuff_MatchesTheOriginalFormulaForAllLengths(int opValue, bool skipTransparentSource)
        {
            var op = (PorterDuffOp)opValue;
            var random = new Random(42);
            for (var pixels = 0; pixels <= 70; pixels++)
            {
                for (var trial = 0; trial < 6; trial++)
                {
                    var a = RandomPixels(random, pixels);
                    var b = RandomPixels(random, pixels);
                    var expected = ReferencePorterDuff(op, a, b, skipTransparentSource);

                    var actual = new byte[a.Length];
                    PixelKernels.PorterDuff(op, a, b, actual, skipTransparentSource);

                    Assert.Equal(expected, actual);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public void PorterDuff_VectorMatchesScalar(int opValue, bool skipTransparentSource)
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var op = (PorterDuffOp)opValue;
            var random = new Random(7);
            var a = RandomPixels(random, 64);
            var b = RandomPixels(random, 64);

            var scalar = new byte[a.Length];
            PixelKernels.PorterDuffScalar(op, a, b, scalar, skipTransparentSource, a.Length);

            var vector = new byte[a.Length];
            var done = PixelKernels.PorterDuffVector128(op, a, b, vector, skipTransparentSource, a.Length);

            Assert.Equal(a.Length, done);
            Assert.Equal(scalar, vector);
        }

        [Fact]
        public void PorterDuff_WritingIntoItsSecondInput_GivesTheSameBytesAsASeparateResult()
        {
            var random = new Random(99);
            var top = RandomPixels(random, 37);
            var bottom = RandomPixels(random, 37);

            var separate = new byte[top.Length];
            PixelKernels.PorterDuff(PorterDuffOp.Over, top, bottom, separate, skipTransparentSource: true);

            var inPlace = (byte[])bottom.Clone();
            PixelKernels.PorterDuff(PorterDuffOp.Over, top, inPlace, inPlace, skipTransparentSource: true);

            Assert.Equal(separate, inPlace);
        }

        [Fact]
        public void ScaleCoverage_MatchesDiv255ForEveryCoverageAndOpacity()
        {
            var coverage = new byte[256];
            for (var i = 0; i < coverage.Length; i++)
                coverage[i] = (byte)i;

            for (var opacity = 0; opacity < 256; opacity++)
            {
                var actual = (byte[])coverage.Clone();
                PixelKernels.ScaleCoverage(actual, opacity);

                for (var i = 0; i < 256; i++)
                    Assert.Equal(PixelKernels.Div255(i * opacity), actual[i]);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(16)]
        [InlineData(31)]
        [InlineData(33)]
        public void ScaleCoverage_HandlesLengthsAroundTheVectorWidth(int length)
        {
            var random = new Random(length);
            var coverage = new byte[length];
            random.NextBytes(coverage);

            var expected = (byte[])coverage.Clone();
            PixelKernels.ScaleCoverageScalar(expected, 100);

            PixelKernels.ScaleCoverage(coverage, 100);

            Assert.Equal(expected, coverage);
        }

        [Fact]
        public void EraseSpan_VectorMatchesScalar_ForAllLengths()
        {
            var random = new Random(5);
            for (var length = 0; length <= 70; length++)
            {
                for (var trial = 0; trial < 10; trial++)
                {
                    var coverage = new byte[length];
                    random.NextBytes(coverage);
                    for (var i = 0; i < length; i++)
                        if (random.Next(4) == 0)
                            coverage[i] = (byte)(random.Next(2) * 255);

                    var start = RandomPixels(random, length);

                    var scalar = (byte[])start.Clone();
                    PixelKernels.EraseSpanScalar(scalar, coverage);

                    var actual = (byte[])start.Clone();
                    PixelKernels.EraseSpan(actual, coverage);

                    Assert.Equal(scalar, actual);

                    for (var i = 0; i < length; i++)
                    {
                        var keep = 255 - coverage[i];
                        for (var c = 0; c < 4; c++)
                            Assert.Equal(PixelKernels.Div255(start[i * 4 + c] * keep), actual[i * 4 + c]);
                    }
                }
            }
        }

        [Fact]
        public void EraseSpan_WithAShortDestination_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelKernels.EraseSpan(new byte[7], new byte[2]));
        }
    }
}
