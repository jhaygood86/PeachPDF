using PeachDrawing;
using PeachDrawing.Core;
using PeachBrackets.Html.Paint;
using System.Numerics;

namespace PeachBrackets.Engine.Tests.Raster
{
    /// <summary>The geometry shared by the HTML painter and the SVG renderer for CSS 3D transforms: when a projective map is good enough to be a plain matrix, and in what order planes are drawn.</summary>
    public class ProjectiveRasterTests
    {
        private static Matrix4x4 Perspective(float distance)
        {
            var m = Matrix4x4.Identity;
            m.M34 = -1f / distance;
            return m;
        }

        private static ProjectivePlane<int>[] Planes(params (Matrix4x4 Matrix, Rect Source)[] specs)
        {
            var planes = new ProjectivePlane<int>[specs.Length];
            for (var i = 0; i < planes.Length; i++)
            {
                planes[i] = new ProjectivePlane<int> { Subject = i, Accumulated = specs[i].Matrix, Source = specs[i].Source, HasSource = true, Parent = -1 };
                ProjectiveRaster.Locate<int>(planes, i, specs[i].Source);
            }

            return planes;
        }

        // ---- Perspective ----

        [Fact]
        public void Perspective_DividesByOneMinusZOverDAboutTheVanishingPoint()
        {
            var m = ProjectiveRaster.Perspective(200, 50, 40);

            // A point of the plane z = 0 is unmoved wherever the vanishing point is: the matrix only acts through z.
            var home = Homography.FromMatrix4(m);
            var (x, y) = home.Apply(10, 70)!.Value;
            Assert.Equal(10, x, 4);
            Assert.Equal(70, y, 4);

            // Brought 100 nearer, a point one unit from the vanishing point is twice as far from it.
            var near = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 100) * m);
            var (nx, ny) = near.Apply(60, 40)!.Value;
            Assert.Equal(70, nx, 4);
            Assert.Equal(40, ny, 4);
        }

        // ---- A projective map that is nearly affine ----

        [Fact]
        public void TryFitAffine_AMapThatIsAlreadyAffine_IsItself()
        {
            var map = Homography.FromMatrix4(Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(5, 6, 0));

            Assert.True(ProjectiveRaster.TryFitAffine(map, new Rect(0, 0, 100, 50), 0.001, out var affine));
            Assert.Equal(MathF.Cos(0.3f), affine.M11, 4);
            Assert.Equal(5, affine.M31, 3);
        }

        [Fact]
        public void TryFitAffine_AFaintPerspective_IsCloseEnough_AStrongOneIsNot()
        {
            var tilted = Matrix4x4.CreateRotationY(0.02f);
            var faint = Homography.FromMatrix4(tilted * Perspective(50000));
            var strong = Homography.FromMatrix4(Matrix4x4.CreateRotationY(0.8f) * Perspective(300));
            var source = new Rect(0, 0, 100, 100);
            var tolerance = ProjectiveRaster.NearAffineTolerance(300, 1.0);

            Assert.False(faint.IsAffine); // the absolute test on the divisor would send it to a bitmap
            Assert.True(ProjectiveRaster.TryFitAffine(faint, source, tolerance, out _));
            Assert.False(ProjectiveRaster.TryFitAffine(strong, source, tolerance, out _));
        }

        [Fact]
        public void TryFitAffine_APlaneThatReachesBehindTheViewer_IsNeverAffine()
        {
            var map = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 400) * Perspective(300));

            Assert.False(ProjectiveRaster.TryFitAffine(map, new Rect(0, 0, 10, 10), 1000, out _));
        }

        [Fact]
        public void NearAffineTolerance_IsAQuarterOfADevicePixel_InTheUnitsOfTheCoordinates()
        {
            Assert.Equal(0.25 * 72 / 300, ProjectiveRaster.NearAffineTolerance(300, 1.0), 9);
            Assert.Equal(0.25 * 72 / 300 / 0.75, ProjectiveRaster.NearAffineTolerance(300, 0.75), 9);
        }

        // ---- Drawing planes back to front without a depth test ----

        [Fact]
        public void TryResolveOrder_TwoParallelPlanes_AreDrawnNearerLast()
        {
            var rect = new Rect(0, 0, 100, 100);
            var planes = Planes((Matrix4x4.CreateTranslation(0, 0, 50), rect), (Matrix4x4.Identity, rect));
            Span<int> order = [0, 1];

            Assert.True(ProjectiveRaster.TryResolveOrder<int>(planes, order));
            Assert.Equal([1, 0], order.ToArray()); // the plane at z = 50 is nearer, so it is drawn last
        }

        [Fact]
        public void TryResolveOrder_PlanesThatDoNotMeet_KeepTheirOrder()
        {
            var planes = Planes((Matrix4x4.Identity, new Rect(0, 0, 50, 50)), (Matrix4x4.CreateRotationY(0.5f), new Rect(100, 0, 50, 50)));
            Span<int> order = [0, 1];

            Assert.True(ProjectiveRaster.TryResolveOrder<int>(planes, order));
            Assert.Equal([0, 1], order.ToArray());
        }

        [Fact]
        public void TryResolveOrder_PlanesThatCrossEachOther_NeedTheDepthTest()
        {
            var rect = new Rect(-50, -50, 100, 100);
            var planes = Planes((Matrix4x4.CreateRotationY(0.7f), rect), (Matrix4x4.CreateRotationY(-0.7f), rect));
            Span<int> order = [0, 1];

            Assert.False(ProjectiveRaster.TryResolveOrder<int>(planes, order));
        }

        [Fact]
        public void TryResolveOrder_APerspectivePlane_NeedsTheBitmap()
        {
            var rect = new Rect(0, 0, 100, 100);
            var planes = Planes((Matrix4x4.CreateRotationY(0.7f) * Perspective(300), rect), (Matrix4x4.Identity, rect));
            Span<int> order = [0, 1];

            Assert.False(ProjectiveRaster.TryResolveOrder<int>(planes, order));
        }

        [Fact]
        public void TryResolveOrder_ATiltedPlaneBehindAnother_IsOrderedByTheOverlapNotByItsCentre()
        {
            // A card tilted about the horizontal axis through its middle: its centre is at z = 0, nearer than the flat plane at z = -1, but over the top
            // part the flat plane covers, the card is far behind it (z from -28 to -6). The order follows the overlap, which the centres get wrong.
            var tilted = Matrix4x4.CreateTranslation(0, -50, 0) * Matrix4x4.CreateRotationX(0.6f) * Matrix4x4.CreateTranslation(0, 50, 0);
            var planes = Planes((tilted, new Rect(0, 0, 100, 100)), (Matrix4x4.CreateTranslation(0, 0, -1), new Rect(0, 0, 100, 40)));
            Span<int> order = [1, 0]; // by centre depth: the flat plane first

            Assert.True(ProjectiveRaster.TryResolveOrder<int>(planes, order));
            Assert.Equal([0, 1], order.ToArray());
        }

        [Fact]
        public void TryResolveOrder_CoplanarPlanes_KeepTheirTreeOrder()
        {
            var rect = new Rect(0, 0, 100, 100);
            var planes = Planes((Matrix4x4.CreateRotationY(0.4f), rect), (Matrix4x4.CreateRotationY(0.4f), rect));
            Span<int> order = [0, 1];

            Assert.True(ProjectiveRaster.TryResolveOrder<int>(planes, order));
            Assert.Equal([0, 1], order.ToArray());
        }

        [Fact]
        public void TryResolveOrder_MoreThanItWillCompare_FallsBackToTheBitmap()
        {
            var rect = new Rect(0, 0, 10, 10);
            var specs = Enumerable.Range(0, 60).Select(i => (Matrix4x4.CreateTranslation(0, 0, i), rect)).ToArray();
            var planes = Planes(specs);
            var order = Enumerable.Range(0, 60).ToArray();

            Assert.False(ProjectiveRaster.TryResolveOrder<int>(planes, order));
        }

        [Fact]
        public void AffineOf_TakesTheDivisorOut()
        {
            var affine = ProjectiveRaster.AffineOf(Homography.FromMatrix4(Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(4, 5, 0) * new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 2)));

            Assert.Equal(1f, affine.M11, 5);
            Assert.Equal(1.5f, affine.M22, 5);
            Assert.Equal(2f, affine.M31, 5);
            Assert.Equal(2.5f, affine.M32, 5);
        }

        // ---- Where a plane lands ----

        [Fact]
        public void Project_ClassifiesVisibleBehindAndEdgeOn()
        {
            var rect = new Rect(0, 0, 100, 100);
            var visible = Planes((Matrix4x4.CreateRotationY(0.4f) * Perspective(500), rect))[0];
            var behind = Planes((Matrix4x4.CreateTranslation(0, 0, 900) * Perspective(500), rect))[0];
            var edgeOn = Planes((Matrix4x4.CreateRotationY(MathF.PI / 2) * Perspective(500), rect))[0];

            Assert.Equal(PlaneProjection.Visible, ProjectiveRaster.Project(visible, rect, 0.01, out var left, out _, out var right, out _));
            Assert.True(right > left);
            Assert.Equal(PlaneProjection.Behind, ProjectiveRaster.Project(behind, rect, 0.01, out _, out _, out _, out _));
            Assert.Equal(PlaneProjection.EdgeOn, ProjectiveRaster.Project(edgeOn, rect, 0.01, out _, out _, out _, out _));
        }

        [Fact]
        public void HasDepthToResolve_IsFalseOnlyForPlanesThatAreAllFlatAndAtTheSameDepth()
        {
            var rect = new Rect(0, 0, 10, 10);

            Assert.False(ProjectiveRaster.HasDepthToResolve<int>(Planes((Matrix4x4.Identity, rect), (Matrix4x4.CreateTranslation(5, 5, 0), rect))));
            Assert.True(ProjectiveRaster.HasDepthToResolve<int>(Planes((Matrix4x4.Identity, rect), (Matrix4x4.CreateTranslation(0, 0, 5), rect))));
            Assert.True(ProjectiveRaster.HasDepthToResolve<int>(Planes((Matrix4x4.Identity, rect), (Matrix4x4.CreateRotationY(0.2f), rect))));
        }

        [Fact]
        public void Locate_SortsAChildNeverBehindItsParent()
        {
            var rect = new Rect(0, 0, 10, 10);
            var planes = new ProjectivePlane<int>[2];
            planes[0] = new ProjectivePlane<int> { Accumulated = Matrix4x4.CreateTranslation(0, 0, 30), Parent = -1 };
            planes[1] = new ProjectivePlane<int> { Accumulated = Matrix4x4.Identity, Parent = 0 };

            ProjectiveRaster.Locate<int>(planes, 0, rect);
            ProjectiveRaster.Locate<int>(planes, 1, rect);

            Assert.Equal(30, planes[0].SortDepth, 6);
            Assert.Equal(30, planes[1].SortDepth, 6); // its own depth is 0, but it sits on a plane at 30
        }

        [Fact]
        public void FacesAway_TellsAMirrorInThePlaneFromTurningAround()
        {
            Assert.True(ProjectiveRaster.FacesAway(Matrix4x4.CreateRotationY(MathF.PI)));
            Assert.False(ProjectiveRaster.FacesAway(Matrix4x4.CreateScale(-1, 1, 1)));
            Assert.False(ProjectiveRaster.FacesAway(Matrix4x4.Identity));
        }

        [Fact]
        public void OrderBackToFront_ListsOnlyPlanesWithASource_ByDepthThenPosition()
        {
            var rect = new Rect(0, 0, 10, 10);
            var planes = Planes((Matrix4x4.CreateTranslation(0, 0, 9), rect), (Matrix4x4.CreateTranslation(0, 0, 3), rect), (Matrix4x4.Identity, rect), (Matrix4x4.CreateTranslation(0, 0, 3), rect));
            planes[2].HasSource = false;
            Span<int> order = stackalloc int[4];

            var count = ProjectiveRaster.OrderBackToFront<int>(planes, order);

            Assert.Equal(3, count);
            Assert.Equal([1, 3, 0], order[..count].ToArray());
        }
    }
}
