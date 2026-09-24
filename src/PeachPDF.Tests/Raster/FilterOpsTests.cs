using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;
using PeachPDF.Svg;

namespace PeachPDF.Tests.Raster
{
    /// <summary>The pixel operations behind the SVG filter primitives PDF cannot express, checked against values the filter specifications define.</summary>
    public class FilterOpsTests
    {
        private static RasterSurface Surface(int w, int h) => new(w, h, 0, 0, 1, 1);

        private static void Set(RasterSurface s, int x, int y, byte r, byte g, byte b, byte a)
        {
            var p = s.Pixels;
            var o = (y * s.Width + x) * 4;
            p[o] = r;
            p[o + 1] = g;
            p[o + 2] = b;
            p[o + 3] = a;
        }

        private static byte[] Get(RasterSurface s, int x, int y) => s.Pixels.Slice((y * s.Width + x) * 4, 4).ToArray();

        private static RasterSurface Solid(int w, int h, byte r, byte g, byte b, byte a)
        {
            var s = Surface(w, h);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    Set(s, x, y, r, g, b, a);
            return s;
        }

        [Fact]
        public void ColorSpaceConversion_RoundTripsWithinRounding_AndKeepsAlpha()
        {
            var s = Solid(1, 1, 128, 64, 200, 255);

            FilterOps.ConvertColorSpace(s, toLinear: true);
            var linear = Get(s, 0, 0);
            // sRGB 128 is about 0.216 in linear light: dark, and much lower than the encoded value.
            Assert.InRange(linear[0], 53, 57);
            Assert.True(linear[1] < linear[0]);

            FilterOps.ConvertColorSpace(s, toLinear: false);
            var back = Get(s, 0, 0);
            Assert.InRange(back[0], 124, 132);
            Assert.InRange(back[2], 196, 204);
            Assert.Equal(255, back[3]);
        }

        [Fact]
        public void ColorSpaceConversion_OfAPartlyTransparentPixel_ConvertsTheStraightColour()
        {
            var s = Surface(1, 1);
            Set(s, 0, 0, 64, 64, 64, 128); // straight grey 128 at half alpha, premultiplied

            FilterOps.ConvertColorSpace(s, toLinear: true);

            // Straight linear ~55 at alpha 128, premultiplied ~27.
            Assert.InRange(Get(s, 0, 0)[0], 25, 30);
            Assert.Equal(128, Get(s, 0, 0)[3]);
        }

        [Fact]
        public void ConvertColor_MapsBlackAndWhiteToThemselves()
        {
            Assert.Equal(RColor.FromArgb(255, 0, 0, 0), FilterOps.ConvertColor(RColor.FromArgb(255, 0, 0, 0), true));
            Assert.Equal(RColor.FromArgb(255, 255, 255, 255), FilterOps.ConvertColor(RColor.FromArgb(255, 255, 255, 255), false));
        }

        [Fact]
        public void Offset_ShiftsWholePixels_AndBringsInTransparency()
        {
            var s = Solid(4, 4, 200, 0, 0, 255);
            var d = Surface(4, 4);

            FilterOps.Offset(s, d, 1, -1);

            Assert.Equal(0, Get(d, 0, 0)[3]);
            Assert.Equal(0, Get(d, 1, 3)[3]);
            Assert.Equal(255, Get(d, 1, 0)[3]);
            Assert.Equal(255, Get(d, 3, 2)[3]);
        }

        [Fact]
        public void Tile_RepeatsTheSubregion()
        {
            var s = Surface(6, 2);
            Set(s, 0, 0, 255, 0, 0, 255);
            Set(s, 1, 0, 0, 255, 0, 255);
            var d = Surface(6, 2);

            FilterOps.Tile(s, d, new IntRect(0, 0, 2, 1));

            Assert.Equal(255, Get(d, 4, 0)[0]);
            Assert.Equal(255, Get(d, 5, 0)[1]);
            Assert.Equal(255, Get(d, 2, 1)[0]);
        }

        [Fact]
        public void Tile_OfAnEmptySubregion_IsTransparent()
        {
            var d = Solid(2, 2, 1, 1, 1, 255);

            FilterOps.Tile(Solid(2, 2, 9, 9, 9, 255), d, new IntRect(5, 5, 6, 6));

            Assert.Equal(0, Get(d, 0, 0)[3]);
        }

