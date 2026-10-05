using PeachDrawing.Core;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// A small, purpose-built, single-shot builder that walks an already-laid-out
    /// <see cref="CssBox"/> subtree (post <see cref="RunningElementLayout.LayoutRunningElementFor"/>)
    /// into a matching <see cref="BoxFragment"/> tree, for css-gcpm-3's <c>content: element()</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not a reuse of <see cref="FragmentEmitter"/>'s pagination-aware <c>Materialize</c>/
    /// <c>BuildDraft</c> machinery - that system is inseparably coupled to multi-pass span/slice
    /// bookkeeping (which slot a rectangle belongs to, whether a box's content is contiguous across
    /// fragmentainers, a frozen slot being re-opened) that is meaningless for a subtree that never
    /// fragments: a running box's isolated layout is always exactly one, whole, unbroken pass. Every
    /// fragment this produces reports <c>IsFirstFragment</c>/<c>IsLastFragment</c> true and an
    /// unbroken <see cref="SliceGeometry"/>, since nothing here is ever sliced by a page/column break.
    /// </remarks>
    internal static class MarginBoxContentFragmentBuilder
    {
        /// <param name="box">The root of the laid-out subtree (the running element itself).</param>
        internal static BoxFragment Build(CssBox box) => Build(box, clip: null);

        /// <param name="box">the box to build a fragment for</param>
        /// <param name="clip">
        /// what the nearest <c>overflow</c>-clipping ancestor inside this subtree imposes on <paramref name="box"/>,
        /// or null when nothing does. A fragment carries its clipping <i>ancestor's</i> clip, not its own (the
        /// painter pushes it before drawing the fragment's content), so the root always starts with none: a
        /// running element or footnote body is not clipped by anything outside itself.
        /// </param>
        private static BoxFragment Build(CssBox box, ClipSource? clip)
        {
            var lines = BuildLines(box);
            var words = box.Words.Select(w => new TextFragment(w.Rectangle, w)).ToList();

            // What this box passes down: its own clip when it has one, else whatever reached it.
            // A leaf has nobody to pass a clip to, so it is not derived (it would only allocate a curve and basis).
            var forChildren = box.Boxes.Count > 0 && DomUtils.ClipsItsOverflow(box) ? ClipSource.Of(box) : clip;

            var children = box.Boxes
                .Where(b => b.DerivedStyle.ActualDisplay != Keywords.None && !b.IsOutOfFlow && !b.IsRunningPositioned)
                .Select(b => Build(b, forChildren))
                .ToList();

            return new BoxFragment(
                Rect: box.Bounds,
                Box: box,
                FragmentainerIndex: -1,
                OriginY: 0,
                WholeBoxRect: box.Bounds,
                IsFixed: false,
                IsFirstFragment: true,
                IsLastFragment: true,
                IsMonolithic: false,
                Lines: lines,
                Words: words,
                Children: children,
                OverflowClip: clip?.Rect,
                OverflowClipCurve: clip?.Curve,
                OverflowClipBasis: clip?.Basis);
        }

        /// <summary>
        /// A clipping box's clip, in the shape a <see cref="BoxFragment"/> carries it. Derived by
        /// <see cref="RenderUtils.OverflowClipGeometryOf"/>, the same derivation <see cref="FragmentEmitter"/> uses, so
        /// a footnote body or running element clips exactly as the same box in the page does. Its out-of-flow
        /// descendants are left out of this tree (see <see cref="Build(CssBox)"/>), so the chain never needs to
        /// jump to another containing block.
        /// </summary>
        private sealed record ClipSource(Rect Rect, OverflowClipCurve? Curve, OverflowClipBasis? Basis)
        {
            internal static ClipSource Of(CssBox box)
            {
                var borderBox = RenderUtils.ClipSourceBoundsOf(box.Bounds, box.IsInline, box.Rectangles);
                var (clipRect, radii, axisOpen) = RenderUtils.OverflowClipGeometryOf(box, borderBox);

                return new ClipSource(
                    clipRect,
                    radii is { } r ? new OverflowClipCurve(clipRect, r) : null,
                    // An opened-out axis has no padding edge to re-snap, so such a clip carries no basis.
                    axisOpen ? null : new OverflowClipBasis(borderBox, clipRect, Band: null));
            }
        }

        private static List<LineFragment> BuildLines(CssBox box)
        {
            if (box.Rectangles.Count == 0)
            {
                // Mirrors LineFragment's own documented fallback for a block-level box with no line
                // boxes of its own: one LineFragment covering the border box, with a null Line.
                return [new LineFragment(box.Bounds, null, new SliceGeometry(box.Bounds, box.Bounds, true, true))];
            }

            return box.Rectangles
                .Select(kv => new LineFragment(kv.Value, kv.Key, new SliceGeometry(kv.Value, kv.Value, true, true)))
                .ToList();
        }
    }
}
