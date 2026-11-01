using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Fragmentation
{
    /// <summary>
    /// One strip of a run of content that crosses more than one band: which band, where in it the strip
    /// opens, how much of the run it holds, and how far the run's own box is displaced to draw it there.
    /// </summary>
    /// <param name="Slot">the band</param>
    /// <param name="ContentTop">where this band's strip begins, below the room a repeated header takes</param>
    /// <param name="Room">how much of the run this band can hold</param>
    /// <param name="Shift">the accumulated depth of the gaps the repeated groups have opened above it</param>
    internal readonly record struct RunStrip(int Slot, double ContentTop, double Room, double Shift);

    /// <summary>
    /// The page grid as seen from inside a row that the enclosing table slices across bands
    /// (css-tables-3 §6.2: a repeated header and footer take room on every band the table spans).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sliced row is laid out once, continuously, and each band then shows the strip of it that
    /// falls there, displaced down below the repeated header. The row's own coordinates are therefore a
    /// <i>run frame</i>: band <c>k</c> of the run occupies <c>[ContentTop - Shift, ContentTop - Shift +
    /// Room)</c>, which is the real band less the footer's height, with the header's room folded out by
    /// the displacement. A table nested in that row has to break where those strips end, not where the
    /// real pages do, or a row it keeps whole is cut by the slice anyway.
    /// </para>
    /// <para>
    /// <see cref="Strips"/> is the one place the strips are computed: the slicer that states the
    /// displacements and this grid are two readings of the same sequence, so they cannot disagree.
    /// Slots stay the real ones; only the coordinates move into the run frame.
    /// </para>
    /// </remarks>
    internal sealed class RunBandGrid : IPageGrid
    {
        private readonly IPageGrid _parent;
        private readonly int _firstSlot;
        private readonly double _runTop;
        private readonly IEnumerator<RunStrip> _strips;
        private readonly List<double> _bounds = [];

        /// <param name="parent">the grid the sliced row's own table is laid out against</param>
        /// <param name="runTop">where the run begins: the sliced row's top</param>
        /// <param name="headerRoom">what the enclosing table's repeated header takes from every later band</param>
        /// <param name="footerHeight">what the enclosing table's repeated footer takes from every band</param>
        internal RunBandGrid(IPageGrid parent, double runTop, double headerRoom, double footerHeight)
        {
            _parent = parent;
            _runTop = runTop;
            _firstSlot = parent.SlotStartingAt(runTop);
            _strips = Strips(parent, runTop, headerRoom, footerHeight).GetEnumerator();
        }

        /// <summary>
        /// The strips a run beginning at <paramref name="top"/> falls into, band after band, once each
        /// has given the groups the enclosing table repeats their room. Never ends.
        /// </summary>
        /// <remarks>
        /// A band must always take some of the run (css-break-3 §4.3: progress is guaranteed). A band the
        /// groups would leave nothing in gives the run its whole height instead - the trade §4.3 makes
        /// everywhere else.
        /// </remarks>
        /// <param name="grid">the grid whose bands the strips come from</param>
        /// <param name="top">where the run begins</param>
        /// <param name="headerRoom">what a repeated header takes from every band after the first</param>
        /// <param name="footerHeight">what a repeated footer takes from the bottom of every band</param>
        internal static IEnumerable<RunStrip> Strips(IPageGrid grid, double top, double headerRoom, double footerHeight)
        {
            var slot = grid.SlotStartingAt(top);
            var contentTop = top;
            var shift = 0d;

            while (true)
            {
                var bandBottom = grid.PageBottomOf(slot) - footerHeight;
                var room = bandBottom - contentTop;

                if (room <= 0)
                {
                    bandBottom = grid.PageBottomOf(slot);
                    room = bandBottom - contentTop;
                }

                yield return new RunStrip(slot, contentTop, room, shift);

                slot++;

                var resumesAt = grid.PageTopOf(slot) + headerRoom;

                // What the gap between the two strips costs the box that draws them: the run's own
                // coordinates are continuous, so the displacement is what makes the second strip pick up
                // exactly where the first stopped.
                shift += resumesAt - bandBottom;
                contentTop = resumesAt;
            }
        }

        /// <summary>Where run band <paramref name="index"/> begins, in the run frame.</summary>
        private double BoundAt(int index)
        {
            // One strip's end is the next one's start in the run frame, so a band's top is all that is kept.
            while (_bounds.Count <= index)
            {
                _strips.MoveNext();
                _bounds.Add(_strips.Current.ContentTop - _strips.Current.Shift);
            }

            return _bounds[index];
        }

        private int RunIndexOf(double y)
        {
            var index = 0;

            while (y >= BoundAt(index + 1))
            {
                index++;
            }

            return index;
        }

        public double PageTopOf(int slot) =>
            slot <= _firstSlot ? Math.Min(_parent.PageTopOf(slot), _runTop) : BoundAt(slot - _firstSlot);

        public double PageBottomOf(int slot) =>
            slot < _firstSlot ? _parent.PageBottomOf(slot) : BoundAt(slot - _firstSlot + 1);

        public double PageBandHeightOf(int slot) => PageBottomOf(slot) - PageTopOf(slot);

        public int SlotStartingAt(double y) => SlotAt(y + HtmlContainerInt.PageBoundaryEpsilon);

        public int SlotEndingAt(double y) => SlotAt(y - HtmlContainerInt.PageBoundaryEpsilon);

        public PageBand BandOfSlot(int slot) => new(PageTopOf(slot), PageBottomOf(slot));

        private int SlotAt(double y) =>
            y < _runTop && _parent.SlotStartingAt(y) is var before && before < _firstSlot
                ? before
                : _firstSlot + RunIndexOf(y);
    }
}