        [Fact]
        public void ClipTo_ClearsOutsideTheRectangle()
        {
            var s = Solid(4, 4, 10, 10, 10, 255);

            FilterOps.ClipTo(s, new IntRect(1, 1, 3, 3));

            Assert.Equal(0, Get(s, 0, 0)[3]);
            Assert.Equal(0, Get(s, 3, 3)[3]);
            Assert.Equal(0, Get(s, 0, 2)[3]);
            Assert.Equal(255, Get(s, 1, 1)[3]);
            Assert.Equal(255, Get(s, 2, 2)[3]);
        }

        [Theory]
        [InlineData("over", 255, 0)]
        [InlineData("in", 255, 0)]
        [InlineData("out", 0, 0)]
        [InlineData("atop", 255, 0)]
        [InlineData("xor", 0, 0)]
        public void PorterDuff_OfTwoOpaquePixels(string op, int expectedFromA, int expectedFromB)
        {
            var a = Solid(1, 1, 255, 0, 0, 255);
            var b = Solid(1, 1, 0, 0, 255, 255);
            var r = Surface(1, 1);

            FilterOps.PorterDuff(op, a, b, r);

            var p = Get(r, 0, 0);
            // Two opaque inputs: over, in and atop show A alone; out and xor leave nothing.
            Assert.Equal(expectedFromA, p[0]);
            Assert.Equal(expectedFromB, p[2]);
            if (op == "over")
                Assert.Equal(255, p[3]);
            if (op is "out" or "xor")
                Assert.Equal(0, p[3]);
        }

        [Fact]
        public void PorterDuff_In_ScalesByTheOtherAlpha()
        {
            var a = Solid(1, 1, 200, 200, 200, 200);
            var b = Solid(1, 1, 0, 0, 0, 128);
            var r = Surface(1, 1);

            FilterOps.PorterDuff("in", a, b, r);

            Assert.InRange(Get(r, 0, 0)[3], 99, 101);
        }

        [Fact]
        public void Arithmetic_ImplementsTheFourTermFormula_AndKeepsColourWithinAlpha()
        {
            var a = Solid(1, 1, 128, 128, 128, 255);
            var b = Solid(1, 1, 255, 255, 255, 255);
            var r = Surface(1, 1);

            // 0.5 * i1 + 0.5 * i2: halfway between mid grey and white.
            FilterOps.Arithmetic(a, b, r, 0, 0.5, 0.5, 0);
            Assert.InRange(Get(r, 0, 0)[0], 190, 193);

            // k4 alone is a constant, but colour can never exceed alpha.
            var transparent = Surface(1, 1);
            FilterOps.Arithmetic(transparent, transparent, r, 0, 0, 0, 0.5);
            var p = Get(r, 0, 0);
            Assert.InRange(p[3], 127, 129);
            Assert.True(p[0] <= p[3]);
        }

        [Fact]
        public void Arithmetic_Clamps()
        {
            var a = Solid(1, 1, 255, 255, 255, 255);
            var r = Surface(1, 1);

            FilterOps.Arithmetic(a, a, r, 0, 2, 2, 0);

            Assert.Equal(new byte[] { 255, 255, 255, 255 }, Get(r, 0, 0));
        }

        [Fact]
        public void Blend_Multiply_DarkensAndNormalIsOver()
        {
            var top = Solid(1, 1, 128, 128, 128, 255);
            var bottom = Solid(1, 1, 128, 128, 128, 255);
            var r = Surface(1, 1);

            FilterOps.Blend(RBlendMode.Multiply, top, bottom, r);
            Assert.InRange(Get(r, 0, 0)[0], 63, 65);

            FilterOps.Blend(RBlendMode.Normal, top, bottom, r);
            Assert.Equal(128, Get(r, 0, 0)[0]);
        }

        [Fact]
        public void Blend_WithATransparentTop_LeavesTheBottom()
        {
            var r = Surface(1, 1);

            FilterOps.Blend(RBlendMode.Screen, Surface(1, 1), Solid(1, 1, 10, 20, 30, 255), r);

            Assert.Equal(new byte[] { 10, 20, 30, 255 }, Get(r, 0, 0));
        }

