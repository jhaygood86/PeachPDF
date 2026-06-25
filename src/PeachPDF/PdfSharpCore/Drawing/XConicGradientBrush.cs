namespace PeachPDF.PdfSharpCore.Drawing
{
    internal sealed class XConicGradientBrush : XBaseGradientBrush
    {
        public XConicGradientBrush(XPoint center, double outerRadius, XColor[] colors, double[] anglesRad)
            : base(colors[0], colors[colors.Length - 1])
        {
            Center = center;
            OuterRadius = outerRadius;
            Colors = colors;
            AnglesRad = anglesRad;
        }

        internal XPoint Center;
        internal double OuterRadius;
        internal XColor[] Colors;
        internal double[] AnglesRad; // radians; 0 = 12 o'clock, increases clockwise (CSS convention)
    }
}
