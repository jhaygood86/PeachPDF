using System.Collections.Generic;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// A piece of human-readable text that goes with a barcode, in the units of its <see cref="BarcodeLayout"/>.
    /// </summary>
    /// <param name="Text">The text.</param>
    /// <param name="Bounds">The box the text is centered in horizontally and starts at the top of.</param>
    /// <param name="FontSize">The font size, in the same units.</param>
    public readonly record struct BarcodeText(string Text, Rect Bounds, double FontSize);

    /// <summary>
    /// A barcode measured out in modules: the dark bars as rectangles with their margins, and the text with them.
    /// One module is one unit; scale and move the whole layout to draw it.
    /// </summary>
    public sealed class BarcodeLayout
    {
        internal BarcodeLayout(double width, double height, Rect symbol, IReadOnlyList<Rect> bars, IReadOnlyList<BarcodeText> texts)
        {
            Width = width;
            Height = height;
            SymbolBounds = symbol;
            Bars = bars;
            Texts = texts;
        }

        /// <summary>The width including the light margin.</summary>
        public double Width { get; }

        /// <summary>The height including the light margin and the text.</summary>
        public double Height { get; }

        /// <summary>The bounds of the symbol itself, without the light margin.</summary>
        public Rect SymbolBounds { get; }

        /// <summary>The dark parts, with the horizontal and vertical runs of modules already merged. They never overlap.</summary>
        public IReadOnlyList<Rect> Bars { get; }

        /// <summary>The human-readable text; empty for the matrix symbologies or when text is switched off.</summary>
        public IReadOnlyList<BarcodeText> Texts { get; }
    }
}
