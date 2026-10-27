namespace PeachBrackets.Html.Fragmentation
{
    /// <summary>
    /// The questions the table engine asks of a page grid: where a band begins and ends, and which band
    /// a coordinate falls in.
    /// </summary>
    /// <remarks>
    /// <see cref="HtmlContainerInt"/> answers them for the document's real pages. A table nested in a
    /// row that the enclosing table slices across several bands (<see cref="RunBandGrid"/>) is laid out
    /// in that row's continuous coordinates, where the bands are the strips the row will be cut into -
    /// so the engine asks this interface rather than the container, and the same break logic is right in
    /// both frames.
    /// </remarks>
    internal interface IPageGrid
    {
        /// <summary>Where <paramref name="slot"/>'s band begins.</summary>
        double PageTopOf(int slot);

        /// <summary>Where <paramref name="slot"/>'s band ends.</summary>
        double PageBottomOf(int slot);

        /// <summary>How tall <paramref name="slot"/>'s band is.</summary>
        double PageBandHeightOf(int slot);

        /// <summary>The slot a top edge at <paramref name="y"/> begins in.</summary>
        int SlotStartingAt(double y);

        /// <summary>The slot a bottom edge at <paramref name="y"/> ends in.</summary>
        int SlotEndingAt(double y);

        /// <summary>The block-axis band <paramref name="slot"/> occupies.</summary>
        PageBand BandOfSlot(int slot);
    }
}
