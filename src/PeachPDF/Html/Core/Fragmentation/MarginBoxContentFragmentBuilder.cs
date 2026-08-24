using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
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
        internal static BoxFragment Build(CssBox box)
        {
            var lines = BuildLines(box);
            var words = box.Words.Select(w => new TextFragment(w.Rectangle, w)).ToList();
            var children = box.Boxes
                .Where(b => b.DerivedStyle.ActualDisplay != Keywords.None && !b.IsOutOfFlow && !b.IsRunningPositioned)
                .Select(Build)
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
                OverflowClip: null,
                OverflowClipCurve: null); // a running element's content is never clipped
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
