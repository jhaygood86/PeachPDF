using PeachPDF.Raster;
using System.Runtime.Intrinsics;

namespace PeachPDF.Tests.Raster
{
    public class PixelKernelsTests
    {
        [Fact]
        public void Div255_IsExactRoundedDivisionForEveryProductOfTwoBytes()
        {
            for (var x = 0; x <= 255 * 255; x++)
                Assert.Equal((int)Math.Round(x / 255.0, MidpointRounding.AwayFromZero), PixelKernels.Div255(x));
        }

        private static byte[] RandomBytes(Random random, int length)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

        /// <summary>Premultiplied pixels: colour channels never exceed alpha, as real surfaces guarantee.</summary>
        private static byte[] RandomPremultiplied(Random random, int pixels)
        {
            var bytes = new byte[pixels * 4];
            for (var i = 0; i < pixels; i++)
            {
                var a = random.Next(256);
                if (random.Next(4) == 0) a = random.Next(2) == 0 ? 0 : 255;
                bytes[i * 4] = (byte)random.Next(a + 1);
                bytes[i * 4 + 1] = (byte)random.Next(a + 1);
                bytes[i * 4 + 2] = (byte)random.Next(a + 1);
                bytes[i * 4 + 3] = (byte)a;
            }

            return bytes;
        }

        private static byte[] RandomCoverage(Random random, int length)
        {
            var cov = RandomBytes(random, length);
            for (var i = 0; i < length; i++)
            {
                switch (random.Next(5))
                {
                    case 0: cov[i] = 0; break;
                    case 1: cov[i] = 255; break;
                }
            }

            return cov;
        }

        [Fact]
        public void BlendSolid_VectorMatchesScalar_ForAllLengthsAndColours()
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var random = new Random(1234);
            for (var length = 0; length <= 70; length++)
            {
                for (var trial = 0; trial < 20; trial++)
                {
                    var a = random.Next(256);
                    var color = PixelKernels.Pack((byte)random.Next(a + 1), (byte)random.Next(a + 1), (byte)random.Next(a + 1), (byte)a);
                    var coverage = RandomCoverage(random, length);
                    var start = RandomPremultiplied(random, length);

                    var scalar = (byte[])start.Clone();
                    PixelKernels.BlendSolidScalar(scalar, coverage, color);

                    var vector = (byte[])start.Clone();
                    var done = length >= 4 ? PixelKernels.BlendSolidVector128(vector, coverage, color) : 0;
                    PixelKernels.BlendSolidScalar(vector, coverage, color, done);

                    Assert.Equal(scalar, vector);
                }
            }
        }

        [Fact]
        public void BlendSpan_VectorMatchesScalar_ForAllLengths()
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var random = new Random(99);
            for (var length = 0; length <= 70; length++)
            {
                for (var trial = 0; trial < 20; trial++)
                {
                    var source = RandomPremultiplied(random, length);
                    var coverage = RandomCoverage(random, length);
                    var start = RandomPremultiplied(random, length);

                    var scalar = (byte[])start.Clone();
                    PixelKernels.BlendSpanScalar(scalar, source, coverage);

                    var vector = (byte[])start.Clone();
                    var done = length >= 4 ? PixelKernels.BlendSpanVector128(vector, source, coverage) : 0;
                    PixelKernels.BlendSpanScalar(vector, source, coverage, done);

                    Assert.Equal(scalar, vector);
                }
            }
        }

        [Fact]
        public void BlendSolid_Avx2PathMatchesScalar_AndSoDoesTheDispatcherAtEveryLength()
        {
            var random = new Random(4321);
            for (var length = 0; length <= 90; length++)
            {
                for (var trial = 0; trial < 20; trial++)
                {
                    var a = random.Next(256);
                    var color = PixelKernels.Pack((byte)random.Next(a + 1), (byte)random.Next(a + 1), (byte)random.Next(a + 1), (byte)a);
                    var coverage = RandomCoverage(random, length);
                    var start = RandomPremultiplied(random, length);

                    var scalar = (byte[])start.Clone();
                    PixelKernels.BlendSolidScalar(scalar, coverage, color);

                    // The public entry point picks the widest path the machine has (256-bit, then 128-bit, then scalar for the tail).
                    var dispatched = (byte[])start.Clone();
                    PixelKernels.BlendSolid(dispatched, coverage, color);
                    Assert.Equal(scalar, dispatched);

                    if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && length >= 8)
                    {
                        var wide = (byte[])start.Clone();
                        var done = PixelKernels.BlendSolidVector256(wide, coverage, color);
                        PixelKernels.BlendSolidScalar(wide, coverage, color, done);
                        Assert.Equal(scalar, wide);
                    }
                }
            }
        }

        [Fact]
        public void BlendSpan_Avx2PathMatchesScalar_AndSoDoesTheDispatcherAtEveryLength()
        {
            var random = new Random(8765);
            for (var length = 0; length <= 90; length++)
            {
                for (var trial = 0; trial < 20; trial++)
                {
                    var source = RandomPremultiplied(random, length);
                    var coverage = RandomCoverage(random, length);
                    var start = RandomPremultiplied(random, length);

                    var scalar = (byte[])start.Clone();
                    PixelKernels.BlendSpanScalar(scalar, source, coverage);

                    var dispatched = (byte[])start.Clone();
                    PixelKernels.BlendSpan(dispatched, source, coverage);
                    Assert.Equal(scalar, dispatched);

                    if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && length >= 8)
                    {
                        var wide = (byte[])start.Clone();
                        var done = PixelKernels.BlendSpanVector256(wide, source, coverage);
                        PixelKernels.BlendSpanScalar(wide, source, coverage, done);
                        Assert.Equal(scalar, wide);
                    }
                }
            }
        }

        [Fact]
        public void MultiplyCoverage_VectorMatchesScalar()
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var random = new Random(7);
            for (var length = 0; length <= 70; length++)
            {
                var coverage = RandomBytes(random, length);
                var mask = RandomBytes(random, length);

                var scalar = (byte[])coverage.Clone();
                PixelKernels.MultiplyCoverageScalar(scalar, mask);

                var vector = (byte[])coverage.Clone();
                var done = length >= 16 ? PixelKernels.MultiplyCoverageVector128(vector, mask) : 0;
                PixelKernels.MultiplyCoverageScalar(vector, mask, done);

                Assert.Equal(scalar, vector);
            }
        }

        [Fact]
        public void BlendSolid_OpaqueColourAtFullCoverage_ReplacesDestination()
        {
            var dst = new byte[16];
            Array.Fill(dst, (byte)77);
            var coverage = new byte[] { 255, 255, 255, 255 };

            PixelKernels.BlendSolid(dst, coverage, PixelKernels.Pack(10, 20, 30, 255));

            for (var i = 0; i < 4; i++)
                Assert.Equal(new byte[] { 10, 20, 30, 255 }, dst.AsSpan(i * 4, 4).ToArray());
        }

        [Fact]
        public void BlendSolid_HalfCoverageOfOpaqueWhiteOverBlack_IsMidGrey()
        {
            var dst = new byte[] { 0, 0, 0, 255 };

            PixelKernels.BlendSolid(dst, new byte[] { 128 }, PixelKernels.Pack(255, 255, 255, 255));

            Assert.Equal(128, dst[0]);
            Assert.Equal(255, dst[3]);
        }
    }
}
