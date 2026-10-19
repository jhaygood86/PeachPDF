using PeachDrawing;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// What the painter did with a box that an annotation can sit over, when an ancestor's (or its own) <c>transform</c> moved it away from the
    /// rectangle layout gave it (css-transforms-1 "Transform Rendering"): the map it was drawn through, or that it was not drawn on that page.
    /// </summary>
    /// <param name="Map">takes the fragmentainer-local layout coordinates (the space <see cref="BoxFragment"/> rectangles are in) to where they landed on the page, in the same space</param>
    /// <param name="Hidden">nothing of the box was drawn on this page (a projected plane wholly outside the clip), so nothing is there to click; <paramref name="Map"/> is meaningless</param>
    internal readonly record struct Placement(Homography Map, bool Hidden);

    /// <summary>
    /// Where the painter drew the boxes that annotations are written over - a link, a form control, an SVG that holds links - when a transform
    /// moved them.
    /// </summary>
    /// <remarks>
    /// The painter is the one place that knows what it did: the cumulative matrix of every transformed ancestor with each
    /// <c>transform-origin</c> resolved against its own box, the projective warp of a <c>perspective</c> element, a 3D rendering context's
    /// accumulated plane. The annotation pass reads the result instead of working it out again from the box tree, the way it already reads
    /// <see cref="HtmlContainerInt.NoteNotRendered"/>, so the two cannot disagree. Only boxes drawn under a non-identity map are recorded; a
    /// box that is not here is where layout put it. Unlike <c>NoteNotRendered</c>, which is per box and so per document, this is per page
    /// slot: a box split over pages can be hidden on one and drawn on another.
    /// </remarks>
    internal sealed class BoxPlacements
    {
        private readonly Dictionary<CssBox, List<Entry>> _byBox = new(ReferenceEqualityComparer.Instance);

        private readonly record struct Entry(BoxFragment Fragment, int Slot, Placement Placement);

        /// <summary>Whether the painter has any reason to record <paramref name="box"/>: an annotation can be written over it.</summary>
        internal static bool IsAnnotationTarget(CssBox box) =>
            box.IsClickable || box is CssBoxFormField or CssBoxSvg or CssBoxImage || box.DisplayContentsAncestors is not null;

        /// <summary>
        /// Records that <paramref name="fragment"/> was drawn through <paramref name="map"/>, or - <paramref name="hidden"/> - not drawn at all.
        /// </summary>
        /// <param name="fragment">the fragment that was painted</param>
        /// <param name="slot">the pagination slot it was painted on, or -1 for content that has no slot (a margin box's running element)</param>
        /// <param name="map">the cumulative map</param>
        /// <param name="hidden">whether nothing of the fragment was drawn</param>
        internal void Note(BoxFragment fragment, int slot, in Homography map, bool hidden = false)
        {
            var box = fragment.Box;
            if (!_byBox.TryGetValue(box, out var entries))
                _byBox[box] = entries = new List<Entry>(1);

            var entry = new Entry(fragment, slot, new Placement(map, hidden));

            // The painter's own _painted set stops a fragment being painted twice, so each is noted once; one layout and paint per container,
            // so nothing is ever cleared.
            entries.Add(entry);
        }

        /// <summary>What the painter did with the fragment of <paramref name="box"/> on pagination slot <paramref name="slot"/>, if it was drawn under a transform.</summary>
        internal bool TryGet(CssBox box, int slot, out Placement placement)
        {
            if (_byBox.TryGetValue(box, out var entries))
            {
                foreach (var entry in entries)
                {
                    if (entry.Slot == slot)
                    {
                        placement = entry.Placement;
                        return true;
                    }
                }
            }

            placement = default;
            return false;
        }

        /// <summary>What the painter did with <paramref name="fragment"/> itself, if it was drawn under a transform.</summary>
        internal bool TryGet(BoxFragment fragment, out Placement placement)
        {
            if (_byBox.TryGetValue(fragment.Box, out var entries))
            {
                foreach (var entry in entries)
                {
                    if (ReferenceEquals(entry.Fragment, fragment))
                    {
                        placement = entry.Placement;
                        return true;
                    }
                }
            }

            placement = default;
            return false;
        }
    }
}
