using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// One box subtree's laid-out geometry, captured so the same boxes can be positioned more than
    /// once in a document — the case a repeating table <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
    /// creates, where one source subtree is shown at a different place on every page.
    /// </summary>
    /// <remarks>
    /// This is the narrow, pre-existing precedent for the fragment tree: "one box, N positions".
    /// <see cref="FragmentTreeBuilder"/> reads a snapshot to build the source subtree's fragments at
    /// each proxy's own position, which is what lets a repeated header appear in the fragment tree at
    /// all — <see cref="CssProxyBox.SourceBox"/> is deliberately not part of the live box tree.
    /// </remarks>
    internal sealed class BoxGeometrySnapshot
    {
        /// <summary>
        /// One box's captured geometry. <see cref="Bounds"/> is reconstructed from the same
        /// location/right/bottom triple <see cref="CssBoxProperties.Bounds"/> derives from, so a
        /// snapshot describes exactly what the live box would have reported at capture time.
        /// </summary>
        internal sealed class BoxGeometry
        {
            internal RPoint Location { get; init; }
            internal double ActualRight { get; init; }
            internal double ActualBottom { get; init; }
            internal Dictionary<CssLineBox, RRect> Rectangles { get; } = [];
            internal List<RPoint> WordOrigins { get; } = [];

            internal RRect Bounds => RRect.FromLTRB(Location.X, Location.Y, ActualRight, ActualBottom);
        }

        private readonly Dictionary<CssBox, BoxGeometry> _geometry = [];

        /// <summary>
        /// Captures the current geometry of <paramref name="root"/> and every descendant.
        /// </summary>
        internal static BoxGeometrySnapshot Capture(CssBox root)
        {
            ArgumentNullException.ThrowIfNull(root);

            var snapshot = new BoxGeometrySnapshot();
            snapshot.CaptureBox(root);
            return snapshot;
        }

        private void CaptureBox(CssBox box)
        {
            var geometry = new BoxGeometry
            {
                Location = box.Location,
                ActualRight = box.ActualRight,
                ActualBottom = box.ActualBottom
            };

            foreach (var (line, rect) in box.Rectangles)
            {
                geometry.Rectangles[line] = rect;
            }

            foreach (var word in box.Words)
            {
                geometry.WordOrigins.Add(new RPoint(word.Left, word.Top));
            }

            _geometry[box] = geometry;

            foreach (var childBox in box.Boxes)
            {
                CaptureBox(childBox);
            }
        }

        internal bool TryGetGeometry(CssBox box, out BoxGeometry geometry) => _geometry.TryGetValue(box, out geometry!);

    }
}
