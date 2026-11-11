using System;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html.Entities
{
    internal record CssFloatCoordinates
    {
        public required double Left { get; set; }
        public required double Right { get; set; }
        public required double Top { get; set; }
        public required double MaxBottom { get; set; }
        public required double MarginLeft { get; set; }
        public required double MarginRight { get; set; }
        public required double ReferenceWidth { get; set; }

        /// <summary>The floating box's own top margin. Only a negative one matters: it puts the border-box <see cref="Top"/> above the margin edge the float has to clear.</summary>
        public double MarginTop { get; init; }

        private double? _outerTop;

        /// <summary>
        /// The margin-edge top the vertical-conflict test reads (CSS 2.1 §9.5.1 rule 5 compares outer tops). Unless
        /// the caller states it, it is the border-box <see cref="Top"/> with a negative <see cref="MarginTop"/> taken
        /// back out. Once the float has been moved by <see cref="DropTo"/> it is the edge the drop cleared.
        /// </summary>
        public double OuterTop
        {
            get => _outerTop ?? Top + Math.Max(0, -MarginTop);
            init => _outerTop = value;
        }

        /// <summary>
        /// The smallest bottom margin edge among the blockers met since the float was last moved, or positive infinity when it has met none. A float
        /// that does not fit beside them goes to the first of these that makes room (CSS 2.1 §9.5.1 rules 7 and 8: as high as possible), not below the
        /// lowest, which <see cref="MaxBottom"/> is.
        /// </summary>
        public double NextBottom { get; set; } = double.PositiveInfinity;

        /// <summary>
        /// The edge the float goes below when it does not fit where it is: the first blocker bottom that frees some room, which is always below the
        /// float's outer top (so the move makes progress), else the lowest one met.
        /// </summary>
        public double DropEdge => NextBottom > OuterTop + 0.01 && NextBottom <= MaxBottom ? NextBottom : MaxBottom;

        /// <summary>
        /// Moves the float below <paramref name="clearEdge"/>, the lowest margin edge it has to clear. The border box
        /// starts <see cref="MarginTop"/> past it, so a negative margin-top leaves the border box above the edge while
        /// the margin box starts at it. <see cref="OuterTop"/> is what the conflict test reads, and it is never above
        /// <paramref name="clearEdge"/> afterwards, so a blocker the float has been moved below cannot be found again.
        /// A search that drops the float repeatedly therefore always moves further down the page, whatever the margins
        /// of the floats involved, and ends.
        /// </summary>
        public void DropTo(double clearEdge)
        {
            Top = clearEdge + MarginTop;
            _outerTop = Top + Math.Max(0, -MarginTop);
        }

        /// <summary>The right edge of the float's containing block (the column, in a multi-column container).</summary>
        public double ContainingRight { get; init; } = double.PositiveInfinity;

        /// <summary>
        /// The right edge of the nearest enclosing multi-column container, or negative infinity outside one.
        /// A right float starting between <see cref="ContainingRight"/> and this lies in another column, which
        /// is not the float's containing block, so it cannot be a blocker (CSS 2.1 §9.5.1 rule 7; css-multicol-1
        /// §2: a float belongs to the column box it appears in).
        /// </summary>
        public double MulticolRight { get; init; } = double.NegativeInfinity;

        /// <summary>
        /// Set by the line-flow cursor lookup, which asks only where a <i>left</i> float pushes the cursor. A right
        /// float is the lookahead limit's business: reported here as well, one whose left edge meets the cursor
        /// exactly (two floats leaving no room between them) pushed the cursor across it to its far edge.
        /// </summary>
        public bool LeftFloatsOnly { get; init; }

        /// <summary>The box whose lines a <see cref="LeftFloatsOnly"/> lookup is made for: the frame the floats found are compared in.</summary>
        public CssBox? Reader { get; init; }

        /// <summary>
        /// Set by the line-flow cursor lookup when the cursor is at the line's start (kept on across the whole chain of hops): a left float that
        /// overlaps the line vertically bounds it by its right edge whether or not it starts at the cursor (CSS 2.1 §9.5: a line box beside a
        /// float is shortened to the float's margin box). Without it a float that begins right of the line start - the second of two floats side
        /// by side once the first has ended on an earlier page - is not seen, and the line runs over it.
        /// </summary>
        public bool SeekPastGap { get; init; }

        public double FloatRightStartX => Right - ReferenceWidth;

        /// <summary>
        /// Set by the blocker search to how far the blocker it returned (and the boxes it lies in) has been moved by relative positioning,
        /// which is visual only (CSS 2.1 §9.4.3): the blocker's static edges are its coordinates less these.
        /// </summary>
        public double BlockerOffsetX { get; set; }

        /// <inheritdoc cref="BlockerOffsetX"/>
        public double BlockerOffsetY { get; set; }
    }
}
