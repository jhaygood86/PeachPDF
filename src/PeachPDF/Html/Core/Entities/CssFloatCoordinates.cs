using System;

namespace PeachPDF.Html.Core.Entities
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

        /// <summary>The margin-edge top the vertical-conflict test reads (CSS 2.1 §9.5.1 rule 5 compares outer tops).</summary>
        public double OuterTop => Top + Math.Max(0, -MarginTop);
        public double FloatRightStartX => Right - ReferenceWidth;
    }
}
