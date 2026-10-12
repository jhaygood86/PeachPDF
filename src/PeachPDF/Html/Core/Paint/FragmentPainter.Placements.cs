using PeachDrawing;
using PeachPDF.Html.Core.Fragments;
using System.Numerics;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Where a box was drawn, for what is written over it afterwards: the link annotation and form widget of an element that a
    /// <c>transform</c> moved (css-transforms-1 "Transform Rendering") belong where it was drawn, not at the rectangle layout gave it.
    /// </summary>
    /// <remarks>
    /// The painter tracks the cumulative map of the transforms it has entered - an element's matrix, the projective warp of a
    /// <c>perspective</c> element, a 3D rendering context's plane - in the fragmentainer-local layout space every fragment rectangle is in,
    /// so the map of a box is the one that takes its layout rectangle to the page. Entering a transform composes the element's own map in
    /// front of the enclosing one, because a point is the element's first and its ancestors' last. Nothing is recorded while the map is the
    /// identity, so a document with no transform pays for the bookkeeping only in two assignments per element.
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        /// <summary>The map from the local space of the fragment being painted to the fragmentainer's, valid while <see cref="_mapped"/>.</summary>
        private Homography _toPage = Homography.Identity;

        /// <summary>Whether a transform has been entered, so <see cref="_toPage"/> is not the identity.</summary>
        private bool _mapped;

        /// <summary>The pagination slot of the page being painted, or -1 for a tree that has none (a margin box's running element).</summary>
        private int _slot = -1;

        /// <summary>
        /// Whether this painter's own walk is the one whose placements are read: the page paint and a detached tree's, not the mirror painters
        /// that repaint part of a tree into a bitmap, which start from the identity wherever they start and would record the wrong map.
        /// </summary>
        private bool _recordsPlacements;

        private readonly record struct MapState(Homography Map, bool Mapped);

        /// <summary>Enters a transform whose own map is <paramref name="own"/>; the returned state restores the enclosing one with <see cref="LeaveMap"/>.</summary>
        private MapState EnterMap(in Homography own)
        {
            var saved = new MapState(_toPage, _mapped);
            _toPage = _mapped ? Homography.Then(own, _toPage) : own;
            _mapped = true;
            return saved;
        }

        private void LeaveMap(MapState saved) => (_toPage, _mapped) = (saved.Map, saved.Mapped);

        /// <summary>The homography of a 2D transform, in the row-vector convention <see cref="Matrix3x2"/> uses.</summary>
        private static Homography HomographyOf(in Matrix3x2 matrix) =>
            new(matrix.M11, matrix.M21, matrix.M31, matrix.M12, matrix.M22, matrix.M32, 0, 0, 1);

        /// <summary>
        /// Notes where <paramref name="fragment"/> is being drawn if something can be written over it and the map in force is not the
        /// identity. Not recorded by the text-only pass, which draws the same text again through a linearisation of the same map.
        /// </summary>
        private void RecordPlacement(BoxFragment fragment)
        {
            if (!_mapped || !_recordsPlacements || _textOnly || !Handlers.BoxPlacements.IsAnnotationTarget(fragment.Box))
                return;

            (fragment.Box.HtmlContainer ?? container).Placements.Note(fragment, _slot, _toPage);
        }

        /// <summary>
        /// Notes that <paramref name="fragment"/> and everything under it was not drawn on this page (a projected plane wholly outside the clip),
        /// so the annotation pass leaves them out here and does not read "not recorded" as "not moved". Per page, unlike
        /// <see cref="NoteNotRendered"/>: a box split over pages is hidden only where it is.
        /// </summary>
        private void RecordHiddenSubtree(BoxFragment fragment)
        {
            if (!_recordsPlacements || _textOnly)
                return;

            if (Handlers.BoxPlacements.IsAnnotationTarget(fragment.Box))
                (fragment.Box.HtmlContainer ?? container).Placements.Note(fragment, _slot, Homography.Identity, hidden: true);

            foreach (var child in fragment.Children)
                RecordHiddenSubtree(child);
        }
    }
}
