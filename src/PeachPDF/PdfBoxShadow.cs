using System.Globalization;

namespace PeachPDF
{
    /// <summary>One <c>box-shadow</c> layer for the declarative document-building API (<see cref="PdfGenerator.CreateDocument"/>).</summary>
    public readonly struct PdfBoxShadow
    {
        /// <summary>The shadow's color.</summary>
        public PdfColor Color { get; }

        /// <summary>The horizontal offset (positive moves the shadow right).</summary>
        public PdfLength OffsetX { get; }

        /// <summary>The vertical offset (positive moves the shadow down).</summary>
        public PdfLength OffsetY { get; }

        /// <summary>The blur radius (0 for a hard-edged shadow).</summary>
        public PdfLength Blur { get; }

        /// <summary>The spread distance (grows the shadow's shape before blurring; negative shrinks it).</summary>
        public PdfLength Spread { get; }

        /// <summary>Whether this shadow is drawn inside the border edge instead of outside it.</summary>
        public bool Inset { get; }

        /// <summary>Creates a box-shadow layer.</summary>
        public PdfBoxShadow(PdfColor color, PdfLength offsetX, PdfLength offsetY, PdfLength blur = default, PdfLength spread = default, bool inset = false)
        {
            Color = color;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Blur = blur;
            Spread = spread;
            Inset = inset;
        }

        /// <summary>Formats this shadow as one canonical <c>box-shadow</c> layer's CSS text.</summary>
        internal string ToCssText() => string.Create(CultureInfo.InvariantCulture,
            $"{(Inset ? "inset " : "")}{OffsetX.ToCssText()} {OffsetY.ToCssText()} {Blur.ToCssText()} {Spread.ToCssText()} {Color.ToCssText()}");
    }
}