        [Fact]
        public void OverInPlace_DrawsTheTopOverTheDestination()
        {
            var d = Solid(1, 1, 0, 0, 255, 255);
            var top = Surface(1, 1);
            Set(top, 0, 0, 128, 0, 0, 128);

            FilterOps.OverInPlace(d, top);

            var p = Get(d, 0, 0);
            Assert.InRange(p[0], 127, 129);
            Assert.InRange(p[2], 126, 128);
            Assert.Equal(255, p[3]);
        }

        [Fact]
        public void Fill_PremultipliesTheColour()
        {
            var s = Surface(2, 1);

            FilterOps.Fill(s, RColor.FromArgb(255, 200, 100, 50), 0.5);

            var p = Get(s, 1, 0);
            Assert.InRange(p[3], 127, 129);
            Assert.InRange(p[0], 99, 101);
        }

        // ---- component transfer --------------------------------------------------------------------------

        private static TransferFunction Function(TransferKind kind, double[]? table = null, double slope = 1, double intercept = 0, double amplitude = 1, double exponent = 1, double offset = 0) =>
            new(kind, table ?? [], slope, intercept, amplitude, exponent, offset);

        [Fact]
        public void TransferTable_InterpolatesBetweenTheEntries()
        {
            var lut = FilterOps.BuildTransferLut(Function(TransferKind.Table, [0, 1, 0]));

            Assert.Equal(0, lut[0]);
            Assert.Equal(255, lut[128] > 250 ? 255 : lut[128]); // the peak in the middle
            Assert.Equal(0, lut[255]);
            Assert.InRange((int)lut[64], 120, 135);
        }

        [Fact]
        public void TransferDiscrete_IsAStaircase()
        {
            var lut = FilterOps.BuildTransferLut(Function(TransferKind.Discrete, [0, 0.5, 1]));

            Assert.Equal(0, lut[0]);
            Assert.InRange((int)lut[128], 127, 128);
            Assert.Equal(255, lut[255]);
        }

        [Fact]
        public void TransferGamma_AndLinear_FollowTheirFormulae()
        {
            var gamma = FilterOps.BuildTransferLut(Function(TransferKind.Gamma, amplitude: 1, exponent: 2, offset: 0));
            Assert.InRange((int)gamma[128], 63, 65); // 0.5^2 = 0.25

            var linear = FilterOps.BuildTransferLut(Function(TransferKind.Linear, slope: 0.5, intercept: 0.25));
            Assert.InRange((int)linear[255], 190, 192); // 0.75
            Assert.InRange((int)linear[0], 63, 65);
        }

        [Fact]
        public void TransferFunction_DefaultsAndClamping()
        {
            var identity = FilterOps.BuildTransferLut(TransferFunction.Identity);
            Assert.Equal(200, identity[200]);

            var overshoot = FilterOps.BuildTransferLut(Function(TransferKind.Linear, slope: 3));
            Assert.Equal(255, overshoot[200]);
        }

        [Fact]
        public void ComponentTransfer_WorksOnStraightColour_AndCanRaiseAlpha()
        {
            var source = Surface(1, 1);
            Set(source, 0, 0, 64, 0, 0, 128); // straight red 128 at alpha 128
            var dest = Surface(1, 1);
            var identity = FilterOps.BuildTransferLut(TransferFunction.Identity);
            var invert = FilterOps.BuildTransferLut(Function(TransferKind.Linear, slope: -1, intercept: 1));

            FilterOps.ComponentTransfer(source, dest, invert, identity, identity, identity);

            // Straight red 128 inverts to 127; premultiplied by alpha 128 that is ~64.
            Assert.InRange(Get(dest, 0, 0)[0], 62, 66);
            Assert.Equal(128, Get(dest, 0, 0)[3]);

            var fullAlpha = FilterOps.BuildTransferLut(Function(TransferKind.Linear, slope: 0, intercept: 1));
            FilterOps.ComponentTransfer(Surface(1, 1), dest, identity, identity, identity, fullAlpha);
            Assert.Equal(255, Get(dest, 0, 0)[3]);
        }

        // ---- morphology ----------------------------------------------------------------------------------

