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

using System;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// Border types
    /// </summary>
    internal enum Border
    {
        Top,
        Right,
        Bottom,
        Left
    }

    /// <summary>
    /// Edges whose own border stroke a box must not paint, because another mechanism paints it instead,
    /// or because the box is one CSS 2.1 §17.6 never lets paint a border of its own at all. Set by
    /// <see cref="CssLayoutEngineTable"/>: on every participant of a <c>border-collapse: collapse</c>
    /// table (§17.6.2), the table and its cells stop drawing their own four edges so one resolved border
    /// per grid-line segment can be drawn instead - see <see cref="CssBox.CollapsedBorderSegments"/>. A
    /// row, row group, column or column group is suppressed unconditionally, in EITHER border-collapse
    /// mode - §17.6's "Rows, columns, row groups, and column groups cannot have borders" draws no
    /// distinction between the two models. Distinct from <see cref="CssBox.SuppressOwnBorderPaint"/>,
    /// which is whole-box and also suppresses box-shadow (issue #721) - this is edge-granular and touches
    /// border paint only.
    /// </summary>
    [Flags]
    internal enum BorderEdges
    {
        None = 0,
        Top = 1,
        Right = 2,
        Bottom = 4,
        Left = 8,
        All = Top | Right | Bottom | Left
    }
}