using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Svg;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// The pixel evaluation of an SVG filter with a primitive PDF cannot express: filters are built from real markup and
    /// rendered into a <see cref="RasterGraphics"/> host, whose pixels are then read back.
    /// </summary>
    public class SvgRasterFilterEvaluatorTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgDocument Build(string filterMarkup, string extraElements = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><defs>{filterMarkup}</defs><rect id="target" x="20" y="20" width="40" height="40" filter="url(#f)"/>{extraElements}</svg>""";
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), Adapter);
        }

        /// <summary>Renders <paramref name="paint"/> through the filter into a 100 x 100 host at <paramref name="pixelsPerUnit"/> pixels per unit.</summary>
        private static RasterGraphics Render(string filterMarkup, Action<RGraphics> paint, double pixelsPerUnit = 1, SvgElement? element = null)
        {
            var document = Build(filterMarkup);
            var filter = document.Filters["f"];
            var size = (int)(100 * pixelsPerUnit);
            var host = new RasterGraphics(Adapter, new RasterSurface(size, size, 0, 0, pixelsPerUnit, pixelsPerUnit), 1);

            SvgFilterEvaluator.Render(host, filter, element ?? document.Children[0], new RRect(0, 0, 100, 100), paint);
            return host;
        }

        private static void Rect(RGraphics g, RColor color, double x = 20, double y = 20, double w = 40, double h = 40) =>
            g.DrawRectangle(g.GetSolidBrush(color), x, y, w, h);

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static readonly RColor Red = RColor.FromArgb(255, 255, 0, 0);
        private static readonly RColor Blue = RColor.FromArgb(255, 0, 0, 255);
        private static readonly RColor White = RColor.FromArgb(255, 255, 255, 255);

        [Fact]
        public void Blur_SpreadsTheElementOutsideItsEdges()
        {
            var host = Render("""<filter id="f"><feGaussianBlur stdDeviation="4"/></filter>""", g => Rect(g, Red));

            Assert.InRange(Pixel(host, 40, 40)[3], 250, 255);
            var outside = Pixel(host, 17, 40);
            Assert.InRange((int)outside[3], 20, 200);
            Assert.Equal(0, Pixel(host, 5, 40)[3]);
        }

        [Fact]
        public void Blur_DeviationIsInUserUnits_SoADenserSurfaceGivesTheSameShape()
        {
            var coarse = Render("""<filter id="f"><feGaussianBlur stdDeviation="4"/></filter>""", g => Rect(g, Red));
            var fine = Render("""<filter id="f"><feGaussianBlur stdDeviation="4"/></filter>""", g => Rect(g, Red), pixelsPerUnit: 2);

            // The same point of the artwork, one pixel per unit and two: the softened edge agrees.
            Assert.InRange(Math.Abs(Pixel(coarse, 18, 40)[3] - Pixel(fine, 36, 80)[3]), 0, 14);
        }

        [Fact]
        public void ZeroDeviation_IsAPassThrough()
        {
            var host = Render("""<filter id="f"><feGaussianBlur stdDeviation="0"/></filter>""", g => Rect(g, Red));

            Assert.Equal(255, Pixel(host, 20, 20)[3]);
            Assert.Equal(0, Pixel(host, 19, 20)[3]);
        }

        [Fact]
        public void OneAxisDeviation_BlursOnlyThatAxis()
        {
            var host = Render("""<filter id="f"><feGaussianBlur stdDeviation="6 0"/></filter>""", g => Rect(g, Red));

            Assert.True(Pixel(host, 17, 40)[3] > 0);
            Assert.Equal(0, Pixel(host, 40, 17)[3]);
        }

        [Fact]
        public void SaturateZero_TurnsColourToGrey()
        {
            var host = Render("""<filter id="f" color-interpolation-filters="sRGB"><feColorMatrix type="saturate" values="0"/></filter>""", g => Rect(g, Red));

            var p = Pixel(host, 40, 40);
            Assert.InRange(Math.Abs(p[0] - p[1]), 0, 2);
            Assert.InRange(Math.Abs(p[1] - p[2]), 0, 2);
            Assert.InRange((int)p[0], 50, 60); // 0.213 * 255
        }

        [Fact]
        public void HueRotate_Full_ReturnsTheColour()
        {
            var host = Render("""<filter id="f" color-interpolation-filters="sRGB"><feColorMatrix type="hueRotate" values="360"/></filter>""", g => Rect(g, Red));

            var p = Pixel(host, 40, 40);
            Assert.InRange((int)p[0], 250, 255);
            Assert.InRange((int)p[1], 0, 4);
        }

        [Fact]
        public void LuminanceToAlpha_MakesBrightAreasOpaque()
        {
            var host = Render("""<filter id="f" color-interpolation-filters="sRGB"><feColorMatrix type="luminanceToAlpha"/></filter>""", g => Rect(g, White));

            var p = Pixel(host, 40, 40);
            Assert.InRange((int)p[3], 250, 255);
            Assert.Equal(0, p[0]);
        }

        [Fact]
        public void LinearRgbBlur_IsBrighterAcrossAnEdgeThanSrgbBlur()
        {
            const string linear = """<filter id="f" color-interpolation-filters="linearRGB"><feGaussianBlur stdDeviation="5"/></filter>""";
            const string srgb = """<filter id="f" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="5"/></filter>""";
            void Paint(RGraphics g)
            {
                Rect(g, Red, 20, 20, 20, 40);
                Rect(g, RColor.FromArgb(255, 0, 255, 0), 40, 20, 20, 40);
            }

            var l = Pixel(Render(linear, Paint), 40, 40);
            var s = Pixel(Render(srgb, Paint), 40, 40);

            // Mixing red and green halfway: linear-light averaging gives a brighter result than averaging the encoded values.
            Assert.True(l[0] + l[1] > s[0] + s[1] + 20, $"linear {l[0]},{l[1]} vs sRGB {s[0]},{s[1]}");
        }

        [Fact]
        public void FloodColour_SurvivesTheLinearRoundTrip()
        {
            var host = Render("""<filter id="f"><feFlood flood-color="#3399ff"/><feColorMatrix type="saturate" values="1"/></filter>""", _ => { });

            var p = Pixel(host, 40, 40);
            Assert.InRange((int)p[0], 0x31, 0x35);
            Assert.InRange((int)p[1], 0x97, 0x9b);
            Assert.InRange((int)p[2], 0xfd, 0xff);
        }

        [Fact]
        public void PrimitiveSubregion_RestrictsAFlood()
        {
            var host = Render("""<filter id="f" x="0" y="0" width="100%" height="100%" filterUnits="userSpaceOnUse"><feFlood flood-color="#00f" x="30" y="30" width="20" height="20"/></filter>""", _ => { });

            Assert.Equal(255, Pixel(host, 40, 40)[3]);
            Assert.Equal(0, Pixel(host, 25, 40)[3]);
            Assert.Equal(0, Pixel(host, 40, 55)[3]);
        }

        [Fact]
        public void Offset_MovesTheContent()
        {
            var host = Render("""<filter id="f" x="-50%" y="-50%" width="200%" height="200%"><feOffset dx="10" dy="5"/></filter>""", g => Rect(g, Red));

            Assert.Equal(0, Pixel(host, 25, 22)[3]);
            Assert.Equal(255, Pixel(host, 35, 30)[3]);
        }

        [Fact]
        public void NamedResults_AreReferencedByLaterPrimitives()
        {
            // Blur SourceAlpha to a shadow, offset it, and merge under the source: the shadow shows past the bottom-right corner.
            var host = Render("""
                <filter id="f" x="-50%" y="-50%" width="200%" height="200%">
                  <feGaussianBlur in="SourceAlpha" stdDeviation="2" result="b"/>
                  <feOffset in="b" dx="8" dy="8" result="o"/>
                  <feMerge><feMergeNode in="o"/><feMergeNode in="SourceGraphic"/></feMerge>
                </filter>
                """, g => Rect(g, Red));

            Assert.Equal(255, Pixel(host, 30, 30)[3]);
            Assert.Equal(255, Pixel(host, 30, 30)[0]);
            var shadow = Pixel(host, 64, 64);
            Assert.True(shadow[3] > 100);
            Assert.Equal(0, shadow[0]); // the shadow is black (SourceAlpha)
        }

        [Fact]
        public void ArithmeticComposite_CombinesTwoInputs()
        {
            var host = Render("""
                <filter id="f" color-interpolation-filters="sRGB">
                  <feFlood flood-color="#ffffff" result="w"/>
                  <feComposite in="SourceGraphic" in2="w" operator="arithmetic" k1="0" k2="0.5" k3="0.5" k4="0"/>
                </filter>
                """, g => Rect(g, RColor.FromArgb(255, 0, 0, 0)));

            // (black + white) / 2 inside the rectangle.
            Assert.InRange((int)Pixel(host, 40, 40)[0], 126, 129);
        }

        [Fact]
        public void ComponentTransfer_Table_InvertsTheChannel()
        {
            var host = Render("""
                <filter id="f" color-interpolation-filters="sRGB">
                  <feComponentTransfer><feFuncR type="table" tableValues="1 0"/></feComponentTransfer>
                </filter>
                """, g => Rect(g, Red));

            Assert.InRange((int)Pixel(host, 40, 40)[0], 0, 2);
        }

        [Fact]
        public void Morphology_Dilate_GrowsTheShape_AndErodeShrinksIt()
        {
            var dilated = Render("""<filter id="f"><feMorphology operator="dilate" radius="3"/></filter>""", g => Rect(g, Red));
            var eroded = Render("""<filter id="f"><feMorphology operator="erode" radius="3"/></filter>""", g => Rect(g, Red));

            Assert.Equal(255, Pixel(dilated, 18, 40)[3]);
            Assert.Equal(0, Pixel(eroded, 21, 40)[3]);
            Assert.Equal(255, Pixel(eroded, 40, 40)[3]);
        }

        [Fact]
        public void Turbulence_FillsTheRegionWithNoise()
        {
            var host = Render("""<filter id="f" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.1" numOctaves="2" seed="5"/></filter>""", _ => { });

            var distinct = new HashSet<int>();
            for (var x = 20; x < 60; x += 3)
                distinct.Add(Pixel(host, x, 30)[0]);
            Assert.True(distinct.Count > 5, "noise should vary across the region");
            Assert.True(Pixel(host, 40, 40)[3] > 0);
        }

        [Fact]
        public void DisplacementMap_ShiftsPixelsByTheMap()
        {
            var host = Render("""
                <filter id="f" x="-50%" y="-50%" width="200%" height="200%" color-interpolation-filters="sRGB">
                  <feFlood flood-color="#ff8080" result="map"/>
                  <feDisplacementMap in="SourceGraphic" in2="map" scale="20" xChannelSelector="R" yChannelSelector="G"/>
                </filter>
                """, g => Rect(g, Red));

            // R = 1 => +10 in x when reading: the content appears 10 units to the left of where it was.
            Assert.Equal(255, Pixel(host, 12, 40)[3]);
            Assert.Equal(0, Pixel(host, 55, 40)[3]);
        }

        [Fact]
        public void DiffuseLighting_ProducesAnOpaqueLitSurface()
        {
            var host = Render("""
                <filter id="f" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB">
                  <feDiffuseLighting surfaceScale="3" diffuseConstant="1"><feDistantLight azimuth="45" elevation="60"/></feDiffuseLighting>
                </filter>
                """, g => Rect(g, Red));

            var p = Pixel(host, 40, 40);
            Assert.Equal(255, p[3]);
            Assert.True(p[0] > 100);
        }

        [Fact]
        public void ConvolveMatrix_Identity_LeavesTheContentAlone()
        {
            var host = Render("""<filter id="f" color-interpolation-filters="sRGB"><feConvolveMatrix order="3" kernelMatrix="0 0 0 0 1 0 0 0 0"/></filter>""", g => Rect(g, Red));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(host, 40, 40));
        }

        [Fact]
        public void DropShadow_PaintsAShadowUnderTheContent()
        {
            var host = Render("""<filter id="f" x="-50%" y="-50%" width="200%" height="200%" color-interpolation-filters="sRGB"><feDropShadow dx="8" dy="8" stdDeviation="1" flood-color="#000"/></filter>""", g => Rect(g, Red));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(host, 30, 30));
            Assert.True(Pixel(host, 66, 66)[3] > 100);
        }

        [Fact]
        public void DropShadow_WithAPartlyOpaqueMidGreyFlood_IsPremultiplied()
        {
            var host = Render("""<filter id="f" x="-50%" y="-50%" width="300%" height="200%" color-interpolation-filters="sRGB"><feDropShadow dx="30" dy="0" stdDeviation="0" flood-color="#404040" flood-opacity="0.5"/></filter>""", g => Rect(g, Red));

            // Past the content's right edge only the shadow shows: 0x40 at half opacity, premultiplied (r <= a).
            var shadow = Pixel(host, 75, 40);
            Assert.InRange((int)shadow[3], 126, 130);
            Assert.InRange((int)shadow[0], 30, 34);
            Assert.True(shadow[0] <= shadow[3]);
        }

        [Fact]
        public void APrimitiveThatReadsANamedResult_DefaultsToThatResultsSubregion()
        {
            var host = Render("""
                <filter id="f" x="0" y="0" width="100%" height="100%" filterUnits="userSpaceOnUse">
                  <feFlood flood-color="#00f" x="30" y="30" width="20" height="20" result="a"/>
                  <feOffset in="a" dx="5" dy="0"/>
                </filter>
                """, _ => { });

            // The flood, shifted right by 5, is limited to the flood's own square rather than the whole filter region.
            Assert.Equal(255, Pixel(host, 45, 40)[3]);
            Assert.Equal(0, Pixel(host, 52, 40)[3]);
            Assert.Equal(0, Pixel(host, 32, 40)[3]);
        }

        [Theory]
        [InlineData("""<feFuncR type="gamma" exponent="-1"/>""")]
        [InlineData("""<feFuncR type="linear" slope="10000000"/>""")]
        public void OutOfRangeTransferFunctions_ClampInsteadOfDependingOnTheCast(string function)
        {
            var host = Render($"""<filter id="f" color-interpolation-filters="sRGB"><feComponentTransfer>{function}</feComponentTransfer></filter>""", g => Rect(g, RColor.FromArgb(255, 0, 100, 0)));

            // Pow(0, -1) is infinity and 1e7 * 0 is 0: both must land on a byte the same way everywhere (0 or 255), never a wrapped value.
            var red = Pixel(host, 40, 40)[0];
            Assert.True(red is 0 or 255, $"red was {red}");
        }

        [Fact]
        public void FilterOnAnElementThatCannotBeMeasured_UsesTheViewportForItsRegion()
        {
            // Text has no static geometry, so an objectBoundingBox region falls back to the viewport instead of collapsing.
            var document = Build("""<filter id="f"><feGaussianBlur stdDeviation="2"/></filter>""");
            var host = new RasterGraphics(Adapter, new RasterSurface(100, 100, 0, 0, 1, 1), 1);
            var text = new SvgTextElement();

            SvgFilterEvaluator.Render(host, document.Filters["f"], text, new RRect(0, 0, 100, 100), g => Rect(g, Red, 40, 40, 10, 10));

            Assert.True(Pixel(host, 45, 45)[3] > 200);
        }

        [Fact]
        public void RegionOutsideTheRasterContext_PaintsNothing()
        {
            var document = Build("""<filter id="f"><feGaussianBlur stdDeviation="2"/></filter>""");
            // A tile-like host that hands out no raster surface: nothing is painted and nothing throws.
            var host = new RasterGraphics(Adapter, new RasterSurface(10, 10, 500, 500, 1, 1), 1);

            SvgFilterEvaluator.Render(host, document.Filters["f"], document.Children[0], new RRect(0, 0, 100, 100), g => Rect(g, Red));

            Assert.All(host.Surface.Pixels.ToArray(), b => Assert.Equal(0, b));
        }
    }
}
