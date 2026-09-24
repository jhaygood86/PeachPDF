using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>feImage</c> and the <c>FillPaint</c>/<c>StrokePaint</c> inputs of a raster SVG filter, drawn end to end: real markup is built
    /// into a scene graph, painted by <see cref="SvgRenderer"/> into a <see cref="RasterGraphics"/> host, and the pixels read back.
    /// </summary>
    public class SvgFilterInputsTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgDocument Build(string body)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">{body}</svg>""";
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), Adapter);
        }

        private static RasterGraphics Render(string body)
        {
            var document = Build(body);
            var host = new RasterGraphics(Adapter, new RasterSurface(100, 100, 0, 0, 1, 1), 1);
            SvgRenderer.RenderInto(host, document, new RRect(0, 0, 100, 100));
            return host;
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        // The element sits at 20..60; the default filter region is -10%..+120% of that, 16..64.
        private static string Doc(string filter, string elementAttributes = "", string extras = "") =>
            $"""<defs>{filter}</defs>{extras}<rect x="20" y="20" width="40" height="40" filter="url(#f)" {elementAttributes}/>""";

        [Fact]
        public void FillPaint_FillsTheFilterRegionWithTheElementsFill()
        {
            var host = Render(Doc("""<filter id="f" color-interpolation-filters="sRGB"><feMerge><feMergeNode in="FillPaint"/></feMerge></filter>""", "fill=\"rgb(255,0,0)\""));

            Assert.Equal([255, 0, 0, 255], Pixel(host, 17, 17));
            Assert.Equal([255, 0, 0, 255], Pixel(host, 62, 62));
            Assert.Equal(0, Pixel(host, 10, 10)[3]);
            Assert.Equal(0, Pixel(host, 70, 40)[3]);
        }

        [Fact]
        public void StrokePaint_WithNoStroke_IsTransparent()
        {
            var host = Render(Doc("""<filter id="f"><feMerge><feMergeNode in="StrokePaint"/></feMerge></filter>""", "fill=\"red\""));

            Assert.Equal(0, Pixel(host, 40, 40)[3]);
            Assert.Equal(0, Pixel(host, 17, 17)[3]);
        }

        [Fact]
        public void StrokePaint_FillsTheRegionWithTheStrokeColourNotTheStrokeGeometry()
        {
            var host = Render(Doc("""<filter id="f" color-interpolation-filters="sRGB"><feMerge><feMergeNode in="StrokePaint"/></feMerge></filter>""", "fill=\"red\" stroke=\"rgb(0,0,255)\" stroke-width=\"2\""));

            Assert.Equal([0, 0, 255, 255], Pixel(host, 40, 40));
            Assert.Equal([0, 0, 255, 255], Pixel(host, 17, 40));
        }

        [Fact]
        public void FillPaint_WithAGradient_RunsAcrossTheRegion()
        {
            var gradient = """<linearGradient id="g" gradientUnits="userSpaceOnUse" x1="16" y1="0" x2="64" y2="0"><stop offset="0" stop-color="rgb(255,0,0)"/><stop offset="1" stop-color="rgb(0,0,255)"/></linearGradient>""";
            var host = Render(Doc(gradient + """<filter id="f" color-interpolation-filters="sRGB"><feMerge><feMergeNode in="FillPaint"/></feMerge></filter>""", "fill=\"url(#g)\""));

            var left = Pixel(host, 18, 40);
            var right = Pixel(host, 61, 40);
            Assert.True(left[0] > 200 && left[2] < 60, $"left was {string.Join(",", left)}");
            Assert.True(right[2] > 200 && right[0] < 60, $"right was {string.Join(",", right)}");
        }

        [Fact]
        public void FeImage_WithAnImage_FillsTheFilterRegion()
        {
            var host = Render(Doc($"""<filter id="f" color-interpolation-filters="sRGB"><feImage href="{RasterPngFixture.OnePixelDataUri}" preserveAspectRatio="none"/></filter>"""));

            Assert.Equal([255, 0, 0, 255], Pixel(host, 17, 17));
            Assert.Equal([255, 0, 0, 255], Pixel(host, 62, 62));
            Assert.Equal(0, Pixel(host, 10, 10)[3]);
        }

        [Fact]
        public void FeImage_WithASubregion_FillsOnlyThatSubregion()
        {
            var host = Render(Doc($"""<filter id="f" color-interpolation-filters="sRGB"><feImage href="{RasterPngFixture.OnePixelDataUri}" x="30" y="30" width="10" height="10" preserveAspectRatio="none"/></filter>"""));

            Assert.Equal([255, 0, 0, 255], Pixel(host, 35, 35));
            Assert.Equal(0, Pixel(host, 25, 35)[3]);
            Assert.Equal(0, Pixel(host, 45, 35)[3]);
        }

        [Fact]
        public void FeImage_WithAMeetAspectRatio_LeavesTheRestOfTheSubregionTransparent()
        {
            // A 1 x 1 image in a 40 x 20 subregion, xMidYMid meet: a 20 x 20 square in the middle.
            var host = Render(Doc($"""<filter id="f" color-interpolation-filters="sRGB"><feImage href="{RasterPngFixture.OnePixelDataUri}" x="20" y="30" width="40" height="20"/></filter>"""));

            Assert.Equal([255, 0, 0, 255], Pixel(host, 40, 40));
            Assert.Equal(0, Pixel(host, 25, 40)[3]);
            Assert.Equal(0, Pixel(host, 55, 40)[3]);
        }

        [Fact]
        public void FeImage_WithAnElementReference_DrawsTheElementInTheFilteredElementsUserSpace()
        {
            var host = Render(Doc("""<filter id="f" color-interpolation-filters="sRGB"><feImage href="#src"/></filter>""",
                extras: """<rect id="src" x="25" y="25" width="10" height="10" fill="rgb(0,255,0)"/>"""));

            // The referenced rectangle is also an ordinary child, so look where only the filter draws it: inside the filter region.
            Assert.Equal([0, 255, 0, 255], Pixel(host, 30, 30));
        }

        [Fact]
        public void FeImage_WithAnElementReferenceAndSubregionOrigin_TranslatesTheElement()
        {
            var host = Render(Doc("""<filter id="f" color-interpolation-filters="sRGB"><feImage href="#src" x="20" y="0"/></filter>""",
                extras: """<defs><rect id="src" x="25" y="25" width="10" height="10" fill="rgb(0,255,0)"/></defs>"""));

            Assert.Equal(0, Pixel(host, 30, 30)[3]);
            Assert.Equal([0, 255, 0, 255], Pixel(host, 50, 30));
        }

        [Fact]
        public void FeImage_WithAnUnresolvableReference_IsTransparent()
        {
            var host = Render(Doc("""<filter id="f"><feImage href="#nothing"/></filter>"""));

            Assert.Equal(0, Pixel(host, 40, 40)[3]);
        }

        [Fact]
        public void FeImage_ThatNamesAnElementFilteredByItself_DoesNotRecurseForever()
        {
            var host = Render("""<defs><filter id="f"><feImage href="#self"/></filter></defs><rect id="self" x="20" y="20" width="40" height="40" filter="url(#f)"/>""");

            Assert.NotNull(host);
        }

        [Fact]
        public void BackgroundInputs_WithNothingBehindThem_AreTransparent()
        {
            var host = Render(Doc("""<filter id="f"><feMerge><feMergeNode in="BackgroundImage"/></feMerge></filter>"""));

            Assert.Equal(0, Pixel(host, 40, 40)[3]);
        }

        [Fact]
        public void Filter_ReadingTheReservedInputs_IsRasterAndRegistered()
        {
            var document = Build(Doc("""<filter id="f"><feOffset in="FillPaint" dx="1" dy="1"/></filter>"""));

            var filter = document.Filters["f"];
            Assert.True(filter.RequiresRaster);
            Assert.True(filter.Primitives[0].ReadsReservedInput);
            Assert.False(document.ReadsBackdrop);
        }

        [Fact]
        public void Filter_ReadingTheBackdrop_MarksTheDocument()
        {
            var document = Build(Doc("""<filter id="f"><feBlend in="SourceGraphic" in2="BackgroundImage" mode="multiply"/></filter>"""));

            Assert.True(document.ReadsBackdrop);
        }
    }
}
