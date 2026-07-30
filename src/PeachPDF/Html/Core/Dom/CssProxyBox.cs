// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Fragments;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// A proxy box that references an original source box and maintains independent layout state.
    /// Used for repeating table headers and footers across multiple pages.
    /// </summary>
    /// <remarks>
    /// The source box should not be in the document tree. Multiple proxy boxes can reference
    /// the same source box, each maintaining its own layout snapshot.
    /// </remarks>
    internal sealed class CssProxyBox : CssBox
    {
        private readonly CssBox _sourceBox;
        private BoxGeometrySnapshot? _snapshot;

        /// <summary>
        /// This proxy's captured geometry for its <see cref="SourceBox"/> subtree — where that subtree
        /// sits on <i>this</i> proxy's page. <see cref="Fragmentation.FragmentEmitter"/> reads it to build the
        /// repeated header/footer's fragments, since the source subtree is not reachable by walking
        /// <see cref="CssBox.Boxes"/>. Null before this proxy has been laid out.
        /// </summary>
        internal BoxGeometrySnapshot? SourceGeometry => _snapshot;

        /// <summary>
        /// The original box this proxy repeats (a repeating &lt;thead&gt;/&lt;tfoot&gt;, removed
        /// from the live document tree in favor of one proxy per page - see
        /// CssLayoutEngineTable.RemoveHeaderFooterFromTree). Exposed for test inspection of the
        /// source's own row/cell layout, which is otherwise unreachable once removed from the tree.
        /// </summary>
        internal CssBox SourceBox => _sourceBox;

        /// <summary>
        /// The index <see cref="SourceBox"/> occupied among the table's children before it was detached,
        /// or -1 when that was not recorded.
        /// </summary>
        /// <remarks>
        /// The engine is constructed afresh for every layout, and detaching nulls the source's
        /// <see cref="CssBox.ParentBox"/>, so after a run the only thing still referring to the detached
        /// group is its proxies. Restoring the table's own child list therefore has to go through them,
        /// and the index is the one piece of that state nothing else records.
        /// </remarks>
        internal int SourceIndex { get; }

        /// <summary>
        /// Creates a proxy box that references an original source box.
        /// </summary>
        /// <param name="parent">Parent box for this proxy in the document tree</param>
        /// <param name="sourceBox">The original box to proxy (should not be in document tree)</param>
        /// <param name="sourceIndex">
        /// Where <paramref name="sourceBox"/> sat among <paramref name="parent"/>'s children before it was
        /// detached, so a later run of the engine can put it back where it was.
        /// </param>
        public CssProxyBox(CssBox? parent, CssBox sourceBox, int sourceIndex = -1)
        : base(parent, sourceBox.HtmlTag)
        {
            _sourceBox = sourceBox;
            SourceIndex = sourceIndex;

            // Inherit all styles from source
            InheritStyle(sourceBox, everything: true);

            // Explicitly copy critical display properties
            Display = sourceBox.Display;
            Visibility = sourceBox.Visibility;
        }

        /// <summary>
        /// Performs layout by resetting source box, laying it out at this proxy's location,
        /// and capturing the resulting layout state.
        /// </summary>
        protected override async ValueTask PerformLayoutImp(RGraphics g)
        {
#if DEBUG
            System.Console.WriteLine($"CssProxyBox.PerformLayoutImp: START - Location={Location}, Display={Display}");
            System.Console.WriteLine($"  Source already laid out: Location={_sourceBox.Location}, ActualBottom={_sourceBox.ActualBottom}, ActualRight={_sourceBox.ActualRight}");
#endif

            // The source box has already been laid out by the table layout engine
            // We just need to:
            // 1. Position it at our location
            // 2. Capture the snapshot
            // 3. Copy dimensions

            // Update source box location to match proxy location
            var deltaX = this.Location.X - _sourceBox.Location.X;
            var deltaY = this.Location.Y - _sourceBox.Location.Y;

            if (deltaX != 0 || deltaY != 0)
            {
                // Offset the source box and all its children to the proxy's location
                _sourceBox.Location = this.Location;
                foreach (var row in _sourceBox.Boxes)
                {
                    row.OffsetLeft(deltaX);
                    row.OffsetTop(deltaY);
                }
            }

#if DEBUG
            System.Console.WriteLine($"  After positioning: Source.Location={_sourceBox.Location}");
#endif

            // Capture the layout snapshot
            _snapshot = BoxGeometrySnapshot.Capture(_sourceBox);

            // Copy final dimensions from source to this proxy
            ActualBottom = _sourceBox.ActualBottom;
            ActualRight = _sourceBox.ActualRight;
            Size = _sourceBox.Size;

#if DEBUG
            System.Console.WriteLine($"CssProxyBox.PerformLayoutImp: END - Proxy: ActualBottom={ActualBottom}, ActualRight={ActualRight}, Size={Size}");
#endif

            await ValueTask.CompletedTask;
        }

        /// <summary>
        /// Moves the captured <see cref="SourceGeometry"/> along with this proxy. Without this, a mover
        /// that translates a subtree containing this proxy after it has already laid out (a flex item's
        /// final placement, a column re-banding pass, table row placement) would move the proxy's own
        /// <see cref="CssBox.Location"/> via the ordinary <see cref="CssBox.Boxes"/> walk but leave the
        /// frozen snapshot — which <see cref="Fragmentation.FragmentEmitter"/> actually reads to build the
        /// repeated header/footer's fragments — describing the position it was captured at, silently wrong
        /// by however far the move went. See <see href="https://github.com/jhaygood86/PeachPDF/issues/437">#437</see>.
        /// </summary>
        protected override void OnTranslated(double dx, double dy) => _snapshot?.Translate(dx, dy);
    }
}
