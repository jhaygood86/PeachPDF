using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachDrawing;
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
        private readonly record struct ProjectiveWarp(Homography Map, bool Hidden, Matrix3x2? Affine = null);

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

                perspective = ProjectiveRaster.Perspective(parent.ActualPerspective, originX, originY);
                hasPerspective = true;
            }

            var combined = hasPerspective ? local * perspective : local;
            var map = Homography.FromMatrix4(combined);

            // Local (border-box-origin) coordinates to page coordinates.
            map = Homography.Then(Homography.Then(Homography.Translation(-position.X, -position.Y), map), Homography.Translation(position.X, position.Y));
            // A turned-away box with backface-visibility: hidden is not painted, whether or not its map is affine (rotateY(180deg) is).
            var showsBack = box.IsBackfaceHidden && ProjectiveRaster.FacesAway(combined);

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

                var affine = new Matrix3x2((float)(map.M11 / map.M33), (float)(map.M21 / map.M33), (float)(map.M12 / map.M33), (float)(map.M22 / map.M33), (float)(map.M13 / map.M33), (float)(map.M23 / map.M33));
                return affine.IsIdentity ? null : new ProjectiveWarp(map, Hidden: false, affine);
            }

            // The sign of w is meaningful (positive is in front of the eye): ProjectRectangle clips at w > 0, so a box wholly behind the eye
            // projects to nothing and a straddling one paints only its visible part. Negating a map whose centre is behind the eye would
            // reflect that region through the origin and paint it.
            // A weak perspective on a small plane stays within a fraction of a device pixel of an affine map: that is carried as a plain `cm`, with the
            // content kept as vector artwork, rather than as a bitmap.
            if (!showsBack && SubtreeExtent(fragment) is { } extent &&
                ProjectiveRaster.TryFitAffine(map, Inflate(extent, SubtreeBleed(fragment)), ProjectiveRaster.NearAffineTolerance(container.RenderContext.RasterizationDpi, 1.0), out var fit))
                return new ProjectiveWarp(map, Hidden: false, fit);

            return new ProjectiveWarp(map, showsBack);
        }

        /// <summary>
        /// Whether <paramref name="source"/>, mapped through <paramref name="map"/>, lands as a sliver thinner than
        /// <see cref="MinVisibleThickness"/> - a plane turned edge-on to the viewer, whose image a renderer would still stroke as a hairline.
        /// The projective counterpart of <see cref="HasVisibleThickness"/> (see <see cref="ProjectiveRaster.ProjectsToSliver"/>).
        /// </summary>
        internal static bool ProjectsToSliver(in Homography map, Rect source) => ProjectiveRaster.ProjectsToSliver(map, source, MinVisibleThickness);

        private bool PaintProjective(Canvas g, BoxFragment fragment, ProjectiveWarp warp)
        {
            var box = fragment.Box;
            if (warp.Hidden)
            {
                NoteNotRendered(box);
                return true; // backface-visibility: hidden and turned away, or behind the viewer: nothing to paint
            }

            if (SubtreeExtent(fragment) is not { } extent)
                return true;

            var source = Inflate(extent, SubtreeBleed(fragment));
            var map = warp.Map;

            var outcome = ProjectiveRaster.Paint(g, map, source, MinVisibleThickness,
                paintSource: sourceGraphics =>
                {
                    var wasSuppressed = _taggingSuppressed;
                    _taggingSuppressed = true;
                    var enclosingMap = EnterMap(map);
                    try
                    {
                        RecordPlacement(fragment);
                        PaintClippedWithEffects(sourceGraphics, fragment);
                    }
                    finally
                    {
                        LeaveMap(enclosingMap);
                        _taggingSuppressed = wasSuppressed;
                    }
                },
                openArtifact: () => _taggingSuppressed ? null : box.HtmlContainer?.StructureTagBuilder?.OpenArtifact(g));

            switch (outcome)
            {
                case ProjectiveOutcome.NotRendered:
                    NoteNotRendered(box); // wholly behind the viewer, or edge-on: nothing worth a bitmap, and no text layer to supply
                    return true;

                case ProjectiveOutcome.ClippedOut:
                    // Nothing of it lands in the clip, so nothing under it is drawn on this page: the annotation pass must not read "not recorded"
                    // as "not moved" for the box or anything inside it.
                    RecordHiddenSubtree(fragment);
                    return true;

                case ProjectiveOutcome.NoRaster:
                    return false;

                default:
                    SupplyWarpedText(g, fragment, map);
                    return true;
            }
        }

        /// <summary>
        /// Supplies the text that was drawn into a warped bitmap again, invisibly, so it stays selectable. Its positions follow the transform's
        /// local linearisation: exact at the box's centre, approximate away from it, which is as close as an affine text matrix can get. An
        /// edge-on plane (a linearisation with no inverse) has no visible area, so it has no text to supply either.
        /// </summary>
        internal void SupplyWarpedText(Canvas g, BoxFragment fragment, in Homography map)
        {
            if (!TryPushTransform(g, Linearise(map, fragment.WholeBoxRect)))
                return;

            try
            {
                PaintSelectableText(g, fragment);
            }
            finally
            {
                g.PopTransform();
            }
        }

        /// <summary>The affine map that agrees with <paramref name="map"/> at the centre of <paramref name="box"/> in value and in slope.</summary>
        private static Matrix3x2 Linearise(Homography map, Rect box) => ProjectiveRaster.Linearise(map, box);
    }
}
