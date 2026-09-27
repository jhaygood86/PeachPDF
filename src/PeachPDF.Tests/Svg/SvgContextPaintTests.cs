using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Svg;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>context-fill</c> and <c>context-stroke</c> (SVG 2, painting): the fill and stroke of the context element, which is the <c>use</c> for
    /// what it instantiates, the shape a marker is drawn on for the marker's content, and whatever a document was given for the rest (the text,
    /// for a glyph document). Each check paints through the raster graphics and reads pixels, so a keyword that painted nothing, or the wrong
    /// paint, fails.
    /// </summary>
    public class SvgContextPaintTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);
        private static readonly (byte R, byte G, byte B) Blue = (0, 0, 255);
        private static readonly (byte R, byte G, byte B) Green = (0, 255, 0);

        private static SvgDocument Build(string markup, SvgPaint? contextFill = null, SvgPaint? contextStroke = null)
        {
            var root = XDocument.Parse(markup).Root!;
            var css = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, css, "print"), Adapter, null, null, contextFill, contextStroke);
        }

        private static RasterSurface Paint(string markup, SvgPaint? contextFill = null, SvgPaint? contextStroke = null)
        {
            var surface = new RasterSurface(100, 100, 0, 0, 1, 1);
            using var graphics = new RasterGraphics(Adapter, surface, 1);
            SvgRenderer.RenderInto(graphics, Build(markup, contextFill, contextStroke), new RRect(0, 0, 100, 100));
            return surface;
        }

        private static (byte R, byte G, byte B, byte A) At(RasterSurface surface, int x, int y)
        {
            var row = surface.Row(y);
            return (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3]);
        }

        private static void AssertColour(RasterSurface surface, int x, int y, (byte R, byte G, byte B) expected)
        {
            var (r, g, b, a) = At(surface, x, y);
            Assert.Equal(255, a);
            Assert.Equal(expected, (r, g, b));
        }

        private static void AssertNothing(RasterSurface surface, int x, int y) => Assert.Equal(0, At(surface, x, y).A);

        private const string Svg = "xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 100 100\"";

        [Fact]
        public void AUse_IsTheContextElementOfWhatItInstantiates_WithADistinctFillAndStroke()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs><rect id="r" width="40" height="40" fill="context-fill" stroke="context-stroke" stroke-width="10"/></defs>
                  <use xlink:href="#r" x="30" y="30" fill="#ff0000" stroke="#0000ff"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Red);      // the interior: the use's fill
            AssertColour(surface, 32, 50, Blue);     // the edge, inside the stroke: the use's stroke
            AssertNothing(surface, 10, 10);
        }

        [Fact]
        public void TwoUsesOfOneShape_EachPaintWithTheirOwnFillAndStroke()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs><rect id="r" width="30" height="30" fill="context-fill" stroke="context-stroke" stroke-width="6"/></defs>
                  <use xlink:href="#r" x="10" y="10" fill="#ff0000" stroke="#0000ff"/>
                  <use xlink:href="#r" x="60" y="60" fill="#00ff00" stroke="#ff0000"/>
                </svg>
                """);

            AssertColour(surface, 25, 25, Red);
            AssertColour(surface, 11, 25, Blue);
            AssertColour(surface, 75, 75, Green);
            AssertColour(surface, 61, 75, Red);
        }

        [Fact]
        public void ANestedUse_ChangesTheContextForItsContent()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <rect id="r" width="40" height="40" fill="context-fill"/>
                    <g id="g"><use xlink:href="#r" fill="#00ff00"/></g>
                  </defs>
                  <use xlink:href="#g" x="20" y="20" fill="#ff0000"/>
                </svg>
                """);

            AssertColour(surface, 40, 40, Green);
        }

        [Fact]
        public void AContextKeyword_OnTheUseItself_ResolvesAgainstTheOuterContext()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <rect id="r" width="40" height="40" fill="context-fill"/>
                    <g id="g"><use xlink:href="#r" fill="context-fill"/></g>
                  </defs>
                  <use xlink:href="#g" x="20" y="20" fill="#ff0000"/>
                </svg>
                """);

            AssertColour(surface, 40, 40, Red);
        }

        [Fact]
        public void WithNoContextElement_TheKeywordsAreNoPaint()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <rect x="20" y="20" width="60" height="60" fill="context-fill" stroke="context-stroke" stroke-width="10"/>
                </svg>
                """);

            AssertNothing(surface, 50, 50);
            AssertNothing(surface, 20, 50);
        }

        [Fact]
        public void TheSeedGivenToTheBuild_IsWhatOutsideAUseOrMarkerPaintsWith()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <rect x="20" y="20" width="60" height="60" fill="context-fill" stroke="context-stroke" stroke-width="10"/>
                </svg>
                """, SvgPaint.Solid(RColor.FromArgb(255, 255, 0, 0)), SvgPaint.Solid(RColor.FromArgb(255, 0, 0, 255)));

            AssertColour(surface, 50, 50, Red);
            AssertColour(surface, 20, 50, Blue);
        }

        [Fact]
        public void TheSeed_ReachesAKeywordInAStyleAttributeAndAStylesheet()
        {
            var surface = Paint($$"""
                <svg {{Svg}}>
                  <style>.a { fill: context-fill }</style>
                  <rect class="a" x="10" y="10" width="30" height="30"/>
                  <rect x="60" y="60" width="30" height="30" style="fill: context-stroke"/>
                </svg>
                """, SvgPaint.Solid(RColor.FromArgb(255, 255, 0, 0)), SvgPaint.Solid(RColor.FromArgb(255, 0, 0, 255)));

            AssertColour(surface, 25, 25, Red);
            AssertColour(surface, 75, 75, Blue);
        }

        [Fact]
        public void TheKeywordIsCaseInsensitive_AndAFillCanTakeTheStroke()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs><rect id="r" width="40" height="40" fill="CONTEXT-STROKE"/></defs>
                  <use xlink:href="#r" x="30" y="30" fill="#ff0000" stroke="#0000ff"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Blue);
        }

        [Fact]
        public void AMarkerDrawsWithThePaintOfTheShapeItIsOn()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="16" markerHeight="16" refX="8" refY="8" markerUnits="userSpaceOnUse">
                      <rect width="16" height="16" fill="context-fill" stroke="context-stroke" stroke-width="4"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#00ff00" stroke="#0000ff" stroke-width="2" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Green);     // the marker's interior: the path's fill
            AssertColour(surface, 43, 50, Blue);      // its edge: the path's stroke
        }

        [Fact]
        public void TwoShapesWithMarkers_EachGiveTheirOwnPaintToTheirMarker()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="16" markerHeight="16" refX="8" refY="8" markerUnits="userSpaceOnUse">
                      <rect width="16" height="16" fill="context-fill"/>
                    </marker>
                  </defs>
                  <path d="M10,20 L40,20" fill="#00ff00" stroke="#000000" marker-end="url(#m)"/>
                  <path d="M10,70 L40,70" fill="#ff0000" stroke="#000000" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 40, 20, Green);
            AssertColour(surface, 40, 70, Red);
        }

        [Fact]
        public void AMarkersContext_IsNotTheSeed()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="16" markerHeight="16" refX="8" refY="8" markerUnits="userSpaceOnUse">
                      <rect width="16" height="16" fill="context-fill"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#00ff00" stroke="#000000" marker-end="url(#m)"/>
                </svg>
                """, SvgPaint.Solid(RColor.FromArgb(255, 255, 0, 0)));

            AssertColour(surface, 50, 50, Green);
        }

        [Fact]
        public void AGradientOnTheShapeOfAMarker_IsNotCarriedIntoIt()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <marker id="m" markerWidth="16" markerHeight="16" refX="8" refY="8" markerUnits="userSpaceOnUse">
                      <rect width="16" height="16" fill="context-fill"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="url(#g)" stroke="#000000" marker-end="url(#m)"/>
                </svg>
                """);

            AssertNothing(surface, 55, 50);
        }

        [Fact]
        public void AGradientThroughAUse_IsMeasuredAgainstTheUse_NotAgainstTheShapeThatDrawsIt()
        {
            // The use instantiates two squares side by side. A gradient from the use runs across both, so at the far edge of the first
            // square it is about half way; measured against that square alone it would be all the way to blue.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <g id="pair"><rect width="40" height="40" fill="context-fill"/><rect x="40" width="40" height="40" fill="#00ff00"/></g>
                  </defs>
                  <use xlink:href="#pair" x="10" y="30" fill="url(#g)"/>
                </svg>
                """);

            var (r, _, b, a) = At(surface, 48, 50);
            Assert.Equal(255, a);
            Assert.InRange(r, 100, 160);
            Assert.InRange(b, 100, 160);
        }

        [Fact]
        public void ASolidUseFill_DoesNotNeedAnythingOfTheContextElement()
        {
            var document = Build($"""
                <svg {Svg}>
                  <defs><rect id="r" width="40" height="40" fill="context-fill" stroke="context-stroke"/></defs>
                  <use xlink:href="#r" fill="#ff0000"/>
                </svg>
                """);

            var use = Assert.IsType<SvgUseElement>(Assert.Single(document.Children));
            var rect = Assert.IsType<SvgRectElement>(use.Target);
            Assert.Equal(SvgPaintKind.Solid, rect.Fill.Kind);
            Assert.Equal(RColor.FromArgb(255, 255, 0, 0), rect.Fill.Color);
            Assert.Equal(SvgPaintKind.None, rect.Stroke.Kind);
        }

        [Fact]
        public void TheKeywords_AreValidPaintValues()
        {
            Assert.True(SvgValueParsers.TryParsePaint("context-fill", Adapter, RColor.Black, out var fill));
            Assert.Equal(SvgPaintKind.ContextFill, fill.Kind);
            Assert.True(SvgValueParsers.TryParsePaint(" context-stroke ", Adapter, RColor.Black, out var stroke));
            Assert.Equal(SvgPaintKind.ContextStroke, stroke.Kind);
            Assert.False(SvgValueParsers.TryParsePaint("context-nothing", Adapter, RColor.Black, out _));
        }
    }
}
