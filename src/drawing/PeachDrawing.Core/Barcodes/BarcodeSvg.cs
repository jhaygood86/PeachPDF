using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// Writes a <see cref="Barcode"/> as SVG markup: one <c>path</c> of rectangles for the bars, so a renderer draws
    /// them as vector shapes, and <c>text</c> elements for the human-readable text.
    /// </summary>
    public static class BarcodeSvg
    {
        /// <summary>
        /// Writes the barcode as a standalone SVG document. The <c>viewBox</c> is in modules, the
        /// <c>width</c> and <c>height</c> are the module size times the layout's size.
        /// </summary>
        /// <param name="barcode">The barcode.</param>
        /// <param name="moduleSize">The size of one module in CSS pixels.</param>
        /// <param name="color">The color of the bars and the text.</param>
        /// <param name="background">The color of the light margin and the spaces, or <c>null</c> for none.</param>
        /// <param name="fontFamily">The CSS font family of the text, or <c>null</c> for a monospace default.</param>
        /// <returns>The SVG markup.</returns>
        public static string ToSvg(Barcode barcode, double moduleSize, PaintColor color, PaintColor? background = null, string? fontFamily = null)
        {
            ArgumentNullException.ThrowIfNull(barcode);
            if (!(moduleSize > 0))
                throw new ArgumentOutOfRangeException(nameof(moduleSize), "The module size must be positive.");

            var layout = barcode.CreateLayout();
            var ci = CultureInfo.InvariantCulture;
            string fill = Css(color);
            var svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append((layout.Width * moduleSize).ToString("0.####", ci))
               .Append("\" height=\"").Append((layout.Height * moduleSize).ToString("0.####", ci))
               .Append("\" viewBox=\"0 0 ").Append(layout.Width.ToString("0.####", ci)).Append(' ').Append(layout.Height.ToString("0.####", ci))
               .Append("\" role=\"img\" aria-label=\"").Append(Escape(barcode.DisplayText)).Append("\">");
            svg.Append("<title>").Append(Escape(barcode.DisplayText)).Append("</title>");
            if (background is PaintColor back)
                svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(Css(back)).Append("\"/>");

            svg.Append("<path shape-rendering=\"crispEdges\" fill=\"").Append(fill).Append("\" d=\"");
            foreach (var bar in layout.Bars)
            {
                svg.Append('M').Append(bar.X.ToString("0.####", ci)).Append(' ').Append(bar.Y.ToString("0.####", ci))
                   .Append('h').Append(bar.Width.ToString("0.####", ci))
                   .Append('v').Append(bar.Height.ToString("0.####", ci))
                   .Append('h').Append((-bar.Width).ToString("0.####", ci)).Append('z');
            }
            svg.Append("\"/>");

            string family = WebUtility.HtmlEncode(fontFamily ?? "OCR-B, 'Courier New', monospace");
            foreach (var text in layout.Texts)
            {
                // The baseline sits about 0.8 em below the top of the line.
                svg.Append("<text x=\"").Append((text.Bounds.X + text.Bounds.Width / 2).ToString("0.####", ci))
                   .Append("\" y=\"").Append((text.Bounds.Y + text.FontSize * 0.85).ToString("0.####", ci))
                   .Append("\" font-size=\"").Append(text.FontSize.ToString("0.####", ci))
                   .Append("\" font-family=\"").Append(family)
                   .Append("\" text-anchor=\"middle\" fill=\"").Append(fill).Append("\">")
                   .Append(Escape(text.Text)).Append("</text>");
            }
            svg.Append("</svg>");
            return svg.ToString();
        }

        // Characters XML 1.0 forbids (control characters, lone surrogates) would make the document unparseable; they become spaces.
        static string Escape(string text)
        {
            var clean = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    clean.Append(ch).Append(text[++i]);
                else
                    clean.Append(System.Xml.XmlConvert.IsXmlChar(ch) ? ch : ' ');
            }
            return WebUtility.HtmlEncode(clean.ToString());
        }

        static string Css(PaintColor color)
        {
            var (r, g, b) = color.ToRgb();
            return color.A == 255
                ? $"rgb({r},{g},{b})"
                : string.Create(CultureInfo.InvariantCulture, $"rgba({r},{g},{b},{color.A / 255.0:0.###})");
        }
    }
}
