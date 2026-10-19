using PeachDrawing;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Paint;
using System;
using System.Collections.Generic;
using System.Numerics;
using Plane = PeachPDF.Html.Core.Paint.ProjectivePlane<PeachPDF.Svg.SvgElement>;

namespace PeachPDF.Svg
{
    /// <summary>
    /// 3D transforms on SVG elements (CSS Transforms 2 section 11): <c>translate3d()</c>, <c>rotateX()</c>, <c>perspective()</c> and the rest in an
    /// element's <c>transform</c>, the <c>perspective</c> and <c>perspective-origin</c> of its container, <c>backface-visibility</c> and
    /// <c>transform-style: preserve-3d</c>. It is the HTML painter's machinery (<see cref="ProjectiveRaster"/>) pointed at an <see cref="SvgElement"/>
    /// instead of a box: the element is painted untransformed, as it would be without its transform, into a bitmap that is warped through the projective
    /// map; the planes of a <c>preserve-3d</c> context are depth-tested against each other.
    /// </summary>
    /// <remarks>
    /// Everything is measured in the user space of the element's parent, which is also the space <see cref="Canvas.BeginRasterSurface"/> and
    /// <see cref="Canvas.GetClip"/> speak in, so no page coordinates are involved. What stays vector: a transform that is affine once projected (a
    /// rotation about the z axis, a <c>rotateX()</c> with no perspective), and one so close to affine that the difference is a fraction of a device pixel.
    /// </remarks>
    internal static partial class SvgRenderer
    {
        /// <summary>The perspective the container being painted gives the child about to be painted; read (and cleared) by that child's <see cref="RenderElement"/>.</summary>
        [ThreadStatic]
        private static Matrix4x4? s_childPerspective;

        /// <summary>The elements that are planes of the 3D rendering context being composed: the context paints them, their own <see cref="RenderElement"/> must not.</summary>
        [ThreadStatic]
        private static HashSet<SvgElement>? s_contextMembers;

        /// <summary>The viewport the document being painted is clipped to, and the transform in force where it was entered: what is visible, found again in any user space below it.</summary>
        [ThreadStatic]
        private static (Rect Viewport, Matrix3x2 Entry)? s_viewportClip;

