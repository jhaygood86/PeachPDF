#nullable disable

using System.Collections.Generic;
using System.Linq;
using PeachDrawing.Core.Barcodes;

namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary>
    /// The one grammar of <c>-peachpdf-barcode( &lt;symbology&gt;, &lt;string&gt; [, &lt;color&gt; [, &lt;color&gt; ]] )</c>, shared by the
    /// CSS-OM validation (<see cref="BarcodeImageConverter"/>) and the render-layer resolution (<c>CssValueParser.ParseImage</c>):
    /// the symbology keywords and the creation of the barcode; the arguments' values are then read by each layer's own color and
    /// string parsing.
    /// </summary>
    internal static class BarcodeImageGrammar
    {
        public static bool TryParseSymbology(string keyword, out BarcodeSymbology symbology)
        {
            switch (keyword.Trim().ToLowerInvariant())
            {
                case "qr": symbology = BarcodeSymbology.QrCode; return true;
                case "data-matrix": symbology = BarcodeSymbology.DataMatrix; return true;
                case "code128": symbology = BarcodeSymbology.Code128; return true;
                case "gs1-128": symbology = BarcodeSymbology.Gs1128; return true;
                case "ean13": symbology = BarcodeSymbology.Ean13; return true;
                case "ean8": symbology = BarcodeSymbology.Ean8; return true;
                case "upca": symbology = BarcodeSymbology.UpcA; return true;
                default: symbology = default; return false;
            }
        }

        /// <summary>The barcode for the symbology and text, or null when the text is not valid for it.</summary>
        public static Barcode TryCreate(BarcodeSymbology symbology, string text)
        {
            try
            {
                return symbology switch
                {
                    BarcodeSymbology.QrCode => Barcode.CreateQrCode(text),
                    BarcodeSymbology.DataMatrix => Barcode.CreateDataMatrix(text),
                    BarcodeSymbology.Code128 => Barcode.CreateCode128(text),
                    BarcodeSymbology.Gs1128 => Barcode.CreateGs1128(text),
                    BarcodeSymbology.Ean13 => Barcode.CreateEan13(text),
                    BarcodeSymbology.Ean8 => Barcode.CreateEan8(text),
                    _ => Barcode.CreateUpcA(text),
                };
            }
            catch (System.Exception ex) when (ex is System.ArgumentException or System.InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// <c>-peachpdf-barcode( &lt;symbology&gt;, &lt;string&gt; [, &lt;color&gt; [, &lt;color&gt; ]] )</c>: a barcode as an <c>&lt;image&gt;</c>. The
    /// symbology is <c>qr</c>, <c>data-matrix</c>, <c>code128</c>, <c>gs1-128</c>, <c>ean13</c>, <c>ean8</c> or <c>upca</c>; the optional
    /// colors are the bars and the light surround. Validates the grammar only; <c>CssValueParser.ParseImage</c> turns it into the
    /// vector image.
    /// </summary>
    internal sealed class BarcodeImageConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var args = value.ToList(); // comma-separated groups
            if (args.Count is < 2 or > 4) return null;

            var first = args[0].Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (first is not [{ Type: TokenType.Ident } ident] || !BarcodeImageGrammar.TryParseSymbology(ident.Data.ToString(), out _)) return null;

            if (StringConverter.Convert(args[1]) == null) return null;

            for (var i = 2; i < args.Count; i++)
                if (ColorConverter.Convert(args[i]) == null || IsCurrentColor(args[i])) return null;

            return new ImageFunctionValue(value);
        }

        // The image has no element to take a color from, so currentcolor would mean nothing here.
        private static bool IsCurrentColor(List<Token> tokens)
            => tokens.Any(t => t.Type == TokenType.Ident && t.Data.ToString().Equals("currentcolor", System.StringComparison.OrdinalIgnoreCase));

        public IPropertyValue Construct(Property[] properties) => properties.Guard<ImageFunctionValue>();
    }
}
