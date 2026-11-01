namespace PeachDrawing.Core
{
    /// <summary>
    /// Where something sits along one axis of a box: see <see cref="Canvas.DrawStringInRect"/>.
    /// </summary>
    public enum BoxAlignment
    {
        /// <summary>At the start edge: the left edge horizontally, the top edge vertically.</summary>
        Start,
        /// <summary>Centered between the two edges.</summary>
        Center,
        /// <summary>At the end edge: the right edge horizontally, the bottom edge vertically.</summary>
        End,
    }
}