        /// <summary>
        /// The part of the picture that can be seen, in the current user space. <see cref="Canvas.GetClip"/> is not it: a clip is recorded in the space it was
        /// pushed in, and the viewBox transform of an SVG is pushed after its viewport's clip, so the two differ by that transform.
        /// </summary>
        private static Rect? VisibleRegion(Canvas g)
        {
            if (s_viewportClip is not { } clip || !Matrix3x2.Invert(g.CurrentTransform, out var inverse))
                return null;

            // entry space -> layout space -> the current user space
            var toCurrent = clip.Entry * inverse;
            var corners = new[]
            {
                Vector2.Transform(new Vector2((float)clip.Viewport.Left, (float)clip.Viewport.Top), toCurrent),
                Vector2.Transform(new Vector2((float)clip.Viewport.Right, (float)clip.Viewport.Top), toCurrent),
                Vector2.Transform(new Vector2((float)clip.Viewport.Left, (float)clip.Viewport.Bottom), toCurrent),
                Vector2.Transform(new Vector2((float)clip.Viewport.Right, (float)clip.Viewport.Bottom), toCurrent),
            };

            var minX = double.MaxValue; var minY = double.MaxValue; var maxX = double.MinValue; var maxY = double.MinValue;
            foreach (var corner in corners)
            {
                minX = Math.Min(minX, corner.X); maxX = Math.Max(maxX, corner.X);
                minY = Math.Min(minY, corner.Y); maxY = Math.Max(maxY, corner.Y);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>The thinnest sliver of a plane worth a bitmap, in points: the same as the HTML painter's.</summary>
        private const double MinVisibleThicknessPoints = 0.017 * PeachPDF.CSS.Length.PointsPerPx;

        /// <summary>The matrix that views <paramref name="container"/>'s children through its <c>perspective</c> from its <c>perspective-origin</c>, or null without one.</summary>
        private static Matrix4x4? PerspectiveOf(SvgElement container)
        {
            if (container.Perspective is not { } distance || !(distance > 0) || container.PerspectiveOrigin is not { } origin)
                return null;

            var (x, y, _) = origin.Resolve(container);
            return ProjectiveRaster.Perspective(distance, x, y);
        }

        /// <summary>Paints <paramref name="children"/> of <paramref name="container"/>, each seeing the perspective the container gives them.</summary>
        private static void RenderChildren(Canvas g, SvgDocument document, SvgElement? container, IEnumerable<SvgElement> children, double opacity, (double Width, double Height) viewport)
        {
            var perspective = container is null ? null : PerspectiveOf(container);
            var saved = s_childPerspective;

            try
            {
                foreach (var child in children)
                {
                    s_childPerspective = perspective;
                    RenderElement(g, document, child, opacity, viewport);
                }
            }
            finally
            {
                s_childPerspective = saved;
            }
        }

        /// <summary>The 4x4 an element's own transform amounts to: its 3D matrix, or its 2D one lifted into 3D, or the identity.</summary>
        private static Matrix4x4 TransformOf(SvgElement element)
        {
            if (element.Transform3D is { } full)
                return full;

            return element.Transform is { } flat
                ? new Matrix4x4(flat.M11, flat.M12, 0, 0, flat.M21, flat.M22, 0, 0, 0, 0, 1, 0, flat.M31, flat.M32, 0, 1)
                : Matrix4x4.Identity;
        }

        /// <summary>
        /// Paints <paramref name="element"/> through its 3D transform, when it has one or starts a 3D rendering context.
        /// </summary>
        /// <returns>false when the element is an ordinary one (no 3D transform, and no context with depth in it): the caller paints it as always.</returns>
        private static bool TryRenderProjective(Canvas g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport, Matrix4x4? parentPerspective)
        {
            if (element.PreserveThreeD && EstablishesContext(element) && TryRenderContext(g, document, element, inheritedOpacity, viewport, parentPerspective))
                return true;

            if (element.Transform3D is not { } full)
                return false;

            // A singular matrix renders nothing, with its subtree (CSS Transforms 1 section 10).
            if (!FragmentPainter.IsInvertible4(full))
                return true;

            var combined = parentPerspective is { } perspective ? full * perspective : full;
            var map = Homography.FromMatrix4(combined);
            var turnedAway = element.BackfaceHidden && ProjectiveRaster.FacesAway(combined);

            if (map.IsAffine)
            {
                // A turned-away element with backface-visibility: hidden is not painted, whether or not its map is affine (rotateY(180deg) is).
                if (turnedAway)
                    return true;

                // A plane at a constant depth: a uniform divisor. Behind the viewer it is not there at all.
                if (!(map.M33 > 1e-9))
                    return true;

                var affine = new Matrix3x2((float)(map.M11 / map.M33), (float)(map.M21 / map.M33), (float)(map.M12 / map.M33), (float)(map.M22 / map.M33), (float)(map.M13 / map.M33), (float)(map.M23 / map.M33));
                RenderUnderMatrix(g, document, element, inheritedOpacity, viewport, affine);
                return true;
            }

            if (turnedAway)
                return true;

            if (PlaneExtent(g, document, element, viewport, excluded: null) is not { } source)
                return true; // nothing to paint

            var toleranceUnits = ProjectiveRaster.NearAffineTolerance(g.RasterizationDpi, g.TransformScale.X);
            if (ProjectiveRaster.TryFitAffine(map, source, toleranceUnits, out var fit))
            {
                RenderUnderMatrix(g, document, element, inheritedOpacity, viewport, fit);
                return true;
            }

            var outcome = ProjectiveRaster.Paint(g, map, source, MinVisibleThicknessPoints / Math.Max(g.TransformScale.X, 1e-9),
                paintSource: sourceGraphics => RenderElementContent(sourceGraphics, document, element, inheritedOpacity, viewport),
                clip: VisibleRegion(g));

            // No raster context (a measure-only pass): the tangent of the map at the centre is the best that is left.
            if (outcome == ProjectiveOutcome.NoRaster)
                RenderUnderMatrix(g, document, element, inheritedOpacity, viewport, ProjectiveRaster.Linearise(map, source));

            return true;
        }

        /// <summary>Paints <paramref name="element"/>'s content (without its own transform) under <paramref name="matrix"/>.</summary>
        private static void RenderUnderMatrix(Canvas g, SvgDocument document, SvgElement element, double inheritedOpacity, (double Width, double Height) viewport, Matrix3x2 matrix)
        {
            g.PushTransform(matrix);
            try
            {
                RenderElementContent(g, document, element, inheritedOpacity, viewport);
            }
            finally
            {
                g.PopTransform();
            }
        }

        /// <summary>
        /// Where <paramref name="element"/> paints, in its own untransformed user space: the bounds of its shapes with the reach of their strokes, its text, and the
        /// region of any filter, with the children in <paramref name="excluded"/> (the other planes of a 3D context) left out. Null when it paints nothing.
        /// </summary>
        private static Rect? PlaneExtent(Canvas g, SvgDocument document, SvgElement element, (double Width, double Height) viewport, HashSet<SvgElement>? excluded)
        {
            Rect? box;

            if (element is SvgGroupElement group)
            {
                box = null;
                foreach (var child in group.Children)
                {
                    if (excluded?.Contains(child) == true || PlaneExtent(g, document, child, viewport, excluded) is not { } childBox)
                        continue;

                    if (child.Transform is { } transform)
                        childBox = SvgGeometryBounds.TransformBounds(childBox, transform);

                    box = box is { } existing ? UnionRects(existing, childBox) : childBox;
                }
            }
            else
            {
                box = SvgInkExtent.LocalBounds(element);
                if (GetOpacityGroupBounds(g, element, viewport) is { } content)
                    box = box is { } ink ? UnionRects(ink, content) : content;
            }

            // The region a filter paints is not the box of the shapes under it.
            if (element.FilterRef is { } filterRef && document.Filters.TryGetValue(filterRef, out var filter))
            {
                var (x, y, width, height) = SvgFilterEvaluator.ResolveFilterRect(filter, box);
                var region = new Rect(x, y, width, height);
                box = box is { } shapes ? UnionRects(shapes, region) : region;
            }

            // A little room for what has no static extent (a marker, a curve's overshoot).
            return box is { Width: >= 0, Height: >= 0 } padded
                ? new Rect(padded.X - padded.Width * 0.02 - 1, padded.Y - padded.Height * 0.02 - 1, padded.Width * 1.04 + 2, padded.Height * 1.04 + 2)
                : null;
        }

        // ---- transform-style: preserve-3d ----

        /// <summary>
        /// Whether <paramref name="element"/>'s used <c>transform-style</c> is <c>preserve-3d</c>: a container (children to share the space with) that none of the
        /// grouping properties flattens - <c>opacity</c>, <c>filter</c>, <c>clip-path</c> and <c>mask</c> (CSS Transforms 2 section 7.1) - and that is not a viewport,
        /// which clips (<c>overflow</c>). A <c>clipPath</c>, <c>mask</c> or <c>pattern</c> always flattens its content (section 11).
        /// </summary>
        private static bool EstablishesContext(SvgElement element) =>
            element is SvgGroupElement && element.PreserveThreeD && element.Opacity >= 1 &&
            element.FilterRef is null && element.ClipPathRef is null && element.MaskRef is null;

        /// <summary>
        /// Adds <paramref name="element"/> as a plane, then - if it keeps its children in the same 3D space - theirs, in tree order. A child with no transform of its own,
        /// that starts no context and has no <c>backface-visibility</c> to apply is coplanar with its parent by construction and is painted as part of the parent's plane.
        /// </summary>
        private static void CollectPlanes(SvgElement element, in Matrix4x4 tail, List<Plane> planes, int parent)
        {
            var accumulated = TransformOf(element) * tail;
            var index = planes.Count;
            planes.Add(new Plane { Subject = element, Accumulated = accumulated, Hidden = element.BackfaceHidden && ProjectiveRaster.FacesAway(accumulated), Parent = parent });

            if (!EstablishesContext(element) || element is not SvgGroupElement group)
                return;

            var childTail = (PerspectiveOf(element) ?? Matrix4x4.Identity) * accumulated;
            foreach (var child in group.Children)
            {
                if (child.Transform is null && child.Transform3D is null && !child.BackfaceHidden && !EstablishesContext(child))
                    continue;

                // A singular matrix renders nothing, with its subtree: it is not a plane of the context.
                if (!FragmentPainter.IsInvertible4(TransformOf(child)))
                    continue;

                CollectPlanes(child, childTail, planes, index);
            }
        }

        /// <summary>
        /// Paints <paramref name="root"/> and the rest of its 3D rendering context as one depth-resolved picture, when it is the root of one that has depth to resolve.
        /// </summary>
        /// <returns>false, having painted nothing, when there is no depth to resolve or <paramref name="g"/> cannot rasterize: the caller then paints it one element at a time.</returns>
        private static bool TryRenderContext(Canvas g, SvgDocument document, SvgElement root, double inheritedOpacity, (double Width, double Height) viewport, Matrix4x4? parentPerspective)
        {
            var planes = new List<Plane>(8);
            CollectPlanes(root, parentPerspective ?? Matrix4x4.Identity, planes, -1);

            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(planes);
            if (planes.Count < 2 || !ProjectiveRaster.HasDepthToResolve<SvgElement>(span))
                return false;

            var members = s_contextMembers is { } enclosing ? new HashSet<SvgElement>(enclosing) : [];
            foreach (var plane in planes)
                members.Add(plane.Subject);

            var thickness = MinVisibleThicknessPoints / Math.Max(g.TransformScale.X, 1e-9);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;

            for (var i = 0; i < span.Length; i++)
            {
                ref var plane = ref span[i];
                var extent = PlaneExtent(g, document, plane.Subject, viewport, members);
                ProjectiveRaster.Locate(span, i, extent ?? default);

                if (plane.Hidden || extent is not { } source)
                    continue;

                plane.Source = source;
                if (ProjectiveRaster.Project(plane, source, thickness, out var left, out var top, out var right, out var bottom) != PlaneProjection.Visible)
                    continue;

                plane.HasSource = true;
                any = true;
                minX = Math.Min(minX, left);
                minY = Math.Min(minY, top);
                maxX = Math.Max(maxX, right);
                maxY = Math.Max(maxY, bottom);
            }

            if (!any)
                return true; // everything is turned away or behind the viewer: nothing is drawn

            var destination = ProjectiveRaster.Intersect(new Rect(minX, minY, maxX - minX, maxY - minY), VisibleRegion(g) ?? g.GetClip());
            if (destination.Width <= 0 || destination.Height <= 0)
                return true; // outside the clip

            Span<int> order = span.Length <= 128 ? stackalloc int[128] : new int[span.Length];
            var ordered = ProjectiveRaster.OrderBackToFront<SvgElement>(span, order);

            var previousMembers = s_contextMembers;
            s_contextMembers = members;
            try
            {
                // Planes that are only tilted (no perspective) and do not pierce one another need no depth test: drawn back to front, each under its own
                // affine map, they stay vector artwork.
                if (ProjectiveRaster.TryResolveOrder<SvgElement>(span, order[..ordered]))
                {
                    for (var k = 0; k < ordered; k++)
                        RenderUnderMatrix(g, document, span[order[k]].Subject, inheritedOpacity, viewport, ProjectiveRaster.AffineOf(span[order[k]].Map));

                    return true;
                }

                return ProjectiveRaster.Compose(g, span, order[..ordered], destination,
                    paintPlane: (ref Plane plane, Canvas sourceGraphics) => RenderElementContent(sourceGraphics, document, plane.Subject, inheritedOpacity, viewport),
                    planeFailed: (ref Plane _, Canvas _) => { },
                    openArtifact: null);
            }
            finally
            {
                s_contextMembers = previousMembers;
            }
        }
    }
}