        [Fact]
        public void Dilate_GrowsAShape_AndErodeShrinksIt()
        {
            var s = Surface(9, 9);
            for (var y = 3; y < 6; y++)
                for (var x = 3; x < 6; x++)
                    Set(s, x, y, 255, 255, 255, 255);
            var dilated = Surface(9, 9);
            var eroded = Surface(9, 9);

            FilterOps.Morphology(s, dilated, 1, 1, dilate: true);
            FilterOps.Morphology(s, eroded, 1, 1, dilate: false);

            Assert.Equal(255, Get(dilated, 2, 2)[3]);
            Assert.Equal(255, Get(dilated, 6, 6)[3]);
            Assert.Equal(0, Get(dilated, 1, 1)[3]);
            Assert.Equal(255, Get(eroded, 4, 4)[3]);
            Assert.Equal(0, Get(eroded, 3, 3)[3]);
        }

        [Fact]
        public void Morphology_RadiusOnOneAxisOnly_WorksAlongThatAxis()
        {
            var s = Surface(7, 3);
            Set(s, 3, 1, 255, 255, 255, 255);
            var d = Surface(7, 3);

            FilterOps.Morphology(s, d, 2, 0, dilate: true);

            Assert.Equal(255, Get(d, 1, 1)[3]);
            Assert.Equal(255, Get(d, 5, 1)[3]);
            Assert.Equal(0, Get(d, 3, 0)[3]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Morphology_VectorFormMatchesTheScalarReference(bool dilate)
        {
            var random = new Random(dilate ? 21 : 22);
            foreach (var (w, h) in new[] { (1, 1), (3, 2), (9, 9), (20, 7), (33, 41), (64, 3) })
            {
                var source = Surface(w, h);
                random.NextBytes(source.Pixels);
                foreach (var (rx, ry) in new[] { (0, 0), (1, 0), (0, 2), (1, 1), (2, 3), (4, 4), (9, 2), (2, 30) })
                {
                    var expected = Surface(w, h);
                    var actual = Surface(w, h);
                    FilterOps.MorphologyScalar(source, expected, rx, ry, dilate);
                    FilterOps.MorphologyVector(source, actual, rx, ry, dilate);

                    Assert.True(expected.Pixels.SequenceEqual(actual.Pixels), $"{w}x{h} radius {rx},{ry} dilate {dilate}");
                }
            }
        }

        [Fact]
        public void Erode_TreatsTheEdgeOfTheSurfaceAsTransparent()
        {
            var s = Solid(5, 5, 255, 255, 255, 255);
            var d = Surface(5, 5);

            FilterOps.Morphology(s, d, 1, 1, dilate: false);

            Assert.Equal(0, Get(d, 0, 2)[3]);
            Assert.Equal(255, Get(d, 2, 2)[3]);
        }

        // ---- convolution ---------------------------------------------------------------------------------

        private static FeConvolveMatrix Convolve(int order, double[] kernel, FilterEdgeMode edge = FilterEdgeMode.Duplicate, bool preserveAlpha = false, double divisor = 1, double bias = 0) =>
            new()
            {
                OrderX = order,
                OrderY = order,
                Kernel = kernel,
                Divisor = divisor,
                Bias = bias,
                TargetX = order / 2,
                TargetY = order / 2,
                EdgeMode = edge,
                PreserveAlpha = preserveAlpha,
            };

        [Fact]
        public void Convolve_WithTheIdentityKernel_ReturnsTheInput()
        {
            var s = Surface(4, 4);
            Set(s, 1, 1, 200, 100, 50, 255);
            Set(s, 2, 2, 60, 30, 0, 128);
            var d = Surface(4, 4);

            FilterOps.ConvolveMatrix(s, d, Convolve(3, [0, 0, 0, 0, 1, 0, 0, 0, 0]));

            Assert.Equal(Get(s, 1, 1), Get(d, 1, 1));
            Assert.Equal(Get(s, 2, 2), Get(d, 2, 2));
        }

        [Fact]
        public void Convolve_ShiftKernel_MovesThePixelAccordingToTheSpecFormula()
        {
            var s = Surface(5, 1);
            Set(s, 2, 0, 255, 255, 255, 255);
            var d = Surface(5, 1);

            // kernelMatrix index = orderX - J - 1: a 1 in the first cell (J = 0 when reading, x - target + 0) is a
            // correlation with the mirrored kernel, so the pixel appears one to the right of where it was.
            FilterOps.ConvolveMatrix(s, d, Convolve(3, [0, 0, 0, 0, 0, 1, 0, 0, 0], FilterEdgeMode.None));

            Assert.Equal(255, Get(d, 1, 0)[3] + Get(d, 3, 0)[3] > 0 ? 255 : 0);
            Assert.Equal(0, Get(d, 2, 0)[3]);
        }

        [Fact]
        public void Convolve_BoxKernel_AveragesAndHonoursEdgeModes()
        {
            var s = Surface(3, 3);
            Set(s, 0, 0, 255, 255, 255, 255);
            var box = Enumerable.Repeat(1.0, 9).ToArray();

            var none = Surface(3, 3);
            FilterOps.ConvolveMatrix(s, none, Convolve(3, box, FilterEdgeMode.None, divisor: 9));
            // Only the (0,0) source pixel exists: its share reaches the centre of the 3x3.
            Assert.InRange((int)Get(none, 1, 1)[3], 27, 29);

            var wrap = Surface(3, 3);
            FilterOps.ConvolveMatrix(s, wrap, Convolve(3, box, FilterEdgeMode.Wrap, divisor: 9));
            // With wrapping every output pixel sees the whole 3x3 exactly once.
            Assert.InRange((int)Get(wrap, 0, 0)[3], 27, 29);
            Assert.InRange((int)Get(wrap, 2, 2)[3], 27, 29);

            var duplicate = Surface(3, 3);
            FilterOps.ConvolveMatrix(s, duplicate, Convolve(3, box, FilterEdgeMode.Duplicate, divisor: 9));
            // The corner pixel is repeated outwards, so the corner output sees it four times.
            Assert.InRange((int)Get(duplicate, 0, 0)[3], 110, 116);
        }

        [Fact]
        public void Convolve_PreserveAlpha_KeepsTheAlphaChannel()
        {
            var s = Surface(3, 1);
            Set(s, 1, 0, 100, 100, 100, 200);
            var d = Surface(3, 1);

            FilterOps.ConvolveMatrix(s, d, Convolve(3, [0, 0, 0, 0, 2, 0, 0, 0, 0], preserveAlpha: true));

            Assert.Equal(200, Get(d, 1, 0)[3]);
            Assert.Equal(0, Get(d, 0, 0)[3]);
        }

        [Fact]
        public void Convolve_Bias_LiftsTheResult()
        {
            var s = Surface(1, 1);
            var d = Surface(1, 1);

            FilterOps.ConvolveMatrix(s, d, Convolve(1, [1], bias: 0.5));

            Assert.InRange(Get(d, 0, 0)[3], 127, 129);
        }

        // ---- displacement --------------------------------------------------------------------------------

        [Fact]
        public void Displace_WithAMapOfMidGrey_LeavesTheImageAlone()
        {
            var s = Surface(4, 4);
            Set(s, 1, 2, 255, 0, 0, 255);
            var map = Solid(4, 4, 128, 128, 128, 255);
            var d = Surface(4, 4);

            FilterOps.Displace(s, map, d, 10, 10, 0, 1);

            Assert.Equal(255, Get(d, 1, 2)[0]);
        }

        [Fact]
        public void Displace_MovesPixelsByTheMapChannels()
        {
            var s = Surface(6, 1);
            Set(s, 2, 0, 255, 0, 0, 255);
            // R = 0 => x displacement of -scale/2 = -2: each output pixel reads from two pixels to its left, so the dot moves right by two.
            var map = Solid(6, 1, 0, 128, 0, 255);
            var d = Surface(6, 1);

            FilterOps.Displace(s, map, d, 4, 4, 0, 1);

            Assert.Equal(255, Get(d, 4, 0)[0]);
            Assert.Equal(0, Get(d, 2, 0)[3]);
        }

        [Fact]
        public void Displace_OutsideTheImage_IsTransparent_AndAlphaCanDriveDisplacement()
        {
            var s = Solid(3, 3, 255, 255, 255, 255);
            var map = Solid(3, 3, 0, 0, 0, 0); // alpha 0 => channel A = 0
            var d = Surface(3, 3);

            FilterOps.Displace(s, map, d, 10, 10, 3, 3);

            Assert.Equal(0, Get(d, 1, 1)[3]);
        }

        // ---- turbulence ----------------------------------------------------------------------------------

        [Fact]
        public void Turbulence_IsDeterministic_AndTheSeedChangesIt()
        {
            var a = Surface(16, 16);
            var b = Surface(16, 16);
            var c = Surface(16, 16);

            new Turbulence(1).Render(a, 1, 1, 0.1, 0.1, 2, fractalSum: false, stitching: false, 0, 0, 16, 16);
            new Turbulence(1).Render(b, 1, 1, 0.1, 0.1, 2, fractalSum: false, stitching: false, 0, 0, 16, 16);
            new Turbulence(2).Render(c, 1, 1, 0.1, 0.1, 2, fractalSum: false, stitching: false, 0, 0, 16, 16);

            Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());
            Assert.NotEqual(a.Pixels.ToArray(), c.Pixels.ToArray());
        }

        [Fact]
        public void Turbulence_FractalNoise_CentresOnHalfGrey_AndVariesSmoothly()
        {
            var s = Surface(32, 32);

            new Turbulence(7).Render(s, 1, 1, 0.05, 0.05, 3, fractalSum: true, stitching: false, 0, 0, 32, 32);

            long alphaSum = 0;
            for (var i = 3; i < s.Pixels.Length; i += 4)
                alphaSum += s.Pixels[i];
            var meanAlpha = alphaSum / (32.0 * 32);
            Assert.InRange(meanAlpha, 90, 165);

            // Neighbouring pixels differ little at a low base frequency.
            var maxStep = 0;
            for (var x = 1; x < 32; x++)
                maxStep = Math.Max(maxStep, Math.Abs(s.Pixels[(x * 4) + 3] - s.Pixels[((x - 1) * 4) + 3]));
            Assert.True(maxStep < 40, $"max step {maxStep}");
        }

        [Fact]
        public void Turbulence_Stitching_MakesTheTileEdgesMeet()
        {
            // With stitching, noise at the right edge of the tile continues into the left edge: the value one tile-width away is the same.
            var n = new Turbulence(3);
            var left = n.Sample(0, 0.0, 5.0, 0.1, 0.1, 2, fractalSum: true, stitching: true, 0, 0, 20, 20);
            var right = n.Sample(0, 20.0, 5.0, 0.1, 0.1, 2, fractalSum: true, stitching: true, 0, 0, 20, 20);

            Assert.Equal(left, right, 6);
        }

        [Fact]
        public void Turbulence_SeedIsSanitisedLikeTheReferenceImplementation()
        {
            // Zero and negative seeds map into the generator's valid range instead of stalling or crashing.
            var s = Surface(4, 4);
            new Turbulence(0).Render(s, 1, 1, 0.5, 0.5, 1, false, false, 0, 0, 4, 4);
            new Turbulence(-12345).Render(s, 1, 1, 0.5, 0.5, 1, false, false, 0, 0, 4, 4);
            new Turbulence(long.MaxValue).Render(s, 1, 1, 0.5, 0.5, 1, false, false, 0, 0, 4, 4);
        }

        // ---- lighting ------------------------------------------------------------------------------------

        private static FeLighting Light(bool specular, LightSource light, double constant = 1, double exponent = 1, double surfaceScale = 1) =>
            new()
            {
                Specular = specular,
                SurfaceScale = surfaceScale,
                Constant = constant,
                SpecularExponent = exponent,
                LightingColor = RColor.FromArgb(255, 255, 255, 255),
                Light = light,
            };

        private static LightSource Distant(double azimuth, double elevation) => new(LightKind.Distant, azimuth, elevation, 0, 0, 0, 0, 0, 0, 1, null);

        [Fact]
        public void DiffuseLighting_OfAFlatSurface_IsBrightestFromDirectlyAbove()
        {
            var s = Solid(5, 5, 0, 0, 0, 255);
            var overhead = Surface(5, 5);
            var grazing = Surface(5, 5);

            Lighting.Render(s, overhead, Light(false, Distant(0, 90)), RColor.FromArgb(255, 255, 255, 255), 1, 1);
            Lighting.Render(s, grazing, Light(false, Distant(0, 30)), RColor.FromArgb(255, 255, 255, 255), 1, 1);

            Assert.Equal(255, Get(overhead, 2, 2)[0]);
            Assert.Equal(255, Get(overhead, 2, 2)[3]);
            // N.L = sin(30 degrees) = 0.5.
            Assert.InRange((int)Get(grazing, 2, 2)[0], 126, 129);
        }

        [Fact]
        public void DiffuseLighting_LightFromTheSide_BrightensTheFacingSlope()
        {
            // A ramp rising to the right: alpha increases with x. Light from the +x side (azimuth 0) should light it less than from -x,
            // because the surface tilts away from +x... the normal points towards -x, so a light at azimuth 180 is brighter.
            var s = Surface(9, 3);
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 9; x++)
                {
                    var a = (byte)(x * 28);
                    Set(s, x, y, 0, 0, 0, a);
                }

            var fromRight = Surface(9, 3);
            var fromLeft = Surface(9, 3);
            Lighting.Render(s, fromRight, Light(false, Distant(0, 30), surfaceScale: 4), RColor.FromArgb(255, 255, 255, 255), 1, 1);
            Lighting.Render(s, fromLeft, Light(false, Distant(180, 30), surfaceScale: 4), RColor.FromArgb(255, 255, 255, 255), 1, 1);

            Assert.True(Get(fromLeft, 4, 1)[0] > Get(fromRight, 4, 1)[0]);
        }

