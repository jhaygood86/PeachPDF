using PeachDrawing.Core;

namespace PeachPDF.PDF.Drawing
{
    internal sealed class XConicGradientBrush : XBaseGradientBrush
    {
        public XConicGradientBrush(PaintPoint center, double outerRadius, PaintColor[] colors, double[] anglesRad)
            : base(colors[0], colors[colors.Length - 1])
        {
            Center = center;
            OuterRadius = outerRadius;
            Colors = colors;
            AnglesRad = anglesRad;
        }

        internal PaintPoint Center;
        internal double OuterRadius;
        internal PaintColor[] Colors;
        internal double[] AnglesRad; // radians; 0 = 12 o'clock, increases clockwise (CSS convention)
    }
}
