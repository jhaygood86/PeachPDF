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
        public double FloatRightStartX => Right - ReferenceWidth - MarginLeft;
    }
}
