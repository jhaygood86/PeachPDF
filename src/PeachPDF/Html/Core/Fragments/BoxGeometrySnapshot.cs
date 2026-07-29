using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachPDF.Html.Core.Fragments
{
    /// <summary>
    /// One box subtree's laid-out geometry, captured so the same boxes can be positioned more than
    /// once in a document — the case a repeating table <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
    /// creates, where one source subtree is shown at a different place on every page.
    /// </summary>
    /// <remarks>
    /// This is the narrow, pre-existing precedent for the fragment tree: "one box, N positions".
    /// <see cref="Fragmentation.FragmentEmitter"/> reads a snapshot to build the source subtree's fragments at
    /// each proxy's own position, which is what lets a repeated header appear in the fragment tree at
    /// all — <see cref="CssProxyBox.SourceBox"/> is deliberately not part of the live box tree.
    /// </remarks>
    internal sealed class BoxGeometrySnapshot
    {
        /// <summary>
        /// One box's captured geometry. <see cref="Bounds"/> is reconstructed from the same
        /// location/right/bottom triple <see cref="CssBox.Bounds"/> derives from, so a
        /// snapshot describes exactly what the live box would have reported at capture time.
        /// </summary>
        internal sealed class BoxGeometry
        {
            internal RPoint Location { get; init; }
            internal double ActualRight { get; init; }
            internal double ActualBottom { get; init; }
            internal Dictionary<CssLineBox, RRect> Rectangles { get; } = [];

            /// <summary>
            /// Where each of the box's words sat, or null for one that belongs to the <i>next</i>
            /// fragmentainer.
            /// </summary>
            /// <remarks>
            /// A line box never straddles a fragmentainer (css-break-3 §4.1), so a flow that stops mid-line
            /// discards the line being built — but its words keep the position of that abandoned attempt
            /// until the resumed pass re-places them. Captured as-is they are ghosts at the foot of the
            /// fragmentainer they had left, which is exactly what
            /// <see cref="CssRect.AwaitsTheNextFragmentainer"/> exists to say. It cannot be read at emission
            /// time instead: by then the resumed pass has re-placed the word and cleared the flag, while this
            /// snapshot still holds where it used to be.
            /// </remarks>
            internal List<RPoint?> WordOrigins { get; } = [];

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
            snapshot.CaptureBox(root, excluded: null);
            return snapshot;
        }

        /// <summary>
        /// Captures the current geometry of each of <paramref name="roots"/> and their descendants, minus
        /// the subtrees rooted at <paramref name="excluded"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The subtree-set form exists for a nested fragmentainer, which holds <i>some</i> of a container's
        /// children rather than all of them: the ones a column's fill never reached still carry the
        /// measurement pass's geometry, and capturing that would describe content in a column it is not in.
        /// </para>
        /// <para>
        /// <paramref name="excluded"/> says the same thing about content <i>inside</i> a root. A break can
        /// be raised below a container's own child, which leaves that child in this fragmentainer while
        /// everything from the break onward belongs to the next one — so the root is captured and the walk
        /// stops at each excluded box.
        /// </para>
        /// <para>
        /// <b>A root is never itself excluded</b>, which is why only descendants are tested. The two sets
        /// are built from one break record by the same caller
        /// (<c>CssLayoutEngineColumns.FillColumns</c>): the roots stop <i>below</i> the box the break falls
        /// before (<c>ChildrenIn</c>'s upper bound is <c>PlacedBelow</c>), and <paramref name="excluded"/>
        /// starts <i>at</i> it (<c>BeyondThisColumn</c>'s lower bound), so the two ranges abut and cannot
        /// overlap. That is a fact about the caller rather than about this method, so it is asserted here
        /// in <c>DEBUG</c> rather than assumed silently.
        /// </para>
        /// </remarks>
        internal static BoxGeometrySnapshot Capture(IEnumerable<CssBox> roots, IReadOnlySet<CssBox>? excluded = null)
        {
            ArgumentNullException.ThrowIfNull(roots);

            var snapshot = new BoxGeometrySnapshot();

            foreach (var root in roots)
            {
                Debug.Assert(excluded is null || !excluded.Contains(root),
                    "A captured root is also excluded: the caller's two ranges have come to overlap.");

                snapshot.CaptureBox(root, excluded);
            }

            return snapshot;
        }

        private void CaptureBox(CssBox box, IReadOnlySet<CssBox>? excluded)
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
                geometry.WordOrigins.Add(
                    word.AwaitsTheNextFragmentainer ? null : new RPoint(word.Left, word.Top));
            }

            _geometry[box] = geometry;

            foreach (var childBox in box.Boxes)
            {
                if (excluded is not null && excluded.Contains(childBox)) continue;

                CaptureBox(childBox, excluded);
            }
        }

        internal bool TryGetGeometry(CssBox box, out BoxGeometry geometry) => _geometry.TryGetValue(box, out geometry!);

        /// <summary>
        /// Whether this snapshot captured <paramref name="box"/> at all — the question "was this box placed
        /// in the fragmentainer this snapshot describes?", which is not the same as whether it has geometry.
        /// </summary>
        internal bool Holds(CssBox box) => _geometry.ContainsKey(box);
    }
}