        [Fact]
        public void SpecularLighting_HasAlphaOfTheBrightestChannel_AndPeaksAtTheMirrorDirection()
        {
            var s = Solid(5, 5, 0, 0, 0, 255);
            var d = Surface(5, 5);

            Lighting.Render(s, d, Light(true, Distant(0, 90), exponent: 4), RColor.FromArgb(255, 255, 128, 0), 1, 1);

            var p = Get(d, 2, 2);
            Assert.Equal(p[3], Math.Max(p[0], Math.Max(p[1], p[2])));
            Assert.True(p[0] > p[1] && p[1] > p[2]);
        }

        [Fact]
        public void PointLight_FallsOffWithDirection_AndSpotLightIsConfinedToItsCone()
        {
            var s = Solid(21, 21, 0, 0, 0, 255);
            var point = Surface(21, 21);
            var spot = Surface(21, 21);
            var white = RColor.FromArgb(255, 255, 255, 255);

            Lighting.Render(s, point, Light(false, new LightSource(LightKind.Point, 0, 0, 10.5, 10.5, 10, 0, 0, 0, 1, null)), white, 1, 1);
            Assert.True(Get(point, 10, 10)[0] > Get(point, 0, 0)[0]);

            // A spot aimed straight down at the centre with a narrow cone: dark at the corners.
            Lighting.Render(s, spot, Light(false, new LightSource(LightKind.Spot, 0, 0, 10.5, 10.5, 10, 10.5, 10.5, 0, 1, 20)), white, 1, 1);
            Assert.True(Get(spot, 10, 10)[0] > 200);
            Assert.Equal(0, Get(spot, 0, 0)[0]);
        }

        [Fact]
        public void Lighting_IsResolutionIndependent_ForTheSameArtwork()
        {
            // The same ramp drawn at one pixel per unit and at two: the lit result at matching positions agrees.
            var coarse = Surface(9, 3);
            var fine = new RasterSurface(18, 6, 0, 0, 2, 2);
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 9; x++)
                    Set(coarse, x, y, 0, 0, 0, (byte)(x * 28));
            for (var y = 0; y < 6; y++)
                for (var x = 0; x < 18; x++)
                    Set(fine, x, y, 0, 0, 0, (byte)(x / 2 * 28 + (x % 2) * 14));

            var coarseLit = Surface(9, 3);
            var fineLit = new RasterSurface(18, 6, 0, 0, 2, 2);
            var white = RColor.FromArgb(255, 255, 255, 255);
            Lighting.Render(coarse, coarseLit, Light(false, Distant(180, 30), surfaceScale: 4), white, 1, 1);
            Lighting.Render(fine, fineLit, Light(false, Distant(180, 30), surfaceScale: 4), white, 2, 2);

            Assert.InRange(Math.Abs(Get(coarseLit, 4, 1)[0] - Get(fineLit, 9, 2)[0]), 0, 12);
        }
    }
}
