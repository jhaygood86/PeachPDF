using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>CSS <c>perspective</c> and 3D transforms: the homography and warp behind them, and how the painter and the PDF pipeline use them.</summary>
    public class ProjectiveTransformTests
    {
        // ---- Homography ----------------------------------------------------------------------------------

        [Fact]
        public void FromMatrix4_OfAPerspectiveMatrix_DividesByOneMinusZOverD()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 200f;
            // A point pushed towards the viewer (z = 100) by translateZ, then viewed: it looks 2x larger.
            var m = Matrix4x4.CreateTranslation(0, 0, 100) * perspective;

            var h = Homography.FromMatrix4(m);

            var (x, y) = h.Apply(50, 20)!.Value;
            Assert.Equal(100, x, 4);
            Assert.Equal(40, y, 4);
            // A plane at a constant depth is scaled uniformly: affine, though a perspective produced it.
            Assert.True(h.IsAffine);
            Assert.False(Homography.FromMatrix4(Matrix4x4.CreateRotationY(0.5f) * perspective).IsAffine);
        }

        [Fact]
        public void AnAffineMatrix4_GivesAnAffineHomography()
        {
            var h = Homography.FromMatrix4(Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(5, 6, 7));

            Assert.True(h.IsAffine);
        }

        [Fact]
        public void Invert_RoundTripsAPoint()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 300f;
            var h = Homography.FromMatrix4(Matrix4x4.CreateRotationY(0.5f) * perspective);

            var inverse = h.Invert();
            Assert.NotNull(inverse);

            var image = h.Apply(30, 40)!.Value;
            var back = inverse.Value.Apply(image.X, image.Y)!.Value;
            Assert.Equal(30, back.X, 4);
            Assert.Equal(40, back.Y, 4);
        }

        [Fact]
        public void Then_AppliesTheFirstMapFirst()
        {
            var combined = Homography.Then(Homography.Translation(10, 0), Homography.FromMatrix4(Matrix4x4.CreateScale(2, 3, 1)));

            var (x, y) = combined.Apply(1, 1)!.Value;

            Assert.Equal(22, x, 6);
            Assert.Equal(3, y, 6);
        }

        [Fact]
        public void ASingularMap_HasNoInverse()
        {
            Assert.Null(new Homography(1, 2, 0, 2, 4, 0, 0, 0, 1).Invert());
        }

        [Fact]
        public void Apply_BehindTheViewer_IsNull()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 100f;
            var h = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 250) * perspective);

            Assert.Null(h.Apply(1, 1));
        }

        [Fact]
        public void ProjectRectangle_CutsWhatPassesBehindTheEye()
        {
            // rotateY takes the right half of the rectangle towards the viewer past the eye at d = 100.
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 100f;
            var h = Homography.FromMatrix4(Matrix4x4.CreateTranslation(-100, 0, 0) * Matrix4x4.CreateRotationY(-1.2f) * perspective);

            var polygon = h.ProjectRectangle(0, 0, 200, 100);

            Assert.NotEmpty(polygon);
            Assert.All(polygon, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) < 1e6));
        }

        [Fact]
        public void ProjectRectangle_WhollyBehindTheViewer_IsEmpty()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 100f;
            var h = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 500) * perspective);

            Assert.Empty(h.ProjectRectangle(0, 0, 50, 50));
        }

        [Fact]
        public void Negated_IsTheSameMapWithTheOppositeDivisorSign()
        {
            var h = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 50));
            var (x, y, w) = h.Negated().ApplyHomogeneous(3, 4);

            Assert.Equal(-1, w, 6);
            Assert.Equal(h.ApplyHomogeneous(3, 4).X / h.ApplyHomogeneous(3, 4).W, x / w, 6);
            Assert.Equal(4, y / w, 6);
        }

        // ---- Warp ----------------------------------------------------------------------------------------

        private static RasterSurface Solid(int w, int h, byte r, byte g, byte b)
        {
            var s = new RasterSurface(w, h, 0, 0, 1, 1);
            var p = s.Pixels;
            for (var i = 0; i < p.Length; i += 4)
            {
                p[i] = r;
                p[i + 1] = g;
                p[i + 2] = b;
                p[i + 3] = 255;
            }

            return s;
        }

        private static byte[] Pixel(RasterSurface s, int x, int y) => s.Pixels.Slice((y * s.Width + x) * 4, 4).ToArray();

        [Fact]
        public void Warp_WithTheIdentity_CopiesThePicture()
        {
            var source = Solid(8, 8, 200, 100, 50);
            var destination = new RasterSurface(8, 8, 0, 0, 1, 1);

            Warp.Apply(source, destination, Homography.Identity);

            Assert.Equal(new byte[] { 200, 100, 50, 255 }, Pixel(destination, 3, 3));
            Assert.Equal(new byte[] { 200, 100, 50, 255 }, Pixel(destination, 0, 7));
        }

        [Fact]
        public void Warp_WithATranslation_ShiftsThePicture()
        {
            var source = Solid(4, 4, 255, 0, 0);
            var destination = new RasterSurface(12, 12, 0, 0, 1, 1);

            Warp.Apply(source, destination, Homography.Translation(5, 6));

            Assert.Equal(255, Pixel(destination, 6, 7)[3]);
            Assert.Equal(0, Pixel(destination, 2, 2)[3]);
            Assert.Equal(0, Pixel(destination, 11, 11)[3]);
        }

        [Fact]
        public void Warp_WithPerspective_MakesAFarEdgeShorterThanANearOne()
        {
            var source = Solid(40, 40, 0, 200, 0);
            var destination = new RasterSurface(80, 80, 0, 0, 1, 1);
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 60f;
            // Rotate about the vertical axis through the centre: the right edge goes away, the left comes nearer.
            var local = Matrix4x4.CreateTranslation(-20, -20, 0) * Matrix4x4.CreateRotationY(0.6f) * perspective * Matrix4x4.CreateTranslation(40, 40, 0);

            Warp.Apply(source, destination, Homography.FromMatrix4(local));

            int Height(int x)
            {
                var n = 0;
                for (var y = 0; y < 80; y++)
                    if (Pixel(destination, x, y)[3] > 127)
                        n++;
                return n;
            }

            var columns = Enumerable.Range(0, 80).Where(x => Height(x) > 0).ToList();
            Assert.NotEmpty(columns);
            Assert.True(Height(columns.First() + 2) > Height(columns.Last() - 2) + 4, "the near edge should be taller than the far edge");
        }

        [Fact]
        public void Warp_LeavesWhatIsBehindTheViewerTransparent()
        {
            var source = Solid(20, 20, 255, 255, 255);
            var destination = new RasterSurface(20, 20, 0, 0, 1, 1);
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 10f;

            Warp.Apply(source, destination, Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 30) * perspective));

            Assert.All(destination.Pixels.ToArray(), b => Assert.Equal(0, b));
        }

        [Fact]
        public void Warp_OfASingularMap_DoesNothing()
        {
            var destination = new RasterSurface(4, 4, 0, 0, 1, 1);

            Warp.Apply(Solid(4, 4, 1, 1, 1), destination, new Homography(1, 2, 0, 2, 4, 0, 0, 0, 1));

            Assert.All(destination.Pixels.ToArray(), b => Assert.Equal(0, b));
        }

        [Fact]
        public void Warp_ShrinkingASharpPattern_AveragesInsteadOfAliasing()
        {
            var source = new RasterSurface(64, 64, 0, 0, 1, 1);
            var p = source.Pixels;
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 64; x++)
                {
                    var v = (byte)((x + y) % 2 == 0 ? 255 : 0);
                    var o = (y * 64 + x) * 4;
                    p[o] = p[o + 1] = p[o + 2] = p[o + 3] = v == 255 ? (byte)255 : (byte)255;
                    if (v == 0)
                        p[o] = p[o + 1] = p[o + 2] = 0;
                }

            var destination = new RasterSurface(16, 16, 0, 0, 1, 1);
            Warp.Apply(source, destination, Homography.FromMatrix4(Matrix4x4.CreateScale(0.25f, 0.25f, 1)));

            // A checkerboard shrunk four times averages to mid grey, not to whichever colour the sample landed on.
            Assert.InRange((int)Pixel(destination, 8, 8)[0], 100, 156);
        }

        // ---- painter -------------------------------------------------------------------------------------

        private static async Task<RasterGraphics> Paint(string body, int width = 220, int height = 180)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);
            var page = new RasterGraphics(new PdfSharpAdapter(), new RasterSurface(width, height, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, page);
            return page;
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static string Scene(string cardStyle, string sceneStyle = "perspective:200pt") => $$"""
            <div style="position:relative;margin:0;width:200pt;height:160pt;{{sceneStyle}}">
              <div style="position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#00aa00;{{cardStyle}}"></div>
            </div>
            """;

        private static int Coverage(RasterGraphics g, int x)
        {
            var n = 0;
            for (var y = 0; y < g.Surface.Height; y++)
                if (Pixel(g, x, y)[3] > 127)
                    n++;
            return n;
        }

        [Fact]
        public async Task ParentPerspective_ForeshortensARotatedChild()
        {
            var page = await Paint(Scene("transform:rotateY(60deg)"));

            // The centre is still the card; its original left edge is empty now (it narrowed); the two sides differ in height.
            Assert.Equal(new byte[] { 0, 170, 0, 255 }, Pixel(page, 100, 80));
            Assert.Equal(0, Pixel(page, 53, 80)[3]);

            var columns = Enumerable.Range(0, page.Surface.Width).Where(x => Coverage(page, x) > 0).ToList();
            Assert.NotEmpty(columns);
            var near = Coverage(page, columns.First() + 2);
            var far = Coverage(page, columns.Last() - 2);
            Assert.NotEqual(near, far);
        }

        [Fact]
        public async Task RotationAlone_IsAffine_SoTheEdgesStayParallel()
        {
            var page = await Paint(Scene("transform:rotateY(60deg)", sceneStyle: ""));

            var columns = Enumerable.Range(0, page.Surface.Width).Where(x => Coverage(page, x) > 0).ToList();
            var near = Coverage(page, columns.First() + 2);
            var far = Coverage(page, columns.Last() - 2);
            Assert.InRange(Math.Abs(near - far), 0, 2);
        }

        [Fact]
        public async Task ThePerspectiveFunction_NeedsNoParent()
        {
            var page = await Paint(Scene("transform:perspective(200pt) rotateY(60deg)", sceneStyle: ""));

            var columns = Enumerable.Range(0, page.Surface.Width).Where(x => Coverage(page, x) > 0).ToList();
            Assert.NotEqual(Coverage(page, columns.First() + 2), Coverage(page, columns.Last() - 2));
        }

        [Fact]
        public async Task ANonAffinePlaneWhollyBehindTheEye_IsNotPainted()
        {
            // The plane sits at z of about 103 to 113 under a 100pt perspective: past the viewer. It is not affine (rotateX), so it takes the
            // projective path, where a negated map used to reflect it through the origin and cover the page.
            var page = await Paint(Scene("transform:rotateX(20deg) translateZ(110pt)", sceneStyle: "perspective:100pt"));

            for (var x = 0; x < page.Surface.Width; x++)
                Assert.Equal(0, Coverage(page, x));
        }

        [Fact]
        public async Task TranslateZ_UnderPerspective_EnlargesTheChild()
        {
            var flat = await Paint(Scene(""));
            var near = await Paint(Scene("transform:translateZ(100pt)"));

            Assert.True(Coverage(near, 100) > Coverage(flat, 100) + 20, "a plane brought closer looks larger");
        }

        [Fact]
        public async Task BackfaceVisibilityHidden_HidesATurnedAwayChild_ButNotAMirroredOne()
        {
            var hidden = await Paint(Scene("transform:rotateY(180deg);backface-visibility:hidden"));
            var visible = await Paint(Scene("transform:rotateY(180deg)"));
            var mirrored = await Paint(Scene("transform:scaleX(-1);backface-visibility:hidden"));

            Assert.Equal(0, Coverage(hidden, 100));
            Assert.True(Coverage(visible, 100) > 50);
            Assert.True(Coverage(mirrored, 100) > 50, "scaleX(-1) mirrors in the plane and still faces the viewer");
        }

        [Fact]
        public async Task PerspectiveOrigin_MovesTheVanishingPoint()
        {
            var centred = await Paint(Scene("transform:translateZ(60pt)", "perspective:200pt;perspective-origin:50% 50%"));
            var corner = await Paint(Scene("transform:translateZ(60pt)", "perspective:200pt;perspective-origin:0% 0%"));

            // Looking at the plane from its top-left corner, a nearer plane grows away from that corner: the pictures differ.
            Assert.NotEqual(Pixel(centred, 155, 40), Pixel(corner, 155, 40));
        }

        [Fact]
        public async Task ANearerPlaneBehindTheEye_IsNotDrawn_AndNothingThrows()
        {
            var page = await Paint(Scene("transform:translateZ(400pt)"));

            Assert.Equal(0, Coverage(page, 100));
        }

        [Fact]
        public async Task TheChildsOwnOpacity_StillApplies()
        {
            var page = await Paint(Scene("transform:rotateY(40deg);opacity:0.5"));

            var p = Pixel(page, 100, 80);
            Assert.InRange((int)p[3], 120, 135);
        }

        [Fact]
        public async Task WithoutRasterSupport_TheElementFallsBackToItsAffineTransform()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(Scene("transform:rotateY(60deg)")), margin: 0);
            var recording = new RecordingGraphics(new PdfSharpAdapter());

            FragmentPaintHarness.PaintPage(container, recording);

            Assert.Contains(recording.Log, op => op.Kind == PaintOpKind.FillRect);
        }

        [Theory]
        [InlineData("perspective", "400px", true)]
        [InlineData("perspective", "none", true)]
        [InlineData("perspective-origin", "left top", true)]
        [InlineData("perspective-origin", "10px 20%", true)]
        [InlineData("backface-visibility", "hidden", true)]
        [InlineData("backface-visibility", "sideways", false)]
        public void TheProperties_AreRecognised(string property, string value, bool valid)
        {
            Assert.Equal(valid, PeachPDF.Html.Core.Utils.CssPropertyRegistry.SupportsDeclaration(property, value));
        }

        // ---- through the PDF pipeline ---------------------------------------------------------------------

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
        public async Task ThePerspectiveWarp_IsOneBitmap_AndKeepsItsTextSelectable()
        {
            var body = """
                <div style="perspective:200pt;width:200pt;height:150pt"><div style="width:100pt;height:80pt;background:#0a0;transform:rotateY(50deg)">Findable</div></div>
                """;

            var pdf = await PdfObjectReader.GeneratePdf($"<html><body style=\"margin:0\">{body}</body></html>", Config());

            Assert.Equal(1, Placements(pdf));
            Assert.Contains("3 Tr", pdf);
        }

        [Fact]
        public async Task AnAffine3dRotation_StaysVector()
        {
            var body = "<div style=\"width:100pt;height:80pt;background:#0a0;transform:rotateY(50deg)\">Text</div>";

            var pdf = await PdfObjectReader.GeneratePdf($"<html><body style=\"margin:0\">{body}</body></html>", Config());

            Assert.Equal(0, Placements(pdf));
        }
    }
}
