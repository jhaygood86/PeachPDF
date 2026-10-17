using System;

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

        public double FloatRightStartX => Right - ReferenceWidth;
    }
}
