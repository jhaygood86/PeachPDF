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
        /// Creates a proxy box that references an original source box.
        /// </summary>
        /// <param name="parent">Parent box for this proxy in the document tree</param>
        /// <param name="sourceBox">The original box to proxy (should not be in document tree)</param>
        public CssProxyBox(CssBox? parent, CssBox sourceBox)
        : base(parent, sourceBox.HtmlTag)
        {
            _sourceBox = sourceBox;

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
    }
}
