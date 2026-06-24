namespace PeachPDF.PdfSharpCore.Drawing.Layout
{
    internal class FormatterEnvironment
    {
        public XFont Font { get; set; } = null!;
        public XBrush Brush { get; set; } = null!;

        public double LineSpace { get; set; }
        public double CyAscent { get; set; }
        public double CyDescent { get; set; }
        public double SpaceWidth { get; set; }
    }
}