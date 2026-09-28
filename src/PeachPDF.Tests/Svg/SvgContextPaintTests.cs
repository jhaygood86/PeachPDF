using PeachPDF.Adapters;
using PeachDrawing.Abstractions;
using PeachDrawing;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
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
            using var graphics = new RasterCanvas(Adapter, surface, 1);
            SvgRenderer.RenderInto(graphics, Build(markup, contextFill, contextStroke), new Rect(0, 0, 100, 100));
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
                """, SvgPaint.Solid(PaintColor.FromArgb(255, 255, 0, 0)), SvgPaint.Solid(PaintColor.FromArgb(255, 0, 0, 255)));

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
                """, SvgPaint.Solid(PaintColor.FromArgb(255, 255, 0, 0)), SvgPaint.Solid(PaintColor.FromArgb(255, 0, 0, 255)));

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
            AssertColour(surface, 43, 45, Blue);      // its edge, away from the path's own stroke: the path's stroke
        }

        [Fact]
        public void AnElementBetweenTheUseAndTheKeyword_DoesNotBreakTheContext()
        {
            // With plain inheritance the blue group would win; the context element is the use, whatever lies between.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs><g id="g" fill="#0000ff" stroke="#00ff00"><rect width="40" height="40" fill="context-fill" stroke="context-stroke" stroke-width="10"/></g></defs>
                  <use xlink:href="#g" x="30" y="30" fill="#ff0000" stroke="#ffff00"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Red);
            AssertColour(surface, 32, 50, (255, 255, 0));
        }

        [Fact]
        public void AKeywordOnTheMarkerElementItself_IsResolvedForEachInstance()
        {
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="16" markerHeight="16" refX="8" refY="8" markerUnits="userSpaceOnUse" fill="context-stroke">
                      <rect width="16" height="16"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#00ff00" stroke="#0000ff" stroke-width="2" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Blue);
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
                """, SvgPaint.Solid(PaintColor.FromArgb(255, 255, 0, 0)));

            AssertColour(surface, 50, 50, Green);
        }

        [Fact]
        public void AGradientOnTheShapeOfAMarker_IsMappedThroughThePlacementTransform()
        {
            // The path (its own fill, no context element yet) is a zero-height horizontal segment from
            // x=10 to x=90 - the gradient's default vector runs the same way, red at x=10 to blue at
            // x=90. The marker is placed at the path's end (90,50), refX/refY centering it there, so its
            // own frame sits 80 units to the right of - and independent of any rotation from - the
            // path's frame. Mapping the gradient's box through the placement's inverse should land the
            // marker's center exactly on the gradient's blue end (t=1); not doing that (the old
            // behaviour: collapsing the paint to none) would instead paint nothing at all.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <marker id="m" markerWidth="20" markerHeight="20" refX="10" refY="10" markerUnits="userSpaceOnUse">
                      <rect width="20" height="20" fill="context-fill"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L90,50" fill="url(#g)" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 90, 50, Blue);
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
        public void AGradientThroughAUse_UndoesATransformBetweenTheTargetAndThePaintingElement()
        {
            // "pair"'s total box spans both rects once the inner <g>'s transform is taken into account: (0,0,20,40)
            // for the first rect, unioned with (20,0,20,40) for the second (its own raw (0,0,20,40) box mapped
            // through translate(20,0)) - (0,0,40,40) altogether. Mapped correctly into rect2's own frame (undoing
            // that same translate), every point inside rect2 sits at t >= 0.5 - at least half way to blue - since
            // rect2 only ever covers the right half of that combined box. Measuring against rect2's own untransformed
            // 20x40 extent instead (not undoing the inner transform) would run the whole 0..1 gradient across rect2
            // alone, making its left edge pure red (t=0).
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <g id="pair">
                      <rect width="20" height="40" fill="#00ff00"/>
                      <g transform="translate(20,0)"><rect width="20" height="40" fill="context-fill"/></g>
                    </g>
                  </defs>
                  <use xlink:href="#pair" x="10" y="30" fill="url(#g)"/>
                </svg>
                """);

            // rect2 occupies document x 30..50, y 30..70 (the use's x/y plus the inner g's translate(20,0)) -
            // sample just inside its left edge, where the effect is clearest.
            var (r, _, b, a) = At(surface, 31, 50);
            Assert.Equal(255, a);
            Assert.True(b >= r, $"expected at least half way to blue at rect2's left edge, got r={r} b={b}");
        }

        [Fact]
        public void AGradientThroughAUse_IsMeasuredCorrectlyWhenTheTargetPaintsThroughATile()
        {
            // The use's target has its own opacity, so RenderElement paints it through Canvas.CreateTile's
            // isolated tile (RenderContainerOpacityGroup) rather than directly on the outer graphics - the same
            // tile a mask or a <pattern> fill uses. Unless that tile's own CurrentTransform is seeded from the
            // outer graphics' (Canvas.CreateTile's contract), ContextBounds composes the context element's
            // recorded frame against the wrong baseline and gets a materially different (nonsensical) box.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <g id="grp" opacity="0.5"><rect width="40" height="40" fill="context-fill"/></g>
                  </defs>
                  <use xlink:href="#grp" x="10" y="30" fill="url(#g)"/>
                </svg>
                """);

            // The rect's own center (document (10,30) + local (20,20)) - correctly measured, the box the gradient
            // resolves against here already runs more than half way to blue; measured against the wrong baseline
            // (an unseeded tile), it instead lands under half way, still mostly red. Alpha is the group's own
            // opacity (0.5), not full - only the colour balance is the assertion here.
            var (r, _, b, a) = At(surface, 30, 50);
            Assert.True(a > 0);
            Assert.True(b > r, $"expected the correctly-mapped box to already read past the midpoint at the rect's own center, got r={r} b={b}");
        }

        [Fact]
        public void AGradientThroughAUse_OfASymbol_IsMeasuredAgainstTheSymbolsWholeContent()
        {
            // The symbol's viewBox is "0 0 20 20"; the use sizes it to 40x40 (a 2x scale) at (10,10) - established
            // viewport device x:10..50, y:10..50. rect1 (viewBox x 0..10) is green; rect2 (viewBox x 10..20, the
            // right half, fill="context-fill") maps to device x 30..50. The gradient runs left-to-right across the
            // *whole* symbol content (device x 10..50): rect2's own left edge (viewBox x=10, device x=30) sits
            // exactly half way (t=0.5) - a balanced, mid-gradient colour, not pure red.
            //
            // Before the fix, SvgGeometryBounds.GetBoundingBox had no case for <symbol>, so ContextBounds fell back
            // to rect2's own (much narrower, viewBox-only) box - measuring the gradient across just rect2's own
            // 10-wide span instead of the symbol's full 20-wide content, landing near-pure red at this same point.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <symbol id="s" viewBox="0 0 20 20">
                      <rect width="10" height="20" fill="#00ff00"/>
                      <rect x="10" width="10" height="20" fill="context-fill"/>
                    </symbol>
                  </defs>
                  <use xlink:href="#s" x="10" y="10" width="40" height="40" fill="url(#g)"/>
                </svg>
                """);

            var (r, _, b, a) = At(surface, 31, 30);
            Assert.Equal(255, a);
            Assert.InRange(r, 100, 160);
            Assert.InRange(b, 100, 160);
        }

        [Fact]
        public void AGradientThroughAUse_OfANestedSvg_IsMeasuredAgainstItsWholeContent()
        {
            // Same geometry as the <symbol> case above, but the use targets a nested <svg> definition instead - a
            // second element type SvgGeometryBounds.GetBoundingBox had no case for, with its own separate
            // RenderElementSwitch/RenderViewport arm to record the context frame for.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <svg id="s" viewBox="0 0 20 20" width="20" height="20">
                      <rect width="10" height="20" fill="#00ff00"/>
                      <rect x="10" width="10" height="20" fill="context-fill"/>
                    </svg>
                  </defs>
                  <use xlink:href="#s" x="10" y="10" width="40" height="40" fill="url(#g)"/>
                </svg>
                """);

            var (r, _, b, a) = At(surface, 31, 30);
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
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), rect.Fill.PaintColor);
            Assert.Equal(SvgPaintKind.None, rect.Stroke.Kind);
        }

        [Fact]
        public void TheKeywords_AreValidPaintValues()
        {
            Assert.True(SvgValueParsers.TryParsePaint("context-fill", Adapter, PaintColor.Black, out var fill));
            Assert.Equal(SvgPaintKind.ContextFill, fill.Kind);
            Assert.True(SvgValueParsers.TryParsePaint(" context-stroke ", Adapter, PaintColor.Black, out var stroke));
            Assert.Equal(SvgPaintKind.ContextStroke, stroke.Kind);
            Assert.False(SvgValueParsers.TryParsePaint("context-nothing", Adapter, PaintColor.Black, out _));
        }

        [Fact]
        public void TextInAMarker_ResolvesContextFillToThePaintOfTheShapeItIsOn()
        {
            // Before the fix, marker <text> read run.Fill/run.Stroke directly (never through ResolveInMarker), so
            // context-fill stayed the unresolved keyword forever - DrawString's fast (solid-fill) path only runs
            // for SvgPaintKind.Solid, so an unresolved keyword would fall through to painting nothing at all.
            var document = Build($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="20" markerHeight="20" refX="10" refY="18" markerUnits="userSpaceOnUse">
                      <text x="0" y="10" font-size="10" fill="context-fill">A</text>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#ff0000" marker-end="url(#m)"/>
                </svg>
                """);

            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 100, 100));

            var call = Assert.Single(g.DrawStringCalls);
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), call.PaintColor);
        }

        [Fact]
        public void TextInAMarker_ResolvesContextFillForItsTextDecorationToo()
        {
            // Before the fix, DrawDecorationSpan read decorator.Fill directly (bypassing ResolveInMarker like the
            // glyphs themselves used to), so an unresolved context-fill keyword never matched SvgPaintKind.Solid
            // and the underline fell back to black instead of the shape's own (red) fill.
            var document = Build($"""
                <svg {Svg}>
                  <defs>
                    <marker id="m" markerWidth="20" markerHeight="20" refX="10" refY="18" markerUnits="userSpaceOnUse">
                      <text x="0" y="10" font-size="10" fill="context-fill" text-decoration-line="underline">A</text>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#ff0000" marker-end="url(#m)"/>
                </svg>
                """);

            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 100, 100));

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), line.PaintColor);
        }

        [Fact]
        public void AFilterInputOnAMarkerShape_ResolvesContextFillToThePaintOfTheShapeItIsOn()
        {
            // Before the fix, RendererFilterInputs.PaintOf read element.Fill/element.Stroke directly, so a marker
            // shape's FillPaint/StrokePaint filter input stayed the unresolved keyword - which ResolvePaintBrush's
            // switch doesn't recognise, so nothing painted.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <filter id="f" color-interpolation-filters="sRGB"><feMerge><feMergeNode in="FillPaint"/></feMerge></filter>
                    <marker id="m" markerWidth="20" markerHeight="20" refX="10" refY="10" markerUnits="userSpaceOnUse">
                      <rect width="20" height="20" fill="context-fill" filter="url(#f)"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L50,50" fill="#ff0000" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 50, 50, Red);
        }

        [Fact]
        public void AFilterInputOnAMarkerShape_WithAGradientContextFill_IsMappedThroughThePlacementTransform()
        {
            // Same geometry as AGradientOnTheShapeOfAMarker_IsMappedThroughThePlacementTransform, but the marker
            // rect reads the gradient through a FillPaint filter input instead of painting it directly - proving
            // RendererFilterInputs.PaintOf resolves a gradient/pattern context-fill (not just a solid one) and that
            // ContextBounds still maps it correctly once the raster filter's own tile (Canvas.BeginRasterSurface,
            // seeded from the calling graphics unlike Canvas.CreateTile) is in the picture.
            var surface = Paint($"""
                <svg {Svg}>
                  <defs>
                    <linearGradient id="g"><stop offset="0" stop-color="#ff0000"/><stop offset="1" stop-color="#0000ff"/></linearGradient>
                    <filter id="f" color-interpolation-filters="sRGB"><feMerge><feMergeNode in="FillPaint"/></feMerge></filter>
                    <marker id="m" markerWidth="20" markerHeight="20" refX="10" refY="10" markerUnits="userSpaceOnUse">
                      <rect width="20" height="20" fill="context-fill" filter="url(#f)"/>
                    </marker>
                  </defs>
                  <path d="M10,50 L90,50" fill="url(#g)" marker-end="url(#m)"/>
                </svg>
                """);

            AssertColour(surface, 90, 50, Blue);
        }
    }
}
