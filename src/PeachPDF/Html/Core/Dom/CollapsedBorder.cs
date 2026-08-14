using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS 2.1 §17.6.2's border-conflict origin priority, most specific first. A lower value wins a
    /// tie in <see cref="CollapsedBorderResolver"/> once width and style priority have already tied.
    /// </summary>
    internal enum CollapsedBorderOrigin
    {
        Cell = 0,
        Row = 1,
        RowGroup = 2,
        Column = 3,
        ColumnGroup = 4,
        Table = 5
    }

    /// <summary>
    /// One border declaration competing for a single grid-line segment - one box's style/width/color on
    /// one of its four edges, tagged with where it came from (for the origin-priority tiebreak) and its
    /// position in the grid (for the final leftmost/topmost tiebreak).
    /// </summary>
    /// <param name="Style">The declaring box's border style on this edge.</param>
    /// <param name="Width">The declaring box's actual border width on this edge.</param>
    /// <param name="Color">The declaring box's actual border color on this edge.</param>
    /// <param name="Origin">Where this declaration comes from, for the origin-priority tiebreak.</param>
    /// <param name="Row">
    /// The grid row of the declaring element - a cell's own row, a row-group's first row, or 0 for a
    /// column/column-group/table declaration (which have no row of their own).
    /// </param>
    /// <param name="Column">
    /// The grid column of the declaring element - a cell's own starting column, a column/column-group's
    /// first column, or 0 for a row/row-group/table declaration.
    /// </param>
    internal readonly record struct CollapsedBorderCandidate(
        LineStyle Style,
        double Width,
        RColor Color,
        CollapsedBorderOrigin Origin,
        int Row,
        int Column);

    /// <summary>
    /// The single border CSS 2.1 §17.6.2 resolved for one grid-line segment.
    /// </summary>
    internal readonly record struct CollapsedBorder(LineStyle Style, double Width, RColor Color)
    {
        /// <summary>No candidate contributed here, or every candidate was <c>none</c>.</summary>
        internal static readonly CollapsedBorder None = new(LineStyle.None, 0, RColor.Empty);

        /// <summary>
        /// The room this border occupies on the grid line - zero for <see cref="LineStyle.None"/> and
        /// <see cref="LineStyle.Hidden"/> alike, since a suppressed edge reserves no space either.
        /// </summary>
        internal double UsedWidth => Style is LineStyle.None or LineStyle.Hidden ? 0 : Width;

        /// <summary>Whether this border actually draws anything.</summary>
        internal bool IsPainted => UsedWidth > 0;
    }

    /// <summary>
    /// One resolved, drawable run along a grid line - the <c>ColumnRuleSegments</c>-style paint-time
    /// carrier <see cref="CssBox.CollapsedBorderSegments"/> holds, consumed by <c>FragmentPainter</c>
    /// after every table-internal background has painted (the fix for issue #735: painting late, once,
    /// is what stops a later row's background from erasing an earlier row's shared border).
    /// </summary>
    /// <param name="IsHorizontal">Whether this segment runs along a horizontal (row) grid line rather than a vertical (column) one.</param>
    /// <param name="Rect">The segment's own bounding rect, in the same fragment-local document space <c>ColumnRuleSegments</c> uses.</param>
    /// <param name="Style">The resolved border style.</param>
    /// <param name="Width">The resolved border width.</param>
    /// <param name="Color">The resolved border color.</param>
    internal readonly record struct CollapsedBorderSegment(
        bool IsHorizontal,
        RRect Rect,
        LineStyle Style,
        double Width,
        RColor Color);
}
