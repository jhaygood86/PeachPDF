using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Raster;
using System;
using System.Numerics;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// CSS <c>perspective</c> and 3D transforms (CSS Transforms 2): an element whose <c>transform</c> is not affine once projected onto its own
    /// plane, which a PDF <c>cm</c> cannot express. It is painted untransformed into a bitmap and warped through the projective map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What is projective.</b> The transform's 4x4 restricted to the element's plane z = 0 is a 3x3 homography whose third row is zero
    /// exactly when the element stays a parallelogram. <c>rotateX/Y()</c> and <c>translateZ()</c> keep it affine on their own (the plane is
    /// tilted, not viewed in perspective); <c>perspective()</c> in the transform list, or a <c>perspective</c> on the parent that the
    /// element's own 3D transform then acts through, do not. Affine results keep the vector path.
    /// </para>
    /// <para>
    /// <b>The parent's perspective</b> is the matrix that divides by <c>1 - z/d</c> about the <c>perspective-origin</c>. It applies to the
    /// element's own transform (after it), in the parent's coordinate space, so it is composed in the element's coordinates using where the
    /// parent's box sits relative to the element's.
    /// </para>
    /// <para>
    /// <b>Planes that share a 3D space</b> (<c>transform-style: preserve-3d</c>) are not painted here: <see cref="TryPaintContext3D"/> composes the
    /// whole context, with each plane's transform accumulated down from the context's root, and leaves a context that has no depth to
    /// resolve to the per-element paint.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>
        /// The transform found for a box that the vector path cannot carry on its own. <paramref name="Map"/> is the map in page coordinates;
        /// <paramref name="Hidden"/> when nothing is to be painted (backface-visibility: hidden and turned away, or wholly behind the viewer);
        /// <paramref name="Affine"/> when the map turned out to be affine after the parent's perspective was applied (a plane parallel to the
        /// view plane, brought nearer or further, is just scaled), which a PDF <c>cm</c> can then carry.
        /// </summary>
        private readonly record struct ProjectiveWarp(Homography Map, bool Hidden, RMatrix? Affine = null);

        /// <summary>The most a bitmap is enlarged for a receding plane that comes closer than its flat size.</summary>
        private const double MaxProjectiveMagnification = 4.0;

        /// <summary>The homography this box is painted through, or null when its transform is affine (or there is none) and the vector path applies.</summary>
        private ProjectiveWarp? ResolveProjective(BoxFragment fragment)
        {
            var box = fragment.Box;
            if (box.ActualTransform4 is not { } local)
                return null;

            var perspective = Matrix4x4.Identity;
            var hasPerspective = false;
            var position = (X: fragment.WholeBoxRect.X, Y: fragment.WholeBoxRect.Y);

            if (box.ParentBox is { ActualPerspective: > 0 } parent && _pageRoot is not null &&
                FindFragment(_pageRoot, parent) is { } parentFragment)
            {
                // The vanishing point in the element's own coordinates (its border box's top-left is the local origin).
                var (ox, oy) = parent.ActualPerspectiveOrigin;
                var originX = parentFragment.WholeBoxRect.X + ox - position.X;
                var originY = parentFragment.WholeBoxRect.Y + oy - position.Y;

                var d = parent.ActualPerspective;
                var persp = Matrix4x4.Identity;
                persp.M34 = (float)(-1.0 / d);
                perspective = Matrix4x4.CreateTranslation((float)-originX, (float)-originY, 0) * persp * Matrix4x4.CreateTranslation((float)originX, (float)originY, 0);
                hasPerspective = true;
            }

            var combined = hasPerspective ? local * perspective : local;
            var map = Homography.FromMatrix4(combined);

            // Local (border-box-origin) coordinates to page coordinates.
            map = Homography.Then(Homography.Then(Homography.Translation(-position.X, -position.Y), map), Homography.Translation(position.X, position.Y));
            // A turned-away box with backface-visibility: hidden is not painted, whether or not its map is affine (rotateY(180deg) is).
            var showsBack = box.IsBackfaceHidden && FacesAway(combined);

            var centre = map.ApplyHomogeneous(fragment.WholeBoxRect.X + fragment.WholeBoxRect.Width / 2, fragment.WholeBoxRect.Y + fragment.WholeBoxRect.Height / 2);
            if (map.IsAffine)
            {
                if (showsBack)
                    return new ProjectiveWarp(map, Hidden: true);

                // Without a perspective on the parent the box's own affine matrix is what the vector path uses already.
                if (!hasPerspective)
                    return null;

                // A plane at a constant depth: a uniform divisor. Behind the viewer it is not there at all.
                if (!(centre.W > 1e-9))
                    return new ProjectiveWarp(map, Hidden: true);

                var affine = new RMatrix(map.M11 / map.M33, map.M21 / map.M33, map.M12 / map.M33, map.M22 / map.M33, map.M13 / map.M33, map.M23 / map.M33);
                return affine.IsIdentity ? null : new ProjectiveWarp(map, Hidden: false, affine);
            }

            // The sign of w is meaningful (positive is in front of the eye): ProjectRectangle clips at w > 0, so a box wholly behind the eye
            // projects to nothing and a straddling one paints only its visible part. Negating a map whose centre is behind the eye would
            // reflect that region through the origin and paint it.
            return new ProjectiveWarp(map, showsBack);
        }

        /// <summary>
        /// Whether the plane's front now points away from the viewer: the sign of the z component of the transformed normal, which is the
        /// determinant of the upper-left 2x2 over that of the upper-left 3x3. <c>rotateY(180deg)</c> shows its back; <c>scaleX(-1)</c>, a
        /// mirror in the plane, does not.
        /// </summary>
        private static bool FacesAway(in Matrix4x4 m)
        {
            var det2 = (double)m.M11 * m.M22 - (double)m.M12 * m.M21;
            var det3 =
                (double)m.M11 * ((double)m.M22 * m.M33 - (double)m.M23 * m.M32) -
                (double)m.M12 * ((double)m.M21 * m.M33 - (double)m.M23 * m.M31) +
                (double)m.M13 * ((double)m.M21 * m.M32 - (double)m.M22 * m.M31);
            return det3 != 0 && det2 / det3 < 0;
        }

        private bool PaintProjective(RGraphics g, BoxFragment fragment, ProjectiveWarp warp)
        {
            var box = fragment.Box;
            if (warp.Hidden)
                return true; // backface-visibility: hidden and turned away, or behind the viewer: nothing to paint

            if (SubtreeExtent(fragment) is not { } extent)
                return true;

            var source = Inflate(extent, SubtreeBleed(fragment));
            if (!warp.Map.TryProjectRectangleBounds(source.Left, source.Top, source.Right, source.Bottom, out var minX, out var minY, out var maxX, out var maxY))
                return true; // wholly behind the viewer

            var destination = Intersect(new RRect(minX, minY, maxX - minX, maxY - minY), g.GetClip());
            if (destination.Width <= 0 || destination.Height <= 0)
                return true;

            // A plane brought closer than its flat size is drawn larger than its bitmap: render the source with as many more pixels as the
            // warp enlarges it by, at most a few times over, so it does not blur.
            var dpi = container.Adapter.RasterizationDpi * Magnification(warp.Map, source);

            using var sourceScope = g.BeginRasterSurface(source, dpi);
            if (sourceScope is null)
                return false;

            using var destinationScope = g.BeginRasterSurface(destination);
            if (destinationScope is null)
                return false;

            var wasSuppressed = _taggingSuppressed;
            _taggingSuppressed = true;
            try
            {
                PaintClippedWithEffects(sourceScope.Graphics, fragment);
            }
            finally
            {
                _taggingSuppressed = wasSuppressed;
            }

            Warp.Apply(sourceScope.Surface, destinationScope.Surface, warp.Map);

            var builder = _taggingSuppressed ? null : box.HtmlContainer?.StructureTagBuilder;
            using (builder?.OpenArtifact(g))
                g.DrawRaster(destinationScope.Surface);

            // The text that was drawn into the bitmap is supplied again, invisibly. Its positions follow the transform's local
            // linearisation: exact at the box's centre, approximate away from it, which is as close as an affine text matrix can get.
            g.PushTransform(Linearise(warp.Map, fragment.WholeBoxRect));
            try
            {
                PaintSelectableText(g, fragment);
            }
            finally
            {
                g.PopTransform();
            }

            return true;
        }

        /// <summary>How much larger than its flat size the warp draws <paramref name="source"/> at its most magnified corner or its centre, at least 1.</summary>
        private static double Magnification(Homography map, RRect source)
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
            return Math.Clamp(most, 1.0, MaxProjectiveMagnification);
        }

        /// <summary>The affine map that agrees with <paramref name="map"/> at the centre of <paramref name="box"/> in value and in slope.</summary>
        private static RMatrix Linearise(Homography map, RRect box)
        {
            var cx = box.X + box.Width / 2;
            var cy = box.Y + box.Height / 2;
            var step = Math.Max(1.0, Math.Min(box.Width, box.Height) / 4);

            if (map.Apply(cx, cy) is not { } centre || map.Apply(cx + step, cy) is not { } right || map.Apply(cx, cy + step) is not { } below)
                return RMatrix.Identity;

            var m11 = (right.X - centre.X) / step;
            var m12 = (right.Y - centre.Y) / step;
            var m21 = (below.X - centre.X) / step;
            var m22 = (below.Y - centre.Y) / step;
            return new RMatrix(m11, m12, m21, m22, centre.X - (cx * m11 + cy * m21), centre.Y - (cx * m12 + cy * m22));
        }
    }
}
