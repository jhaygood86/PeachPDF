using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Raster;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

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
    /// <b>No depth to resolve.</b> When every plane stays parallel to the view plane at the same depth (<see cref="HasDepthToResolve"/> says
    /// otherwise) there is nothing a depth test could change, and the context is painted exactly as it was before it was modelled, in
    /// vector form.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>Below this, a matrix entry that would tilt a plane out of the view plane, or move it in depth, counts as zero.</summary>
        private const double FlatTolerance = 1e-6;

        /// <summary>One member of a 3D rendering context. Mutable, and kept in a reused list, so a context allocates nothing per plane.</summary>
        private struct Plane
        {
            public BoxFragment Fragment;

            /// <summary>The plane's whole transform in page coordinates.</summary>
            public Matrix4x4 Accumulated;

            public Homography Map;
            public DepthPlane Depth;

            /// <summary>What the plane paints, in its own untransformed page coordinates; only meaningful when <see cref="HasSource"/>.</summary>
            public RRect Source;

            /// <summary>Whether there is anything of this plane to draw: it is not turned away, has content and is at least partly in front of the viewer.</summary>
            public bool HasSource;

            /// <summary><c>backface-visibility: hidden</c> and turned away from the viewer.</summary>
            public bool Hidden;

            /// <summary>The index of the plane this one's box is a child of (-1 for the root).</summary>
            public int Parent;

            /// <summary>Where the plane sorts in depth: its centre's, but never behind its parent's, so a child is never painted before what it sits on.</summary>
            public double SortDepth;
        }

        /// <summary>A plane's place in the back-to-front order: its centre's depth, then its position in the tree (so a parent is under its coplanar children).</summary>
        private struct DepthKey
        {
            public double Depth;
            public int Plane;
        }

        private static readonly Comparison<DepthKey> BackToFront = static (a, b) =>
        {
            var byDepth = a.Depth.CompareTo(b.Depth);
            return byDepth != 0 ? byDepth : a.Plane.CompareTo(b.Plane);
        };

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
        private bool TryPaintContext3D(RGraphics g, BoxFragment root)
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
                if (planes.Count < 2 || !HasDepthToResolve(CollectionsMarshal.AsSpan(planes)))
                    return false;

                for (var i = 0; i < planes.Count; i++)
                    members.Add(planes[i].Fragment);

                if (!ComposeContext(g, CollectionsMarshal.AsSpan(planes)))
                    return false;

                (_composedContexts ??= new HashSet<BoxFragment>(ReferenceEqualityComparer.Instance)).Add(root);
                return true;
            }
            finally
            {
                for (var i = 0; i < planes.Count; i++)
                    members.Remove(planes[i].Fragment);

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
            planes.Add(new Plane { Fragment = fragment, Accumulated = accumulated, Hidden = box.IsBackfaceHidden && FacesAway(accumulated), Parent = parent });

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
            var perspective = Matrix4x4.Identity;
            perspective.M34 = (float)(-1.0 / distance);
            return Matrix4x4.CreateTranslation(-x, -y, 0) * perspective * Matrix4x4.CreateTranslation(x, y, 0);
        }

        /// <summary>The perspective the context's root is viewed through: the one its parent gives it, or the identity.</summary>
        private Matrix4x4 RootPerspective(BoxFragment root) =>
            root.Box.ParentBox is { ActualPerspective: > 0 } parent && _pageRoot is not null && FindFragment(_pageRoot, parent) is { } parentFragment
                ? PerspectiveOf(parentFragment)
                : Matrix4x4.Identity;

        /// <summary>
        /// Whether a depth test could change anything: false only when every plane faces the viewer and stays parallel to the view plane, at
        /// the same depth as all the others, with no perspective divisor - then painting them in tree order is already right.
        /// </summary>
        private static bool HasDepthToResolve(ReadOnlySpan<Plane> planes)
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
        /// Paints every plane of <paramref name="planes"/> into a shared bitmap through a depth test and draws it, then supplies their text.
        /// </summary>
        /// <returns>false, having painted nothing, when <paramref name="g"/> cannot rasterize.</returns>
        private bool ComposeContext(RGraphics g, Span<Plane> planes)
        {
            // Where every plane lands, and what part of the picture that is.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var any = false;
            Span<DepthKey> keys = planes.Length <= StackPlanes ? stackalloc DepthKey[StackPlanes] : new DepthKey[planes.Length];
            var ordered = 0;

            for (var i = 0; i < planes.Length; i++)
            {
                ref var plane = ref planes[i];
                plane.Map = Homography.FromMatrix4(plane.Accumulated);
                plane.Depth = DepthPlane.FromMatrix4(plane.Accumulated);

                // Where the plane sorts: its centre's depth, and never behind its parent's, so a child is drawn after what it sits on however the
                // context is tilted. A centre behind the viewer has no depth: such a plane is drawn first, whatever part of it is in front.
                var centre = plane.Fragment.WholeBoxRect;
                var depth = plane.Depth.At(centre.X + centre.Width / 2, centre.Y + centre.Height / 2);
                plane.SortDepth = double.IsNaN(depth) ? double.NegativeInfinity : depth;
                if (plane.Parent >= 0)
                    plane.SortDepth = Math.Max(plane.SortDepth, planes[plane.Parent].SortDepth);

                if (plane.Hidden || SubtreeExtent(plane.Fragment, _contextMembers) is not { } extent)
                    continue;

                plane.Source = Inflate(extent, SubtreeBleed(plane.Fragment, _contextMembers));
                if (!plane.Map.TryProjectRectangleBounds(plane.Source.Left, plane.Source.Top, plane.Source.Right, plane.Source.Bottom,
                        out var left, out var top, out var right, out var bottom))
                    continue; // wholly behind the viewer

                plane.HasSource = true;
                any = true;
                minX = Math.Min(minX, left);
                minY = Math.Min(minY, top);
                maxX = Math.Max(maxX, right);
                maxY = Math.Max(maxY, bottom);
                keys[ordered++] = new DepthKey { Depth = plane.SortDepth, Plane = i };
            }

            if (!any)
                return true; // everything is turned away or behind the viewer: nothing is drawn

            var destination = Intersect(new RRect(minX, minY, maxX - minX, maxY - minY), g.GetClip());
            if (destination.Width <= 0 || destination.Height <= 0)
                return true;

            using var destinationScope = g.BeginRasterSurface(destination);
            if (destinationScope is null)
                return false;

            keys[..ordered].Sort(BackToFront);
            using var depthBuffer = new DepthBuffer(destinationScope.Surface.Width, destinationScope.Surface.Height);

            var wasSuppressed = _taggingSuppressed;
            _taggingSuppressed = true;
            try
            {
                for (var k = 0; k < ordered; k++)
                {
                    ref var plane = ref planes[keys[k].Plane];

                    // A plane brought closer than its flat size is drawn larger than its bitmap: give it as many more pixels as the warp
                    // enlarges it by, so it does not blur.
                    var dpi = container.Adapter.RasterizationDpi * Magnification(plane.Map, plane.Source);
                    using var sourceScope = g.BeginRasterSurface(plane.Source, dpi);
                    if (sourceScope is null)
                    {
                        plane.HasSource = false;
                        continue;
                    }

                    PaintClippedWithEffects(sourceScope.Graphics, plane.Fragment);
                    Warp.Composite(sourceScope.Surface, destinationScope.Surface, plane.Map, plane.Depth, depthBuffer);
                }
            }
            finally
            {
                _taggingSuppressed = wasSuppressed;
            }

            var builder = _taggingSuppressed ? null : planes[0].Fragment.Box.HtmlContainer?.StructureTagBuilder;
            using (builder?.OpenArtifact(g))
                g.DrawRaster(destinationScope.Surface);

            // The text drawn into the bitmaps is supplied again, invisibly, in document order (not depth order): each plane's text at
            // the position its transform's linearisation gives it, which is exact at the plane's centre and approximate away from it.
            for (var i = 0; i < planes.Length; i++)
            {
                ref var plane = ref planes[i];
                if (!plane.HasSource)
                    continue;

                g.PushTransform(Linearise(plane.Map, plane.Fragment.WholeBoxRect));
                try
                {
                    PaintSelectableText(g, plane.Fragment);
                }
                finally
                {
                    g.PopTransform();
                }
            }

            return true;
        }
    }
}
