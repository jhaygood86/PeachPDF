using PeachPDF.Adapters;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// <c>transform-style: preserve-3d</c>: nested planes sharing one 3D space, depth-tested per pixel where they cross. The painter tests
    /// rasterize a page and look at pixels (a token in a content stream would prove nothing about what is on top of what).
    /// </summary>
    public class Preserve3DTests
    {
        private const string Red = "#ff0000";
        private const string Blue = "#0000ff";
        private const string Green = "#00aa00";
        private const string Yellow = "#ffff00";

        private static async Task<RasterGraphics> Paint(string body, int width = 220, int height = 180)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            var page = new RasterGraphics(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static int Coverage(RasterGraphics g, int x)
        {
            var n = 0;
            for (var y = 0; y < g.Surface.Height; y++)
                if (Pixel(g, x, y)[3] > 127)
                    n++;
            return n;
        }

        /// <summary>A 200 x 160 stage whose contents are absolutely positioned, with an optional perspective and a 3D context on the root of the rest.</summary>
        private static string Stage(string contextStyle, string children, string stageStyle = "") => $$"""
            <div style="position:relative;margin:0;width:200pt;height:160pt;{{stageStyle}}">
              <div style="position:absolute;left:0;top:0;width:200pt;height:160pt;{{contextStyle}}">{{children}}</div>
            </div>
            """;

        private static string Plane(string colour, string style, string extra = "") =>
            $"<div style=\"position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:{colour};{style}\">{extra}</div>";

        // ---- Depth beats document order ---------------------------------------------------------------------------------

        [Fact]
        public async Task ANearerPlane_IsOnTop_EvenWhenItComesFirstInTheDocument()
        {
            // Both cover the same rectangle. Red is brought forward, blue is later in the document: flat, blue wins; in a 3D context red does.
            var children = Plane(Red, "transform:translateZ(50pt)") + Plane(Blue, "");

            var shared = await Paint(Stage("transform-style:preserve-3d", children));
            var flat = await Paint(Stage("", children));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(shared, 100, 80));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(flat, 100, 80));
        }

        [Fact]
        public async Task ANearerPlane_IsOnTop_WhateverTheDocumentOrder()
        {
            var children = Plane(Blue, "") + Plane(Red, "transform:translateZ(50pt)");

            var page = await Paint(Stage("transform-style:preserve-3d", children));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 100, 80));
        }

        [Fact]
        public async Task ACoplanarChild_IsOnTopOfItsParent()
        {
            // The parent's own background is a plane at the same depth as its child: the later plane (the child) must win.
            var child = Plane(Green, "transform:translateZ(0)");
            var page = await Paint(Stage("transform-style:preserve-3d;background:" + Red, child + Plane(Blue, "transform:translateZ(10pt)")));

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 100, 80));
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 10, 10));
        }

        [Theory]
        [InlineData(150)]
        [InlineData(0)]
        public async Task AChildOfATiltedRoot_IsOnTopOfTheRootsOwnBackground_OnBothSidesOfTheTilt(int childLeft)
        {
            // The root is tilted, so its own background is not at one depth. A child lying in the same plane (at its far or its near end) must
            // still be drawn over that background, whichever way its centre sorts against the root's.
            var child = $"<div style=\"position:absolute;left:{childLeft}pt;top:30pt;width:50pt;height:100pt;background:{Green};transform:translateZ(0)\"></div>";

            var page = await Paint(Stage("transform-style:preserve-3d;transform:rotateY(40deg);background:" + Red, child));

            // Sample a point inside the projected child: its centre, mapped by the same rotation about the root's centre (100, 80).
            var (x, y) = ProjectedCentre(childLeft + 25, 80);
            var p = Pixel(page, x, y);
            Assert.True(p[1] > 150 && p[0] < 60, $"the child should be on top of the root's background, got {string.Join(",", p)}");
        }

        /// <summary>Where a point of the 200 x 160 root lands under <c>rotateY(40deg)</c> about its centre, with the stage's perspective of 400pt.</summary>
        private static (int X, int Y) ProjectedCentre(double x, double y)
        {
            var angle = 40 * Math.PI / 180;
            var dx = x - 100;
            var z = -dx * Math.Sin(angle);
            var scale = 400 / (400 - z);
            return ((int)Math.Round(100 + dx * Math.Cos(angle) * scale), (int)Math.Round(80 + (y - 80) * scale));
        }

        [Fact]
        public async Task AChildWithNoTransformOfItsOwn_IsPaintedInItsParentsPlaneInOrdinaryPaintOrder()
        {
            // An untransformed in-flow block and an absolutely positioned one over it: in CSS 2.1 the positioned one is on top. As separate coplanar
            // planes in tree order the later block would have covered it.
            var overlap = "<div style=\"position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:" + Red + "\"></div>" +
                          "<div style=\"width:100pt;height:100pt;margin:30pt 0 0 50pt;background:" + Blue + "\"></div>";

            var page = await Paint(Stage("transform-style:preserve-3d", overlap + Plane(Green, "transform:translateZ(-30pt);left:10pt;width:20pt")));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 100, 80));
        }

        // ---- Intersecting planes are resolved per pixel -----------------------------------------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TwoPlanesThatCross_ShowTheNearerOneOnEachSideOfTheLine(bool swapDocumentOrder)
        {
            // rotateY(45deg) takes the left half of a card towards the viewer, rotateY(-45deg) the right half: they cross down the middle.
            var left = Plane(Red, "transform:rotateY(45deg)");
            var right = Plane(Blue, "transform:rotateY(-45deg)");

            var page = await Paint(Stage("transform-style:preserve-3d", swapDocumentOrder ? right + left : left + right));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 82, 80));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 118, 80));
        }

        [Fact]
        public async Task TheSameTwoPlanesFlattened_AreJustDocumentOrder()
        {
            var page = await Paint(Stage("", Plane(Red, "transform:rotateY(45deg)") + Plane(Blue, "transform:rotateY(-45deg)")));

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 82, 80));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 118, 80));
        }

        // ---- Perspective reaches past the direct children ---------------------------------------------------------------

        private static string Grandchild(string contextStyle) => Stage(contextStyle,
            "<div style=\"position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;transform-style:preserve-3d;transform:rotateY(60deg)\">" +
            $"<div style=\"position:absolute;left:0;top:0;width:100pt;height:100pt;background:{Green}\"></div></div>",
            "perspective:200pt");

        [Fact]
        public async Task APerspective_ReachesAGrandchildThroughThe3DContext()
        {
            var page = await Paint(Grandchild("transform-style:preserve-3d"));

            var columns = Enumerable.Range(0, page.Surface.Width).Where(x => Coverage(page, x) > 0).ToList();
            Assert.NotEmpty(columns);

            // Seen in perspective, the near edge is taller than the far one; without it the edges stay parallel.
            Assert.True(Math.Abs(Coverage(page, columns.First() + 2) - Coverage(page, columns.Last() - 2)) > 8);
        }

        [Fact]
        public async Task WithoutA3DContext_APerspectiveDoesNotReachAGrandchild()
        {
            var page = await Paint(Grandchild(""));

            var columns = Enumerable.Range(0, page.Surface.Width).Where(x => Coverage(page, x) > 0).ToList();
            Assert.NotEmpty(columns);
            Assert.InRange(Math.Abs(Coverage(page, columns.First() + 2) - Coverage(page, columns.Last() - 2)), 0, 2);
        }

        // ---- A cube ------------------------------------------------------------------------------------------------------

        private static string Cube(string cubeStyle, string faceStyle = "", string text = "") =>
            Stage("transform-style:preserve-3d;" + cubeStyle,
                Plane(Red, "transform:translateZ(50pt);" + faceStyle, text) +
                Plane(Blue, "transform:rotateY(180deg) translateZ(50pt);" + faceStyle, text) +
                Plane(Green, "transform:rotateY(90deg) translateZ(50pt);" + faceStyle, text) +
                Plane(Yellow, "transform:rotateY(-90deg) translateZ(50pt);" + faceStyle, text),
                "perspective:400pt");

        [Fact]
        public async Task ACube_ShowsItsNearFace_AndItsBackFaceStaysBehindIt()
        {
            // Blue is the far face and comes second in the document: without depth it would be painted over the red one.
            var page = await Paint(Cube("transform:rotateY(25deg)"));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 100, 80));
        }

        [Fact]
        public async Task ACube_ShowsTheSideThatTurnsTowardsTheViewer()
        {
            // rotateY(25deg) turns the left face (yellow) towards the viewer and the right face (green) away.
            var page = await Paint(Cube("transform:rotateY(25deg)"));

            var yellow = 0;
            var green = 0;
            for (var x = 0; x < page.Surface.Width; x++)
            {
                var p = Pixel(page, x, 80);
                if (p[3] == 255 && p[0] == 255 && p[1] == 255 && p[2] == 0)
                    yellow++;
                if (p[3] == 255 && p[0] == 0 && p[1] == 170 && p[2] == 0)
                    green++;
            }

            Assert.True(yellow > 4, "the left face should be visible");
            Assert.Equal(0, green);
        }

        [Fact]
        public async Task ACubeWithBackfacesHidden_LeavesOnlyTheFacesTurnedTowardsTheViewer()
        {
            var page = await Paint(Cube("transform:rotateY(25deg)", "backface-visibility:hidden"));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(page, 100, 80));
            for (var x = 0; x < page.Surface.Width; x++)
            {
                var p = Pixel(page, x, 80);
                Assert.False(p[3] == 255 && p[0] == 0 && p[1] == 0 && p[2] == 255, "the far face must not show");
            }
        }

        // ---- A flip card -------------------------------------------------------------------------------------------------

        private static string FlipCard(string cardStyle) => Stage("transform-style:preserve-3d;" + cardStyle,
            Plane(Red, "backface-visibility:hidden") + Plane(Blue, "transform:rotateY(180deg);backface-visibility:hidden"));

        [Fact]
        public async Task AFlipCard_ShowsItsFront_UntilItIsTurnedOver()
        {
            var front = await Paint(FlipCard(""));
            var back = await Paint(FlipCard("transform:rotateY(180deg)"));

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(front, 100, 80));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(back, 100, 80));
        }

        // ---- Translucency blends in depth order --------------------------------------------------------------------------

        [Fact]
        public async Task ATranslucentNearPlane_BlendsOverTheOpaqueOneBehindIt()
        {
            // The blue plane is nearer but comes first in the document; half transparent, it must let the red one show through.
            var children = Plane(Blue, "transform:translateZ(50pt);opacity:0.5") + Plane(Red, "");

            var page = await Paint(Stage("transform-style:preserve-3d", children));

            var p = Pixel(page, 100, 80);
            Assert.Equal(255, p[3]);
            Assert.InRange((int)p[0], 120, 135);
            Assert.InRange((int)p[2], 120, 135);
        }

        // ---- Grouping properties force the used value to flat ------------------------------------------------------------

        [Theory]
        [InlineData("overflow:hidden")]
        [InlineData("opacity:0.99")]
        [InlineData("filter:grayscale(0)")]
        [InlineData("clip-path:inset(0)")]
        [InlineData("clip:rect(0,300pt,300pt,0)")]
        public async Task AGroupingProperty_FlattensTheChildren(string grouping)
        {
            // Depth order would put red on top; flattened, the later blue plane is.
            var children = Plane(Red, "transform:translateZ(50pt)") + Plane(Blue, "");

            var page = await Paint(Stage("transform-style:preserve-3d;" + grouping, children));

            // Blue (the later plane) is on top; a grouping property's own effect (opacity 0.99) may leave it a shade off pure.
            var p = Pixel(page, 100, 80);
            Assert.True(p[0] < 8 && p[1] < 8 && p[2] > 240, $"expected blue on top, got {string.Join(",", p)}");
        }

        [Theory]
        [InlineData("", true)]
        [InlineData("overflow:hidden", false)]
        [InlineData("opacity:0.5", false)]
        [InlineData("filter:blur(2px)", false)]
        [InlineData("mix-blend-mode:multiply", false)]
        [InlineData("clip-path:circle(50%)", false)]
        public async Task EstablishesPreserve3d_HonoursTheGroupingProperties(string extra, bool expected)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<div id=\"a\" style=\"transform-style:preserve-3d;{extra}\"><div id=\"b\" style=\"width:10pt;height:10pt\"></div></div>"), margin: 0);

            var a = LayoutHarness.FindById(root, "a")!;

            Assert.True(a.IsPreserve3dRequested);
            Assert.Equal(expected, DomUtils.EstablishesPreserve3d(a));
            Assert.False(DomUtils.EstablishesPreserve3d(LayoutHarness.FindById(root, "b")!));
        }

        [Fact]
        public async Task APreserve3dBox_IsAStackingContext_AndAFlatOneIsNot()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id=\"a\" style=\"transform-style:preserve-3d\">x</div><div id=\"b\">x</div><div id=\"c\" style=\"transform-style:preserve-3d;overflow:hidden\">x</div>"), margin: 0);

            Assert.True(DomUtils.IsStackingContextBox(LayoutHarness.FindById(root, "a")!));
            Assert.False(DomUtils.IsStackingContextBox(LayoutHarness.FindById(root, "b")!));
            Assert.False(DomUtils.IsStackingContextBox(LayoutHarness.FindById(root, "c")!));
        }

        [Theory]
        [InlineData("transform-style", "preserve-3d", true)]
        [InlineData("transform-style", "flat", true)]
        [InlineData("transform-style", "sideways", false)]
        public void TheProperty_IsRecognised(string property, string value, bool valid)
        {
            Assert.Equal(valid, CssPropertyRegistry.SupportsDeclaration(property, value));
        }

        // ---- Nothing to resolve stays vector; a real context is a bitmap -------------------------------------------------

        private static PdfGenerateConfig Config() => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            MarginLeft = 0,
            MarginTop = 0,
            MarginRight = 0,
            MarginBottom = 0,
        };

        private static int Placements(string pdf) =>
            Regex.Matches(pdf, @"q\s+[-\d.]+\s+0\s+0\s+[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q").Count;

        [Fact]
        public async Task AContextWhosePlanesAreAllParallelToTheView_StaysVector()
        {
            var body = Stage("transform-style:preserve-3d;transform:rotate(10deg)",
                Plane(Red, "transform:translate(10pt,5pt)", "Text") + Plane(Blue, "transform:rotate(20deg) scale(0.5)"));

            var pdf = await PdfObjectReader.GeneratePdf($"<html><body style=\"margin:0\">{body}</body></html>", Config());

            Assert.Equal(0, Placements(pdf));
        }

        [Fact]
        public async Task ARealContext_IsOneBitmap_AndKeepsItsTextSelectable()
        {
            var body = Cube("transform:rotateY(25deg)", text: "Findable");

            var pdf = await PdfObjectReader.GeneratePdf($"<html><body style=\"margin:0\">{body}</body></html>", Config());

            Assert.Equal(1, Placements(pdf));
            Assert.Contains("3 Tr", pdf);
        }

        [Fact]
        public async Task WithoutRasterSupport_TheContextFallsBackToPaintingItsBoxesOneAtATime()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Cube("transform:rotateY(25deg)")), margin: 0);
            var recording = new RecordingGraphics(new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, recording);

            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        // ---- Contexts inside contexts, and planes behind the viewer ------------------------------------------------------

        [Fact]
        public async Task ANestedContextInsideAFlatPlane_IsResolvedOnItsOwn()
        {
            var inner = "<div style=\"position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d\">" +
                        Plane(Red, "transform:translateZ(50pt)") + Plane(Blue, "") + "</div>";

            var page = await Paint(Stage("transform-style:preserve-3d", $"<div style=\"position:absolute;left:0;top:0;width:200pt;height:160pt;transform:rotateY(10deg)\">{inner}</div>"));

            Assert.Equal(255, Pixel(page, 100, 80)[3]);
            Assert.True(Pixel(page, 100, 80)[0] > Pixel(page, 100, 80)[2], "red is in front of blue in the inner context");
        }

        [Fact]
        public async Task APlaneWhollyBehindTheViewer_IsNotDrawn_AndTheOthersAre()
        {
            var children = Plane(Red, "transform:translateZ(500pt)") + Plane(Blue, "");

            var page = await Paint(Stage("transform-style:preserve-3d", children, "perspective:300pt"));

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(page, 100, 80));
        }

        [Fact]
        public async Task AContextWithNothingVisible_PaintsNothing()
        {
            var page = await Paint(Stage("transform-style:preserve-3d", Plane(Red, "transform:rotateY(180deg);backface-visibility:hidden") +
                                                                  Plane(Blue, "transform:rotateY(180deg);backface-visibility:hidden")));

            for (var x = 0; x < page.Surface.Width; x++)
                Assert.Equal(0, Coverage(page, x));
        }
    }
}
