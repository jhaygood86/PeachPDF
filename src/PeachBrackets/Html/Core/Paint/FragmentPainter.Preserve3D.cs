using PeachPDF.CSS;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachDrawing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Plane = PeachPDF.Html.Core.Paint.ProjectivePlane<PeachPDF.Html.Core.Fragments.BoxFragment>;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// <c>transform-style: preserve-3d</c> (CSS Transforms 2 §6.1): a 3D rendering context, in which a box and its descendants share one 3D
    /// space instead of each being flattened into its parent's plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The model.</b> A context is rooted at a box whose <em>used</em> <c>transform-style</c> is <c>preserve-3d</c>
    /// (<see cref="DomUtils.EstablishesPreserve3d"/>: a grouping property such as <c>overflow</c>, <c>opacity</c> or <c>filter</c> forces
    /// <c>flat</c>). Its members are the root and, recursively, every child of a member that itself uses <c>preserve-3d</c>. Each member is one
    /// <em>plane</em>: the member's own content plus whatever is flattened into it (its non-member descendants).
    /// </para>
    /// <para>
    /// <b>Accumulated transforms.</b> A plane's matrix is its own transform (about its transform origin), then the perspective its parent
    /// gives its children, then its parent's whole accumulated matrix: <c>Acc(C) = N(C) · Persp(Q) · Acc(Q)</c> in the row-vector convention,
    /// with <c>Acc(root) = N(root) · Persp(parent of the root)</c>. That accumulation is what makes a grandchild see a perspective set on an
    /// ancestor, and a child of a rotated parent rotate with it. Everything is in page (fragmentainer-local) coordinates, so the
    /// planes' matrices are directly comparable.
    /// </para>
    /// <para>
    /// <b>Composition.</b> Each plane is painted untransformed into its own bitmap (the other members are excluded through
    /// <see cref="_contextMembers"/>, which <see cref="PaintFragment"/> honours) and <see cref="Warp.Composite"/> draws it into one shared
    /// destination bitmap through a per-pixel depth test, so planes that intersect are resolved exactly where they cross. Planes are drawn back
    /// to front by their centre's depth first, which is what makes translucent pixels blend in the right order. The destination is drawn once.
    /// </para>
    /// <para>
    /// <b>No depth to resolve.</b> When every plane stays parallel to the view plane at the same depth (<see cref="ProjectiveRaster.HasDepthToResolve{T}"/> says
    /// otherwise) there is nothing a depth test could change, and the context is painted exactly as it was before it was modelled, in
    /// vector form.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>The planes of the context being painted, and of any context enclosing it: <see cref="PaintFragment"/> leaves them to the context.</summary>
        private HashSet<BoxFragment>? _contextMembers;

        /// <summary>The list of planes of the last context, kept for the next one (taken while in use, so a nested context simply makes its own).</summary>
        private List<Plane>? _planeScratch;

        /// <summary>Roots of contexts already composed on this page, so a stacking context reached by a second ordering scope is not drawn twice.</summary>
        private HashSet<BoxFragment>? _composedContexts;

        /// <summary>The most planes sorted in stack memory; more are sorted in a heap array.</summary>
        private const int StackPlanes = 128;

        /// <summary>
        /// Paints <paramref name="root"/> and the rest of its 3D rendering context as one depth-resolved picture, when it is the root of one that
        /// has depth to resolve.
        /// </summary>
        /// <returns>
        /// false, having painted nothing, when <paramref name="root"/> does not start such a context or <paramref name="g"/> cannot rasterize: the
        /// caller then paints it one element at a time, as it always did.
        /// </returns>
        private bool TryPaintContext3D(Canvas g, BoxFragment root)
        {
            if (!DomUtils.EstablishesPreserve3d(root.Box))
                return false;

            // Reached a second time (a stacking context is found by more than one ordering scope): its picture is already drawn.
            if (_composedContexts?.Contains(root) == true)
                return true;

            var planes = _planeScratch ?? new List<Plane>(16);
            _planeScratch = null;
            var members = _contextMembers ??= new HashSet<BoxFragment>(ReferenceEqualityComparer.Instance);

            try
            {
                CollectPlanes(root, RootPerspective(root), planes, -1);
                if (planes.Count < 2 || !ProjectiveRaster.HasDepthToResolve<BoxFragment>(CollectionsMarshal.AsSpan(planes)))
                    return false;

                for (var i = 0; i < planes.Count; i++)
                    members.Add(planes[i].Subject);

                if (!ComposeContext(g, CollectionsMarshal.AsSpan(planes)))
                    return false;

                (_composedContexts ??= new HashSet<BoxFragment>(ReferenceEqualityComparer.Instance)).Add(root);
                return true;
            }
            finally
            {
                for (var i = 0; i < planes.Count; i++)
                    members.Remove(planes[i].Subject);

                planes.Clear();
                _planeScratch = planes;
            }
        }

        /// <summary>
        /// Adds <paramref name="fragment"/> as a plane, then - if it keeps its children in the same 3D space - theirs, in tree order. A child that
        /// neither has a transform of its own, nor starts its own chain of <c>preserve-3d</c>, nor has a <c>backface-visibility</c> to apply is
        /// coplanar with its parent by construction and is painted as part of the parent's plane, in ordinary CSS 2.1 paint order.
        /// </summary>
        /// <param name="fragment">the plane to add</param>
        /// <param name="tail">what the fragment's own matrix is followed by: the perspective its parent gives it, then the parent's accumulated matrix</param>
        /// <param name="planes">receives the planes, the fragment first</param>
        /// <param name="parent">the index in <paramref name="planes"/> of the plane this one's box is a child of, or -1</param>
        private static void CollectPlanes(BoxFragment fragment, in Matrix4x4 tail, List<Plane> planes, int parent)
        {
            var box = fragment.Box;
            var rect = fragment.WholeBoxRect;

            // The box's own transform is about its border box's top-left in its own coordinates: take the plane there and back.
            var own = Matrix4x4.CreateTranslation((float)-rect.X, (float)-rect.Y, 0) * (box.ActualTransform4 ?? Matrix4x4.Identity) *
                      Matrix4x4.CreateTranslation((float)rect.X, (float)rect.Y, 0);
            var accumulated = own * tail;

            var index = planes.Count;
            planes.Add(new Plane { Subject = fragment, Accumulated = accumulated, Hidden = box.IsBackfaceHidden && ProjectiveRaster.FacesAway(accumulated), Parent = parent });

            if (!DomUtils.EstablishesPreserve3d(box))
                return;

            var childTail = PerspectiveOf(fragment) * accumulated;
            var children = fragment.Children;
            for (var i = 0; i < children.Count; i++)
            {
                var childBox = children[i].Box;

                // Whatever PaintFragment would not paint at all is not a plane, and a marker is painted with its list item.
                if (childBox.IsMarkerPseudoElement || childBox.DerivedStyle.ActualDisplay == Keywords.None || childBox.Visibility.Value != Visibility.Visible)
                    continue;

                if (childBox.ActualTransform4 is null && !childBox.IsBackfaceHidden && !DomUtils.EstablishesPreserve3d(childBox))
                    continue;

                // A singular transformation matrix renders nothing, with its subtree (PaintFragment's own guard, which the plane's parent
                // reaches when it paints this child as part of its plane): it is not a plane of the context.
                if (childBox.ActualTransform4 is { } childTransform && !IsInvertible4(childTransform))
                    continue;

                CollectPlanes(children[i], childTail, planes, index);
            }
        }

        /// <summary>The matrix that views a box's children through its <c>perspective</c> from its <c>perspective-origin</c>, in page coordinates; the identity without one.</summary>
        private static Matrix4x4 PerspectiveOf(BoxFragment fragment)
        {
            var distance = fragment.Box.ActualPerspective;
            if (!(distance > 0))
                return Matrix4x4.Identity;

            var (ox, oy) = fragment.Box.ActualPerspectiveOrigin;
            var x = (float)(fragment.WholeBoxRect.X + ox);
            var y = (float)(fragment.WholeBoxRect.Y + oy);
            return ProjectiveRaster.Perspective(distance, x, y);
        }

        /// <summary>The perspective the context's root is viewed through: the one its parent gives it, or the identity.</summary>
        private Matrix4x4 RootPerspective(BoxFragment root) =>
            root.Box.ParentBox is { ActualPerspective: > 0 } parent && _pageRoot is not null && FindFragment(_pageRoot, parent) is { } parentFragment
                ? PerspectiveOf(parentFragment)
                : Matrix4x4.Identity;

        /// <summary>
        /// Records the boxes of the planes <paramref name="culled"/> marks as not drawn, so the links and fields in them are not written.
        /// A culled plane is left unrecorded while a plane below it is still drawn: that plane is composed on its own account (a
        /// <c>rotateY(-90deg)</c> inside a <c>rotateY(90deg)</c> faces the viewer again), and the collectors skip a recorded box's whole
        /// subtree, which would take its live links with it. The price is that what is coplanar with the culled plane keeps its links.
        /// "Drawn" means composed (<c>HasSource</c>); a culled plane counts only if something below it is. Planes are in tree order,
        /// parents before children.
        /// </summary>
        private void NoteCulledPlanes(Span<Plane> planes, ReadOnlySpan<bool> culled)
        {
            Span<bool> hasLiveChild = planes.Length <= StackPlanes ? stackalloc bool[StackPlanes] : new bool[planes.Length];
            hasLiveChild = hasLiveChild[..planes.Length];
            hasLiveChild.Clear();

            for (var i = planes.Length - 1; i >= 0; i--)
            {
                var live = planes[i].HasSource || hasLiveChild[i];
                if (live && planes[i].Parent >= 0)
                    hasLiveChild[planes[i].Parent] = true;

                if (culled[i] && !hasLiveChild[i])
                    NoteNotRendered(planes[i].Subject.Box);
            }
        }

        /// <summary>
        /// Paints every plane of <paramref name="planes"/> into a shared bitmap through a depth test and draws it, then supplies their text.
        /// </summary>
        /// <returns>false, having painted nothing, when <paramref name="g"/> cannot rasterize.</returns>
        private bool ComposeContext(Canvas g, Span<Plane> planes)
        {
            // Where every plane lands, and what part of the picture that is.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;
            Span<bool> culled = planes.Length <= StackPlanes ? stackalloc bool[StackPlanes] : new bool[planes.Length];
            culled = culled[..planes.Length];
            culled.Clear();

            for (var i = 0; i < planes.Length; i++)
            {
                ref var plane = ref planes[i];
                ProjectiveRaster.Locate(planes, i, plane.Subject.WholeBoxRect);

                if (plane.Hidden)
                {
                    culled[i] = true;
                    continue;
                }

                if (SubtreeExtent(plane.Subject, _contextMembers) is not { } extent)
                    continue;

                plane.Source = Inflate(extent, SubtreeBleed(plane.Subject, _contextMembers));

                // Wholly behind the viewer, or edge-on (a hairline at most): not drawn.
                if (ProjectiveRaster.Project(plane, plane.Source, MinVisibleThickness, out var left, out var top, out var right, out var bottom) != PlaneProjection.Visible)
                {
                    culled[i] = true;
                    continue;
                }

                plane.HasSource = true;
                any = true;
                minX = Math.Min(minX, left);
                minY = Math.Min(minY, top);
                maxX = Math.Max(maxX, right);
                maxY = Math.Max(maxY, bottom);
            }

            NoteCulledPlanes(planes, culled);

            if (!any)
                return true; // everything is turned away or behind the viewer: nothing is drawn

            var destination = Intersect(new Rect(minX, minY, maxX - minX, maxY - minY), g.GetClip());
            if (destination.Width <= 0 || destination.Height <= 0)
            {
                // Outside the clip, so nothing of these planes is drawn here: see PaintProjective.
                for (var i = 0; i < planes.Length; i++)
                {
                    if (planes[i].HasSource)
                        RecordHiddenSubtree(planes[i].Subject);
                }

                return true;
            }

            Span<int> order = planes.Length <= StackPlanes ? stackalloc int[StackPlanes] : new int[planes.Length];
            var ordered = ProjectiveRaster.OrderBackToFront<BoxFragment>(planes, order);

            // Planes that are only tilted (no perspective) and do not pierce one another need no depth test: drawn back to front, each under its own
            // affine map, they stay vector artwork.
            if (ProjectiveRaster.TryResolveOrder<BoxFragment>(planes, order[..ordered]))
            {
                for (var k = 0; k < ordered; k++)
                    PaintUnderMap(g, planes[order[k]].Subject, ProjectiveRaster.AffineOf(planes[order[k]].Map));

                return true;
            }

            var wasSuppressed = _taggingSuppressed;
            _taggingSuppressed = true;
            bool composed;
            try
            {
                var rootBox = planes[0].Subject.Box;
                composed = ProjectiveRaster.Compose(g, planes, order[..ordered], destination,
                    paintPlane: (ref ProjectivePlane<BoxFragment> plane, Canvas sourceGraphics) =>
                    {
                        var enclosingMap = EnterMap(plane.Map);
                        try
                        {
                            RecordPlacement(plane.Subject);
                            PaintClippedWithEffects(sourceGraphics, plane.Subject);
                        }
                        finally
                        {
                            LeaveMap(enclosingMap);
                        }
                    },
                    planeFailed: (ref ProjectivePlane<BoxFragment> plane, Canvas _) => RecordHiddenSubtree(plane.Subject), // not drawn, so nothing to click
                    openArtifact: () => wasSuppressed ? null : rootBox.HtmlContainer?.StructureTagBuilder?.OpenArtifact(g));
            }
            finally
            {
                _taggingSuppressed = wasSuppressed;
            }

            if (!composed)
                return false;

            // The text drawn into the bitmaps is supplied again, invisibly, in document order (not depth order): each plane's text at
            // the position its transform's linearisation gives it, which is exact at the plane's centre and approximate away from it.
            for (var i = 0; i < planes.Length; i++)
            {
                ref var plane = ref planes[i];
                if (plane.HasSource)
                    SupplyWarpedText(g, plane.Subject, plane.Map);
            }

            return true;
        }
    }
}
