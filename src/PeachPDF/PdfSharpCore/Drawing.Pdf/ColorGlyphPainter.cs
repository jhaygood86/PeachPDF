#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Paints color-font (COLR/CPAL) glyphs as vector fills into the PDF content
// stream, instead of embedding the font program and showing CID text. Driven
// from XGraphicsPdfRenderer.DrawString for fonts that report IsColorFont.
//
//   - COLR v0: each base glyph is a stack of (layer glyph, palette color)
//     outlines painted bottom-to-top.
//   - COLR v1: a recursive paint graph (added in a later stage).
//
// Glyph outlines are decoded to design-unit contours and mapped to world space
// (the same space DrawString's baseline is in) via a per-glyph placement
// matrix; fills go through the renderer's own DrawPath, so page scaling and the
// WorldToView mapping apply exactly as for ordinary vector content.
//
#endregion

using System.Text;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed partial class ColorGlyphPainter
    {
        private const int UseForegroundColor = 0xFFFF;

        private readonly XGraphicsPdfRenderer _renderer;
        private readonly OpenTypeDescriptor _descriptor;
        private readonly double _scale;         // design units -> world units
        private readonly double _letterSpacing; // world units
        private readonly bool _pageDownwards;
        private readonly XColor _foreground;

        private readonly double _baselineX;
        private readonly double _baselineY;

        public ColorGlyphPainter(XGraphicsPdfRenderer renderer, OpenTypeDescriptor descriptor, XFont font,
            XBrush brush, double baselineX, double baselineY, double letterSpacing, XPageDirection pageDirection)
        {
            _renderer = renderer;
            _descriptor = descriptor;
            _scale = font.Size / descriptor.UnitsPerEm;
            _letterSpacing = letterSpacing;
            _pageDownwards = pageDirection == XPageDirection.Downwards;
            _foreground = brush is XSolidBrush solid ? solid.Color : XColors.Black;
            _baselineX = baselineX;
            _baselineY = baselineY;
        }

        /// <summary>Paints every rune of the run, advancing the pen by each glyph's advance width.</summary>
        public void Paint(string text)
        {
            double penX = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                int glyphId = _descriptor.CharCodeToGlyphIndex(rune);
                PaintGlyph(glyphId, _baselineX + penX);
                penX += _descriptor.GlyphIndexToWidth(glyphId) * _scale + _letterSpacing;
            }
        }

        private void PaintGlyph(int glyphId, double originX)
        {
            XMatrix placement = Placement(originX);

            ColrTable colr = _descriptor.ColorTable;

            if (colr.TryGetV0Layers(glyphId, out var layers))
            {
                foreach ((int layerGlyphId, int paletteIndex) in layers)
                    FillGlyphOutline(layerGlyphId, placement, ResolveColor(paletteIndex));
                return;
            }

            if (colr.Version >= 1 && colr.GetV1BaseGlyphPaint(glyphId) is { } paint)
            {
                PaintV1(paint, placement);
                return;
            }

            // A glyph with no color record inside a color font (e.g. space, digits): draw its plain
            // outline in the text color.
            FillGlyphOutline(glyphId, placement, _foreground);
        }

        // Implemented in ColorGlyphPainter.ColrV1.cs.
        partial void PaintV1(ColrPaint paint, XMatrix transform);

        /// <summary>Fills a single glyph's outline (transformed by <paramref name="transform"/>) with a solid color.</summary>
        private void FillGlyphOutline(int glyphId, XMatrix transform, XColor color)
        {
            if (!_descriptor.TryGetGlyphOutline(glyphId, out GlyphOutline outline) || outline.IsEmpty)
                return;

            XGraphicsPath path = BuildPath(outline, transform);
            _renderer.DrawPath(null, new XSolidBrush(color), path);
        }

        private static XGraphicsPath BuildPath(GlyphOutline outline, XMatrix transform)
        {
            var path = new XGraphicsPath { FillMode = XFillMode.Winding };

            foreach (GlyphContour contour in outline.Contours)
            {
                XPoint current = transform.Transform(new XPoint(contour.Start.X, contour.Start.Y));
                foreach (GlyphSegment segment in contour.Segments)
                {
                    XPoint end = transform.Transform(new XPoint(segment.End.X, segment.End.Y));
                    if (segment.IsCubic)
                    {
                        XPoint c1 = transform.Transform(new XPoint(segment.Control1.X, segment.Control1.Y));
                        XPoint c2 = transform.Transform(new XPoint(segment.Control2.X, segment.Control2.Y));
                        path.AddBezier(current.X, current.Y, c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        path.AddLine(current.X, current.Y, end.X, end.Y);
                    }
                    current = end;
                }
                path.CloseFigure();
            }

            return path;
        }

        /// <summary>The design-units-&gt;world placement matrix for a glyph at the given pen origin.</summary>
        private XMatrix Placement(double originX)
        {
            // Font em-square is y-up; the page (when downwards) is y-down, so flip Y.
            double m22 = _pageDownwards ? -_scale : _scale;
            return new XMatrix(_scale, 0, 0, m22, originX, _baselineY);
        }

        private XColor ResolveColor(int paletteIndex) => ResolveColor(paletteIndex, 1.0);

        private XColor ResolveColor(int paletteIndex, double alpha)
        {
            XColor color;
            if (paletteIndex == UseForegroundColor)
            {
                color = _foreground;
            }
            else if (_descriptor.ColorPalette.TryGetColor(0, paletteIndex, out var c))
            {
                color = XColor.FromArgb(c.A, c.R, c.G, c.B);
            }
            else
            {
                color = _foreground;
            }

            if (alpha < 1.0)
                color = XColor.FromArgb((int)System.Math.Round(color.A * alpha), color.R, color.G, color.B);
            return color;
        }
    }
}
