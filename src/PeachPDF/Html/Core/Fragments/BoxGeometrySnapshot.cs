using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
            internal RPoint Location { get; set; }
            internal double ActualRight { get; set; }
            internal double ActualBottom { get; set; }
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
        /// The single root this snapshot was captured from, or null for the multi-root <see cref="Capture(IEnumerable{CssBox}, IReadOnlySet{CssBox}?)"/>
        /// overload. Consulted by <see cref="Translate"/>/<see cref="ReflectSubtree"/> to skip an
        /// out-of-flow descendant whose containing block lies outside this snapshot's own subtree,
        /// mirroring <see cref="CssBox.OffsetTop(double)"/>'s <see cref="CssBox.EscapesTranslationOf"/>
        /// guard - see <see href="https://github.com/jhaygood86/PeachPDF/issues/787">#787</see>. Never
        /// set for the multi-root overload since it is never combined with either method today.
        /// </summary>
        private CssBox? _translationRoot;

        /// <summary>
        /// Captures the current geometry of <paramref name="root"/> and every descendant.
        /// </summary>
        internal static BoxGeometrySnapshot Capture(CssBox root)
        {
            ArgumentNullException.ThrowIfNull(root);

            var snapshot = new BoxGeometrySnapshot { _translationRoot = root };
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
        /// Re-captures <paramref name="subtreeRoot"/> and its descendants' <b>current</b> live geometry,
        /// overwriting whatever this snapshot already held for them.
        /// </summary>
        /// <remarks>
        /// A repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>'s proxy is created - and its own snapshot
        /// captured - before the body row loop runs (<c>CssLayoutEngineTable.LayoutBodyRows</c>'s own
        /// header-proxy-creation step precedes its row loop), so a header cell whose <c>rowspan</c>
        /// crosses into <c>&lt;tbody&gt;</c> (issue #788) is captured at its natural, not-yet-closed height
        /// - the body row loop only stretches it to its real, final extent afterward, through
        /// <c>CloseSpanningCell</c>. Without this, the proxy's own painted snapshot - what
        /// <see cref="Fragmentation.FragmentEmitter"/> actually builds the header's fragments from - shows
        /// the cell cut short at the header's own bottom, even though the live box (what every
        /// <c>CssBox</c>-property test reads) has already been correctly stretched: exactly the
        /// layout/paint divergence this repo's own testing conventions warn a property-only assertion
        /// cannot catch. <see cref="CssLayoutEngineTable.SeedCrossBoundaryRowSpans"/>'s caller resyncs
        /// every header proxy that already exists at the moment such a cell closes; a proxy created
        /// afterward (a later page's repeat) captures the already-closed live geometry directly through
        /// the ordinary <see cref="Capture(CssBox)"/> path and needs no resync of its own.
        /// </remarks>
        internal void Resync(CssBox subtreeRoot) => CaptureBox(subtreeRoot, excluded: null);

        /// <summary>
        /// Whether this snapshot captured <paramref name="box"/> at all — the question "was this box placed
        /// in the fragmentainer this snapshot describes?", which is not the same as whether it has geometry.
        /// </summary>
        internal bool Holds(CssBox box) => _geometry.ContainsKey(box);

        /// <summary>
        /// Shifts every captured box's geometry by <paramref name="dx"/>/<paramref name="dy"/>.
        /// </summary>
        /// <remarks>
        /// A snapshot's own boxes (<see cref="CssProxyBox.SourceBox"/>'s detached subtree) are not part of
        /// the live tree, so a mover that translates the proxy holding this snapshot — a flex item, a
        /// column re-banding pass, a keep-with-next run, table row placement — cannot reach them by walking
        /// <see cref="CssBox.Boxes"/>; <see cref="CssProxyBox.OnTranslated"/> calls this instead, once per
        /// such move (see <see href="https://github.com/jhaygood86/PeachPDF/issues/437">#437</see>).
        /// An out-of-flow descendant whose own containing block lies outside this snapshot's subtree is
        /// skipped, mirroring <see cref="CssBox.OffsetTop(double)"/> (see
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/787">#787</see>) - a flat per-box check
        /// would be wrong here, since an in-flow descendant *inside* a correctly-skipped escaping box must
        /// still be skipped too, not shifted independently; only a real tree walk that stops descending the
        /// instant it finds an escaping box gets that right.
        /// </remarks>
        internal void Translate(double dx, double dy)
        {
            if (_translationRoot is null)
            {
                // Not reached by any call site today - Translate/ReflectSubtree are only ever called on
                // a snapshot captured via the single-root Capture(CssBox), which always sets
                // _translationRoot. Kept as a defensive fallback, not dead code, for the multi-root
                // Capture(IEnumerable<CssBox>, excluded) overload, whose one real caller
                // (CssLayoutEngineColumns.FillColumns) never combines it with either method - a future
                // caller that starts doing so gets this snapshot's pre-#787 behavior (an unconditional
                // shift, no escape-of-subtree guard) rather than a crash, since there is no single root
                // for that guard to walk from; BoxGeometrySnapshotTests.cs exercises this branch directly.
                foreach (var geometry in _geometry.Values)
                {
                    ShiftGeometry(geometry, dx, dy);
                }

                return;
            }

            TranslateSubtree(_translationRoot, dx, dy, _translationRoot);
        }

        private void TranslateSubtree(CssBox box, double dx, double dy, CssBox translationRoot)
        {
            if (_geometry.TryGetValue(box, out var geometry))
            {
                ShiftGeometry(geometry, dx, dy);
            }

            foreach (var child in box.Boxes)
            {
                if (child.EscapesTranslationOf(translationRoot)) continue;

                TranslateSubtree(child, dx, dy, translationRoot);
            }
        }

        /// <summary>
        /// Shifts <paramref name="root"/> and its captured descendants by <paramref name="dx"/> only -
        /// the scoped, single-subtree counterpart of <see cref="Translate"/>, needed when different
        /// subtrees of the same snapshot must move by different amounts.
        /// </summary>
        /// <remarks>
        /// A multi-row <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>'s own internal rows reversing their
        /// row-axis order under <c>vertical-rl</c> is exactly this case - see
        /// <see cref="CssLayoutEngineTable.ReflectRowAxisForVerticalRl"/>, issue
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/784">#784</see>. Only <c>dx</c> (the
        /// row axis for a vertical table) is ever non-zero here - passed as <c>dy = 0</c> into the same
        /// <see cref="ShiftGeometry"/> helper <see cref="Translate"/> uses, so <c>ActualBottom</c> and the
        /// Y component of <c>Rectangles</c>/<c>WordOrigins</c> are left untouched, matching
        /// <see cref="CssBox.OffsetLeft(double)"/>'s own row-axis-only convention: a row-axis reflection
        /// never touches the column axis.
        /// </remarks>
        internal void ReflectSubtree(CssBox root, double dx)
        {
            if (dx == 0) return;

            TranslateSubtree(root, dx, dy: 0, translationRoot: root);
        }

        /// <summary>
        /// Shifts one captured box's own geometry by <paramref name="dx"/>/<paramref name="dy"/> - the
        /// per-box mutation both <see cref="Translate"/> (every captured box, one shared shift) and
        /// <see cref="ReflectSubtree"/> (one subtree, its own shift) apply.
        /// </summary>
        private static void ShiftGeometry(BoxGeometry geometry, double dx, double dy)
        {
            geometry.Location = new RPoint(geometry.Location.X + dx, geometry.Location.Y + dy);
            geometry.ActualRight += dx;
            geometry.ActualBottom += dy;

            foreach (var line in geometry.Rectangles.Keys)
            {
                var r = geometry.Rectangles[line];
                geometry.Rectangles[line] = new RRect(r.X + dx, r.Y + dy, r.Width, r.Height);
            }

            for (var i = 0; i < geometry.WordOrigins.Count; i++)
            {
                if (geometry.WordOrigins[i] is { } origin)
                    geometry.WordOrigins[i] = new RPoint(origin.X + dx, origin.Y + dy);
            }
        }
    }
}
