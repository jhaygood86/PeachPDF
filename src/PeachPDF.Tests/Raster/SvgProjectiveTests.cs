using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.PdfBackend;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// 3D transforms on SVG elements (CSS Transforms 2 section 11): <c>rotateX/Y()</c>, <c>translateZ()</c>, <c>perspective()</c>, a container's
    /// <c>perspective</c>, <c>backface-visibility</c> and <c>transform-style: preserve-3d</c>. They rasterize an inline SVG and look at pixels, as the HTML
    /// 3D tests do: a token in a content stream would prove nothing about what lands where.
    /// </summary>
    public class SvgProjectiveTests
    {
        private const string Red = "#ff0000";
        private const string Blue = "#0000ff";

        private static async Task<RasterCanvas> Paint(string svgBody)
        {
            // One user unit is one point, so a pixel of the 220 x 180 surface is a user unit.
            var html = $"""<svg viewBox="0 0 220 180" style="display:block;width:220pt;height:180pt">{svgBody}</svg>""";
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(html), margin: 0);
            var page = new RasterCanvas(new PdfRenderContext(), new RasterSurface(220, 180, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        private static byte[] Pixel(RasterCanvas g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static bool IsCovered(RasterCanvas g, int x, int y) => Pixel(g, x, y)[3] > 127;

        private static int ColumnCoverage(RasterCanvas g, int x)
        {
            var n = 0;
            for (var y = 0; y < g.Surface.Height; y++)
                if (IsCovered(g, x, y))
                    n++;
            return n;
        }

        private static void Near(int expected, int actual, int tolerance) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

        /// <summary>The first and last covered pixel of a row.</summary>
        private static (int First, int Last) HorizontalExtent(RasterCanvas g, int y)
        {
            int first = -1, last = -1;
            for (var x = 0; x < g.Surface.Width; x++)
            {
                if (!IsCovered(g, x, y))
                    continue;
                if (first < 0)
                    first = x;
                last = x;
            }

            return (first, last);
        }

        private static int RowExtent(RasterCanvas g, int y)
        {
            var n = 0;
            for (var x = 0; x < g.Surface.Width; x++)
                if (IsCovered(g, x, y))
                    n++;
            return n;
        }

        /// <summary>A 100 x 100 square at (60, 40), turned about its own centre.</summary>
        private static string Square(string colour, string style, string attributes = "") =>
            $"""<rect x="60" y="40" width="100" height="100" fill="{colour}" style="transform-origin:110px 90px;{style}" {attributes}/>""";

        // ---- The plain cases: rotations with no perspective stay affine (vector) ------------------------------------

        [Fact]
        public async Task RotateY_WithoutPerspective_NarrowsTheSquareByTheCosine()
        {
            var flat = await Paint(Square(Red, ""));
            var turned = await Paint(Square(Red, "transform:rotateY(60deg)"));

            Near(100, RowExtent(flat, 90), 2);
            Near(50, RowExtent(turned, 90), 3); // cos 60 degrees
            Assert.False(IsCovered(turned, 70, 90));    // outside the narrowed square
            Assert.True(IsCovered(turned, 110, 90));
        }

        [Fact]
        public async Task TranslateZ_WithoutPerspective_ChangesNothingOnItsOwn()
        {
            var moved = await Paint(Square(Red, "transform:translateZ(80px)"));

            Near(100, RowExtent(moved, 90), 2);
        }

        [Fact]
        public async Task TranslateZ_UnderAContainersPerspective_ScalesThePlane()
        {
            // perspective 400: a plane brought 100 nearer is 400 / 300 larger.
            var near = await Paint($"""<g style="perspective:400px;perspective-origin:110px 90px">{Square(Red, "transform:translateZ(100px)")}</g>""");

            Near(133, RowExtent(near, 90), 3);
        }

        // ---- Perspective ---------------------------------------------------------------------------------------------

        [Fact]
        public async Task ContainerPerspective_MakesTheNearEdgeOfATurnedPlaneLarger()
        {
            // rotateY(+50deg): the right side turns away from the viewer, the left comes towards it.
            var page = await Paint($"""<g style="perspective:300px;perspective-origin:110px 90px">{Square(Red, "transform:rotateY(50deg)")}</g>""");

            var (first, last) = HorizontalExtent(page, 90);
            var left = ColumnCoverage(page, first + 4);
            var right = ColumnCoverage(page, last - 4);
            Assert.True(last - first > 20, $"the plane is drawn (spans {first}..{last})");
            Assert.True(left > right + 10, $"the near side is taller than the far one (left {left}, right {right})");
        }

        [Fact]
        public async Task PerspectiveFunction_InTheTransformList_ProjectsTheElementItself()
        {
            var page = await Paint(Square(Red, "transform:perspective(300px) rotateY(50deg)"));

            var (first, last) = HorizontalExtent(page, 90);
            Assert.True(ColumnCoverage(page, first + 4) > ColumnCoverage(page, last - 4) + 10);
        }

        [Fact]
        public async Task Perspective_OnTheElementItself_DoesNotMoveIt()
        {
            // A container's perspective is for its children: on the rect it changes nothing.
            var flat = await Paint(Square(Red, ""));
            var page = await Paint(Square(Red, "perspective:100px"));

            Assert.Equal(RowExtent(flat, 90), RowExtent(page, 90));
        }

        [Fact]
        public async Task APerspectiveUnderOnePixel_IsOnePixel_NotAnImpossibleViewer()
        {
            var page = await Paint($"""<g style="perspective:0.01px">{Square(Red, "transform:rotateY(30deg)")}</g>""");

            // The viewer sits one pixel from the plane: the plane is hugely larger than the page or behind the eye, but nothing throws.
            Assert.NotNull(page);
        }

        // ---- backface-visibility, collapsed transforms ----------------------------------------------------------------

        [Fact]
        public async Task BackfaceVisibilityHidden_HidesAnElementTurnedAway()
        {
            var visibleBack = await Paint(Square(Red, "transform:rotateY(180deg)"));
            var hiddenBack = await Paint(Square(Red, "transform:rotateY(180deg);backface-visibility:hidden"));
            var facing = await Paint(Square(Red, "transform:rotateY(20deg);backface-visibility:hidden"));

            Assert.True(IsCovered(visibleBack, 110, 90));
            Assert.False(IsCovered(hiddenBack, 110, 90));
            Assert.True(IsCovered(facing, 110, 90));
        }

        [Fact]
        public async Task AnEdgeOnPlane_WithNoPerspective_PaintsNothing()
        {
            // rotateX(90deg) has a cosine of about 4e-8 in floats: not singular, but a hairline at most.
            var page = await Paint(Square(Red, "transform:rotateX(90deg)"));

            Assert.False(IsCovered(page, 110, 90));
            Assert.Equal(0, RowExtent(page, 90));
        }

        [Fact]
        public async Task AWarpedElementInsideAWarpedGroup_IsNotCroppedToTheOuterSpace()
        {
            // The inner card is drawn into the outer group's own bitmap, whose space is the group's, not the page's: it must still show whole.
            var inner = Square(Red, "transform:perspective(300px) rotateY(40deg)");
            var page = await Paint($"<g style=\"transform-origin:110px 90px;transform:perspective(900px) rotateX(20deg)\">{inner}</g>");

            var (first, last) = HorizontalExtent(page, 90);
            Assert.True(last - first > 40, $"the card is drawn whole (spans {first}..{last})");
        }

        [Fact]
        public async Task ACollapsedMatrix_PaintsNothing()
        {
            var page = await Paint(Square(Red, "transform:scale3d(1,1,0) rotateX(90deg)"));

            Assert.False(IsCovered(page, 110, 90));
        }

        [Fact]
        public async Task AnEdgeOnPlane_PaintsNothing()
        {
            var page = await Paint($"""<g style="perspective:300px;perspective-origin:110px 90px">{Square(Red, "transform:rotateY(90deg)")}</g>""");

            Assert.False(IsCovered(page, 110, 90));
        }

        // ---- A container's content moves with it ---------------------------------------------------------------------

        [Fact]
        public async Task AGroupWithA3DTransform_CarriesItsChildren()
        {
            var page = await Paint($"""<g style="transform-origin:110px 90px;transform:rotateY(60deg)"><rect x="60" y="40" width="100" height="100" fill="{Red}"/></g>""");

            Near(50, RowExtent(page, 90), 3);
        }

        [Fact]
        public async Task AFiltersOutput_IsWarpedWithTheElement()
        {
            var page = await Paint($"""
                <defs><filter id="f" x="-0.5" y="-0.5" width="2" height="2"><feOffset dx="0" dy="0"/></filter></defs>
                {Square(Red, "transform:rotateY(60deg);filter:url(#f)")}
                """);

            Near(50, RowExtent(page, 90), 3);
        }

        // ---- transform-style: preserve-3d ----------------------------------------------------------------------------

        [Fact]
        public async Task InA3DContext_ANearerPlaneIsOnTop_EvenWhenItComesFirst()
        {
            var children = Square(Red, "transform:translateZ(50px)") + Square(Blue, "");

            var shared = await Paint($"""<g style="transform-style:preserve-3d">{children}</g>""");
            var flat = await Paint($"""<g>{children}</g>""");

            Assert.Equal([255, 0, 0, 255], Pixel(shared, 110, 90));
            Assert.Equal([0, 0, 255, 255], Pixel(flat, 110, 90));
        }

        [Fact]
        public async Task InA3DContext_IntersectingPlanesAreResolvedWhereTheyCross()
        {
            // Two squares crossing like an X seen from above: red is rotated one way, blue the other. Each is in front on its own side of the crossing.
            var children = Square(Red, "transform:rotateY(45deg)") + Square(Blue, "transform:rotateY(-45deg)");
            var page = await Paint($"""<g style="transform-style:preserve-3d;perspective:600px;perspective-origin:110px 90px">{children}</g>""");

            var left = Pixel(page, 90, 90);
            var right = Pixel(page, 130, 90);
            Assert.NotEqual(left[0..3], right[0..3]);
        }

        [Fact]
        public async Task AnOpacityOnTheContainer_FlattensTheContext()
        {
            // opacity < 1 forces transform-style: flat (CSS Transforms 2 section 7.1): document order decides again, as without preserve-3d.
            var children = Square(Red, "transform:translateZ(50px)") + Square(Blue, "");
            var page = await Paint($"""<g style="transform-style:preserve-3d;opacity:0.999">{children}</g>""");

            Assert.True(Pixel(page, 110, 90)[2] > 200);
        }

        [Fact]
        public async Task APlaneTurnedAwayWithBackfaceHidden_IsNotDrawnInAContext()
        {
            var children = Square(Red, "transform:rotateY(180deg);backface-visibility:hidden") + Square(Blue, "transform:translateZ(10px)");
            var page = await Paint($"""<g style="transform-style:preserve-3d">{children}</g>""");

            Assert.Equal([0, 0, 255, 255], Pixel(page, 110, 90));
        }

        // ---- 3D functions where the whole list is ignored -------------------------------------------------------------

        [Fact]
        public async Task AGradientTransformWithA3DFunction_IsIgnoredWhole()
        {
            var flat = await Paint("""
                <defs><linearGradient id="g" x1="0" x2="1"><stop offset="0" stop-color="#f00"/><stop offset="1" stop-color="#00f"/></linearGradient></defs>
                <rect x="60" y="40" width="100" height="100" fill="url(#g)"/>
                """);
            var with3D = await Paint("""
                <defs><linearGradient id="g" x1="0" x2="1" gradientTransform="rotateY(60) translate(40,0)"><stop offset="0" stop-color="#f00"/><stop offset="1" stop-color="#00f"/></linearGradient></defs>
                <rect x="60" y="40" width="100" height="100" fill="url(#g)"/>
                """);

            Assert.Equal(Pixel(flat, 70, 90), Pixel(with3D, 70, 90));
            Assert.Equal(Pixel(flat, 150, 90), Pixel(with3D, 150, 90));
        }
    }
}
