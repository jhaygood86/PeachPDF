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

namespace PeachPDF.Html.Core.Dom
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
    /// Edges whose own border stroke a box must not paint, because another mechanism paints it instead.
    /// Set by <see cref="CssLayoutEngineTable"/> on every participant of a <c>border-collapse: collapse</c>
    /// table (CSS 2.1 §17.6.2): the table, its rows, its row groups and its cells each stop drawing their
    /// own four edges, and one resolved border per grid-line segment is drawn instead - see
    /// <see cref="CssBox.CollapsedBorderSegments"/>. Distinct from <see cref="CssBox.SuppressOwnBorderPaint"/>,
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