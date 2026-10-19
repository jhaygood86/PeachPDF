using PeachDrawing;
using PeachDrawing.Core;
using System;
using System.Numerics;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// One member of a 3D rendering context (CSS Transforms 2 section 4.1.2): a flat piece of artwork with the 4x4 matrix that carries it into the picture.
    /// <typeparamref name="T"/> is whatever the caller paints it from - a <c>BoxFragment</c> for HTML, an SVG element for SVG - so the geometry and the
    /// composition below are one implementation for both. A mutable struct kept in a reused list, so a context allocates nothing per plane.
    /// </summary>
    internal struct ProjectivePlane<T>
    {
        /// <summary>What the plane is painted from.</summary>
        public T Subject;

        /// <summary>The plane's whole transform in the coordinates the picture is composed in.</summary>
        public Matrix4x4 Accumulated;

        public Homography Map;
        public DepthPlane Depth;

        /// <summary>What the plane paints, in its own untransformed coordinates; only meaningful when <see cref="HasSource"/>.</summary>
        public Rect Source;

        /// <summary>Whether there is anything of this plane to draw: it is not turned away, has content and is at least partly in front of the viewer.</summary>
        public bool HasSource;

        /// <summary><c>backface-visibility: hidden</c> and turned away from the viewer.</summary>
        public bool Hidden;

        /// <summary>The index of the plane this one is a child of (-1 for the root).</summary>
        public int Parent;

        /// <summary>Where the plane sorts in depth: its centre's, but never behind its parent's, so a child is never painted before what it sits on.</summary>
        public double SortDepth;
    }

    /// <summary>Where a plane ended up once projected.</summary>
    internal enum PlaneProjection
    {
        /// <summary>At least partly in front of the viewer and thick enough to see.</summary>
        Visible,

        /// <summary>Wholly behind the viewer.</summary>
        Behind,

        /// <summary>Turned edge-on to the viewer: a hairline at most.</summary>
        EdgeOn,
    }

    /// <summary>Does something with one plane of a context; the plane is passed by reference because a span of planes cannot be captured.</summary>
    internal delegate void PlaneAction<T>(ref ProjectivePlane<T> plane, Canvas g);

    /// <summary>How a projective paint ended.</summary>
    internal enum ProjectiveOutcome
    {
        /// <summary>Drawn.</summary>
        Painted,

        /// <summary>Wholly behind the viewer, or edge-on: there is nothing to draw.</summary>
        NotRendered,

        /// <summary>Nothing of it lands inside the clip.</summary>
        ClippedOut,

        /// <summary>The canvas cannot rasterize (a measure-only pass): the caller falls back to what it did before.</summary>
        NoRaster,
    }

    /// <summary>
    /// The geometry and the bitmap recipe behind CSS <c>perspective</c> and 3D transforms (CSS Transforms 2), shared by the HTML painter
    /// (<see cref="FragmentPainter"/>) and the SVG renderer. Content whose transform is not affine once projected onto its plane cannot be a PDF
    /// <c>cm</c>: it is painted untransformed into a bitmap and warped through the projective map, and the planes of a <c>preserve-3d</c>
    /// context are depth-tested against each other.
    /// </summary>
    internal static class ProjectiveRaster
    {
        /// <summary>The most a bitmap is enlarged for a receding plane that comes closer than its flat size.</summary>
        public const double MaxMagnification = 4.0;

        /// <summary>Below this, a matrix entry that would tilt a plane out of the view plane, or move it in depth, counts as zero.</summary>
        public const double FlatTolerance = 1e-6;

        /// <summary>The perspective matrix of CSS Transforms 2 section 8: divides by <c>1 - z/d</c> about the vanishing point (<paramref name="originX"/>, <paramref name="originY"/>).</summary>
        public static Matrix4x4 Perspective(double distance, double originX, double originY)
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = (float)(-1.0 / distance);
            return Matrix4x4.CreateTranslation((float)-originX, (float)-originY, 0) * perspective * Matrix4x4.CreateTranslation((float)originX, (float)originY, 0);
        }

        /// <summary>
        /// Whether the plane's front now points away from the viewer: the sign of the z component of the transformed normal, which is the
        /// determinant of the upper-left 2x2 over that of the upper-left 3x3. <c>rotateY(180deg)</c> shows its back; <c>scaleX(-1)</c>, a
        /// mirror in the plane, does not.
        /// </summary>
        public static bool FacesAway(in Matrix4x4 m)
        {
            var det2 = (double)m.M11 * m.M22 - (double)m.M12 * m.M21;
            var det3 =
                (double)m.M11 * ((double)m.M22 * m.M33 - (double)m.M23 * m.M32) -
                (double)m.M12 * ((double)m.M21 * m.M33 - (double)m.M23 * m.M31) +
                (double)m.M13 * ((double)m.M21 * m.M32 - (double)m.M22 * m.M31);
            return det3 != 0 && det2 / det3 < 0;
        }

        /// <summary>
        /// Whether <paramref name="source"/>, mapped through <paramref name="map"/>, lands as a sliver thinner than <paramref name="minThickness"/> - a
        /// plane turned edge-on to the viewer, whose image a renderer would still stroke as a hairline. The thickness is the image's area over its
        /// longest extent (its diameter), measured on the part of it in front of the eye. An image wholly behind the viewer has no polygon and is not
        /// a sliver (the caller already drops it).
        /// </summary>
        public static bool ProjectsToSliver(in Homography map, Rect source, double minThickness)
        {
            var polygon = map.ProjectRectangle(source.Left, source.Top, source.Right, source.Bottom);
            if (polygon.Count < 3)
                return false;

            double twiceArea = 0, diameterSquared = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var (x, y) = polygon[i];
                var (nextX, nextY) = polygon[(i + 1) % polygon.Count];
                twiceArea += x * nextY - nextX * y;

                for (var j = i + 1; j < polygon.Count; j++)
                {
                    var (otherX, otherY) = polygon[j];
                    diameterSquared = Math.Max(diameterSquared, (x - otherX) * (x - otherX) + (y - otherY) * (y - otherY));
                }
            }

            return diameterSquared <= 0 || Math.Abs(twiceArea) / 2 / Math.Sqrt(diameterSquared) < minThickness;
        }

        /// <summary>How much larger than its flat size the warp draws <paramref name="source"/> at its most magnified corner or its centre, at least 1.</summary>
        public static double Magnification(Homography map, Rect source)
        {
            double Scale(double x, double y)
            {
                const double step = 1.0;
                if (map.Apply(x, y) is not { } origin || map.Apply(x + step, y) is not { } right || map.Apply(x, y + step) is not { } below)
                    return 1.0;

                var sx = Math.Sqrt((right.X - origin.X) * (right.X - origin.X) + (right.Y - origin.Y) * (right.Y - origin.Y)) / step;
                var sy = Math.Sqrt((below.X - origin.X) * (below.X - origin.X) + (below.Y - origin.Y) * (below.Y - origin.Y)) / step;
                return Math.Max(sx, sy);
            }

            var most = Math.Max(
                Math.Max(Scale(source.Left, source.Top), Scale(source.Right, source.Top)),
                Math.Max(Math.Max(Scale(source.Left, source.Bottom), Scale(source.Right, source.Bottom)), Scale(source.Left + source.Width / 2, source.Top + source.Height / 2)));
            return Math.Clamp(most, 1.0, MaxMagnification);
        }

        /// <summary>The affine map that agrees with <paramref name="map"/> at the centre of <paramref name="box"/> in value and in slope.</summary>
        public static Matrix3x2 Linearise(Homography map, Rect box)
        {
            var cx = box.X + box.Width / 2;
            var cy = box.Y + box.Height / 2;
            var step = Math.Max(1.0, Math.Min(box.Width, box.Height) / 4);

            if (map.Apply(cx, cy) is not { } centre || map.Apply(cx + step, cy) is not { } right || map.Apply(cx, cy + step) is not { } below)
                return Matrix3x2.Identity;

            var m11 = (right.X - centre.X) / step;
            var m12 = (right.Y - centre.Y) / step;
            var m21 = (below.X - centre.X) / step;
            var m22 = (below.Y - centre.Y) / step;
            return new Matrix3x2((float)m11, (float)m12, (float)m21, (float)m22, (float)(centre.X - (cx * m11 + cy * m21)), (float)(centre.Y - (cx * m12 + cy * m22)));
        }

        /// <summary>
        /// How far an affine stand-in for a projective map may stray before it is not good enough: a quarter of a device pixel at the rasterization
        /// resolution, in the units of the coordinates it is measured in (<paramref name="pointsPerUnit"/> points to a unit).
        /// </summary>
        public static double NearAffineTolerance(double rasterizationDpi, double pointsPerUnit) => 0.25 * 72.0 / rasterizationDpi / Math.Max(pointsPerUnit, 1e-9);

        /// <summary>
        /// Whether a projective <paramref name="map"/> is, over <paramref name="source"/>, within <paramref name="tolerance"/> of an affine one - a weak perspective
        /// on a small plane, say - which a PDF <c>cm</c> carries exactly as far as anyone can see, with all of the artwork kept as vector content instead of a bitmap.
        /// The stand-in is the tangent at the centre; it is judged at the four corners and the centre, where it strays most.
        /// </summary>
        public static bool TryFitAffine(in Homography map, Rect source, double tolerance, out Matrix3x2 affine)
        {
            affine = Linearise(map, source);

            var cx = source.X + source.Width / 2;
            var cy = source.Y + source.Height / 2;
            ReadOnlySpan<(double X, double Y)> probes = [(source.Left, source.Top), (source.Right, source.Top), (source.Left, source.Bottom), (source.Right, source.Bottom), (cx, cy)];
            foreach (var (x, y) in probes)
            {
                if (map.Apply(x, y) is not { } actual)
                    return false; // part of it is behind the viewer

                var ax = x * affine.M11 + y * affine.M21 + affine.M31;
                var ay = x * affine.M12 + y * affine.M22 + affine.M32;
                if (Math.Abs(actual.X - ax) > tolerance || Math.Abs(actual.Y - ay) > tolerance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a depth test could change anything: false only when every plane faces the viewer and stays parallel to the view plane, at
        /// the same depth as all the others, with no perspective divisor - then painting them in tree order is already right.
        /// </summary>
        public static bool HasDepthToResolve<T>(ReadOnlySpan<ProjectivePlane<T>> planes)
        {
            foreach (ref readonly var plane in planes)
            {
                ref readonly var m = ref plane.Accumulated;
                if (plane.Hidden ||
                    Math.Abs(m.M13) > FlatTolerance || Math.Abs(m.M23) > FlatTolerance ||
                    Math.Abs(m.M14) > FlatTolerance || Math.Abs(m.M24) > FlatTolerance ||
                    Math.Abs(m.M43) > FlatTolerance || Math.Abs(m.M44 - 1) > FlatTolerance)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Fills in plane <paramref name="index"/>'s map and depth from its accumulated matrix, and where it sorts: its centre's depth (<paramref name="centre"/> is the
        /// plane's border box), and never behind its parent's, so a child is drawn after what it sits on however the context is tilted. A centre behind the
        /// viewer has no depth: such a plane is drawn first, whatever part of it is in front. Parents come before their children in <paramref name="planes"/>.
        /// </summary>
        public static void Locate<T>(Span<ProjectivePlane<T>> planes, int index, Rect centre)
        {
            ref var plane = ref planes[index];
            plane.Map = Homography.FromMatrix4(plane.Accumulated);
            plane.Depth = DepthPlane.FromMatrix4(plane.Accumulated);

            var depth = plane.Depth.At(centre.X + centre.Width / 2, centre.Y + centre.Height / 2);
            plane.SortDepth = double.IsNaN(depth) ? double.NegativeInfinity : depth;
            if (plane.Parent >= 0)
                plane.SortDepth = Math.Max(plane.SortDepth, planes[plane.Parent].SortDepth);
        }

        /// <summary>Where <paramref name="source"/> lands once <paramref name="plane"/> is projected: its bounds when it is <see cref="PlaneProjection.Visible"/>.</summary>
        public static PlaneProjection Project<T>(in ProjectivePlane<T> plane, Rect source, double minThickness, out double left, out double top, out double right, out double bottom)
        {
            if (!plane.Map.TryProjectRectangleBounds(source.Left, source.Top, source.Right, source.Bottom, out left, out top, out right, out bottom))
                return PlaneProjection.Behind;

            return ProjectsToSliver(plane.Map, source, minThickness) ? PlaneProjection.EdgeOn : PlaneProjection.Visible;
        }

        /// <summary>
        /// Paints one piece of artwork through <paramref name="map"/>: <paramref name="paintSource"/> draws it untransformed into a bitmap of
        /// <paramref name="source"/> (in the coordinates of <paramref name="g"/>), which is warped into a bitmap of what lands in the clip and drawn.
        /// </summary>
        /// <param name="g">the canvas to draw on</param>
        /// <param name="map">source coordinates to the coordinates of <paramref name="g"/></param>
        /// <param name="source">what the artwork covers, with any bleed (blur, shadow) it paints beyond its box</param>
        /// <param name="minThickness">the thinnest image still worth a bitmap (<see cref="ProjectsToSliver"/>)</param>
        /// <param name="paintSource">draws the artwork into the canvas it is given, in the coordinates of <paramref name="g"/></param>
        /// <param name="openArtifact">opens the scope the final bitmap is drawn in (a tagged-PDF artifact), or null for none</param>
        /// <param name="clip">the part of the picture that can be seen, in the coordinates of <paramref name="g"/>; null for the clip <paramref name="g"/> reports</param>
        public static ProjectiveOutcome Paint(Canvas g, in Homography map, Rect source, double minThickness, Action<Canvas> paintSource, Func<IDisposable?>? openArtifact = null, Rect? clip = null)
        {
            if (!map.TryProjectRectangleBounds(source.Left, source.Top, source.Right, source.Bottom, out var minX, out var minY, out var maxX, out var maxY))
                return ProjectiveOutcome.NotRendered; // wholly behind the viewer

            // Edge-on: nothing worth a bitmap, and no text layer to supply over one that is not there.
            if (ProjectsToSliver(map, source, minThickness))
                return ProjectiveOutcome.NotRendered;

            var destination = Intersect(new Rect(minX, minY, maxX - minX, maxY - minY), clip ?? g.GetClip());
            if (destination.Width <= 0 || destination.Height <= 0)
                return ProjectiveOutcome.ClippedOut;

            // A plane brought closer than its flat size is drawn larger than its bitmap: render the source with as many more pixels as the
            // warp enlarges it by, at most a few times over, so it does not blur.
            var dpi = g.RasterizationDpi * Magnification(map, source);

            using var sourceScope = g.BeginRasterSurface(source, dpi);
            if (sourceScope is null)
                return ProjectiveOutcome.NoRaster;

            using var destinationScope = g.BeginRasterSurface(destination);
            if (destinationScope is null)
                return ProjectiveOutcome.NoRaster;

            paintSource(sourceScope.Graphics);

            Warp.Apply(sourceScope.Surface, destinationScope.Surface, map);

            using (openArtifact?.Invoke())
                g.DrawRaster(destinationScope.Surface);

            return ProjectiveOutcome.Painted;
        }

        /// <summary>The planes in <paramref name="planes"/> with a source (<see cref="ProjectivePlane{T}.HasSource"/>), ordered back to front by their sort depth, then by position in the list.</summary>
        public static int OrderBackToFront<T>(ReadOnlySpan<ProjectivePlane<T>> planes, Span<int> order)
        {
            var count = 0;
            for (var i = 0; i < planes.Length; i++)
            {
                if (planes[i].HasSource)
                    order[count++] = i;
            }

            // Insertion sort: a context holds a handful of planes, and the order is stable for equal depths (a parent under its coplanar children).
            for (var i = 1; i < count; i++)
            {
                var item = order[i];
                var depth = planes[item].SortDepth;
                var j = i - 1;
                while (j >= 0 && planes[order[j]].SortDepth > depth)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = item;
            }

            return count;
        }

        /// <summary>The most planes <see cref="TryResolveOrder{T}"/> will try to order by pairs: it compares every pair.</summary>
        private const int MaxOrderedPlanes = 48;

        /// <summary>
        /// Whether the planes of a context can be drawn one over the other, as vector artwork, instead of through a depth-tested bitmap: every plane stays affine
        /// (a tilt with no perspective), and where two of them overlap one is in front of the other over the whole overlap - none pierces another, and they
        /// do not take turns in front in a cycle. <paramref name="order"/> is then rewritten back to front.
        /// </summary>
        /// <param name="planes">the planes of the context, mapped and sourced</param>
        /// <param name="order">the indexes of the planes to draw, ordered by their centres' depth; reordered when this returns true</param>
        public static bool TryResolveOrder<T>(ReadOnlySpan<ProjectivePlane<T>> planes, Span<int> order)
        {
            var count = order.Length;
            if (count > MaxOrderedPlanes)
                return false;

            Span<Homography> inverses = stackalloc Homography[MaxOrderedPlanes];
            Span<(double Left, double Top, double Right, double Bottom)> bounds = stackalloc (double, double, double, double)[MaxOrderedPlanes];

            for (var k = 0; k < count; k++)
            {
                ref readonly var plane = ref planes[order[k]];

                // An affine map with a constant divisor: anything else is a real perspective, which a cm cannot carry.
                if (!plane.Map.IsAffine || !(plane.Map.M33 > 1e-9) || plane.Map.Invert() is not { } inverse ||
                    !plane.Map.TryProjectRectangleBounds(plane.Source.Left, plane.Source.Top, plane.Source.Right, plane.Source.Bottom, out var left, out var top, out var right, out var bottom))
                    return false;

                inverses[k] = inverse;
                bounds[k] = (left, top, right, bottom);
            }

            // before[j] has bit i set when plane i must be drawn before plane j (i is behind j over their overlap).
            Span<ulong> behind = stackalloc ulong[MaxOrderedPlanes];
            behind[..count].Clear();

            for (var a = 0; a < count; a++)
            {
                for (var b = a + 1; b < count; b++)
                {
                    var left = Math.Max(bounds[a].Left, bounds[b].Left);
                    var top = Math.Max(bounds[a].Top, bounds[b].Top);
                    var right = Math.Min(bounds[a].Right, bounds[b].Right);
                    var bottom = Math.Min(bounds[a].Bottom, bounds[b].Bottom);
                    if (right <= left || bottom <= top)
                        continue; // they do not meet

                    var sign = 0;
                    ReadOnlySpan<(double X, double Y)> corners = [(left, top), (right, top), (left, bottom), (right, bottom)];
                    foreach (var (x, y) in corners)
                    {
                        var difference = DepthAt(planes[order[a]], inverses[a], x, y) - DepthAt(planes[order[b]], inverses[b], x, y);
                        if (double.IsNaN(difference))
                            return false;

                        if (Math.Abs(difference) <= FlatTolerance)
                            continue;

                        var current = difference > 0 ? 1 : -1;
                        if (sign != 0 && sign != current)
                            return false; // they cross: one pierces the other
                        sign = current;
                    }

                    if (sign > 0)
                        behind[a] |= 1UL << b; // a is nearer: b is drawn first
                    else if (sign < 0)
                        behind[b] |= 1UL << a;
                    else if (planes[order[a]].SortDepth > planes[order[b]].SortDepth)
                        behind[a] |= 1UL << b;  // coplanar: the existing order (parent under child, then tree order) stands
                    else
                        behind[b] |= 1UL << a;
                }
            }

            // Back to front: a plane is drawn once everything that must be under it is. Ties keep the order the planes came in.
            Span<int> sorted = stackalloc int[MaxOrderedPlanes];
            ulong drawn = 0;
            for (var placed = 0; placed < count; placed++)
            {
                var next = -1;
                for (var k = 0; k < count && next < 0; k++)
                {
                    if ((drawn & (1UL << k)) == 0 && (behind[k] & ~drawn) == 0)
                        next = k;
                }

                if (next < 0)
                    return false; // a cycle: each is in front of another

                sorted[placed] = order[next];
                drawn |= 1UL << next;
            }

            sorted[..count].CopyTo(order);
            return true;
        }

        private static double DepthAt<T>(in ProjectivePlane<T> plane, in Homography inverse, double x, double y) =>
            inverse.Apply(x, y) is { } source ? plane.Depth.At(source.X, source.Y) : double.NaN;

        /// <summary>The plain matrix an affine <paramref name="map"/> amounts to (its divisor taken out), for drawing a plane as vector artwork.</summary>
        public static Matrix3x2 AffineOf(in Homography map) =>
            new((float)(map.M11 / map.M33), (float)(map.M21 / map.M33), (float)(map.M12 / map.M33), (float)(map.M22 / map.M33), (float)(map.M13 / map.M33), (float)(map.M23 / map.M33));

        /// <summary>
        /// Draws the ordered planes of a context into one shared bitmap through a per-pixel depth test, so planes that intersect are resolved exactly
        /// where they cross, and then draws that bitmap once.
        /// </summary>
        /// <param name="g">the canvas to draw on</param>
        /// <param name="planes">the planes of the context</param>
        /// <param name="order">the indexes of the planes to draw, back to front (<see cref="OrderBackToFront"/>)</param>
        /// <param name="destination">the part of the picture that is drawn, already limited to the clip</param>
        /// <param name="paintPlane">paints a plane untransformed into the canvas it is given</param>
        /// <param name="planeFailed">told which plane could not be drawn (no raster surface for it)</param>
        /// <param name="openArtifact">opens the scope the final bitmap is drawn in, or null for none</param>
        /// <returns>false, having painted nothing, when <paramref name="g"/> cannot rasterize.</returns>
        public static bool Compose<T>(Canvas g, Span<ProjectivePlane<T>> planes, ReadOnlySpan<int> order, Rect destination, PlaneAction<T> paintPlane, PlaneAction<T> planeFailed, Func<IDisposable?>? openArtifact)
        {
            using var destinationScope = g.BeginRasterSurface(destination);
            if (destinationScope is null)
                return false;

            using var depthBuffer = new DepthBuffer(destinationScope.Surface.Width, destinationScope.Surface.Height);

            foreach (var index in order)
            {
                ref var plane = ref planes[index];

                // A plane brought closer than its flat size is drawn larger than its bitmap: give it as many more pixels as the warp
                // enlarges it by, so it does not blur.
                var dpi = g.RasterizationDpi * Magnification(plane.Map, plane.Source);
                using var sourceScope = g.BeginRasterSurface(plane.Source, dpi);
                if (sourceScope is null)
                {
                    plane.HasSource = false;
                    planeFailed(ref plane, g);
                    continue;
                }

                paintPlane(ref plane, sourceScope.Graphics);

                Warp.Composite(sourceScope.Surface, destinationScope.Surface, plane.Map, plane.Depth, depthBuffer);
            }

            using (openArtifact?.Invoke())
                g.DrawRaster(destinationScope.Surface);

            return true;
        }

        /// <summary>The overlap of two rectangles; empty (zero size) when they do not meet.</summary>
        public static Rect Intersect(Rect a, Rect b)
        {
            var left = Math.Max(a.Left, b.Left);
            var top = Math.Max(a.Top, b.Top);
            var right = Math.Min(a.Right, b.Right);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty;
        }
    }
}
