using PeachDrawing.Core;
using PeachDrawing.Core.Barcodes;
using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// How a barcode placed with <see cref="IContainer.Barcode"/> (or one of its shorthand methods) looks:
    /// the size of one module, its colors, and the options of the symbology itself. Barcodes are drawn as vector
    /// shapes, so they stay sharp at any zoom.
    /// </summary>
    public sealed class BarcodeStyle
    {
        /// <summary>
        /// The size of one module (the narrowest bar of a linear code, the side of one square of a QR code or Data
        /// Matrix), as an absolute length (not a percentage, <c>em</c> or <c>rem</c>). Defaults to 2pt for QR codes and Data Matrix and 1pt for linear codes. The symbol's whole size
        /// follows from it, including the light margin around it.
        /// </summary>
        public PdfLength? ModuleSize { get; set; }

        /// <summary>The color of the bars and the text. Defaults to black.</summary>
        public PdfColor Color { get; set; } = PdfColor.Black;

        /// <summary>
        /// The color of the light margin and the spaces, or <c>null</c> (the default) to leave them transparent.
        /// Readers need a light surround, so give a background when the page behind the barcode is not light.
        /// </summary>
        public PdfColor? BackgroundColor { get; set; }

        /// <summary>The CSS font family of the human-readable text under a linear code. Defaults to a monospace stack.</summary>
        public string? FontFamily { get; set; }

        /// <summary>
        /// The alternative text of the barcode for assistive technology and tagged PDF. Defaults to the barcode's
        /// own text.
        /// </summary>
        public string? AltText { get; set; }

        /// <summary>
        /// The options of the symbology: error correction level, quiet zone, bar height, whether to print text, and
        /// so on. Changes made here apply to the shorthand methods (<see cref="IContainer.QrCode"/> and friends); a
        /// barcode passed to <see cref="IContainer.Barcode"/> keeps the options it was created with.
        /// </summary>
        public BarcodeOptions Options { get; } = new();

        internal string ToSvg(PeachDrawing.Core.Barcodes.Barcode barcode)
        {
            if (ModuleSize is { Unit: PdfLengthUnit.Percent or PdfLengthUnit.Em or PdfLengthUnit.Rem })
                throw new ArgumentOutOfRangeException(nameof(ModuleSize), "The module size must be an absolute length (pt, px, in, cm, mm or pc).");
            double points = ModuleSize?.ToPoints() ?? (barcode.IsLinear ? 1 : 2);
            if (!(points > 0))
                throw new ArgumentOutOfRangeException(nameof(ModuleSize), "The module size must be positive.");
            // The SVG is sized in CSS pixels; 1px = 0.75pt.
            return BarcodeSvg.ToSvg(barcode, points / 0.75, PaintColor.FromArgb(Color.A, Color.R, Color.G, Color.B),
                BackgroundColor is PdfColor back ? PaintColor.FromArgb(back.A, back.R, back.G, back.B) : null, FontFamily);
        }
    }
}
