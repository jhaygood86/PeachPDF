#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Paints color-font (COLR/CPAL) glyphs as vector fills into the PDF content
// stream, instead of embedding the font program and showing CID text. Driven
// from XGraphicsPdfRenderer.DrawString for fonts that report IsColorFont.
//
//   - COLR v0: each base glyph is a stack of (layer glyph, palette color)
//     outlines painted bottom-to-top (this file).
//   - COLR v1: a recursive paint graph (ColorGlyphPainter.ColrV1.cs).
//
// Glyph outlines are decoded to design-unit contours and mapped to world space
// (the same space DrawString's baseline is in) through a ColrAffine that starts
// as the per-glyph placement and, for v1, composes the paint graph's own
// transforms. Fills/clips go through the shared XGraphics, so page scaling and
// the WorldToView mapping apply exactly as for ordinary vector content.
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
        private readonly XGraphics _gfx;
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
            _gfx = renderer.Gfx;
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
            ColrAffine placement = Placement(originX);
            ColrTable colr = _descriptor.ColorTable;

            // Per the COLR processing model a v1-aware renderer resolves the v1 BaseGlyphList first,
            // falling back to the v0 layer records only when the glyph has no v1 paint.
            if (colr.Version >= 1 && colr.GetV1BaseGlyphPaint(glyphId) is { } paint)
            {
                PaintV1(paint, placement, hasClip: false, clip: default, depth: 0);
                return;
            }

            if (colr.TryGetV0Layers(glyphId, out var layers))
            {
                foreach ((int layerGlyphId, int paletteIndex) in layers)
                    FillGlyphOutline(layerGlyphId, placement, ResolveColor(paletteIndex));
                return;
            }

            // A glyph with no color record inside a color font (e.g. space, digits): draw its plain
            // outline in the text color.
            FillGlyphOutline(glyphId, placement, _foreground);
        }

        /// <summary>Fills a single glyph's outline (mapped by <paramref name="transform"/>) with a solid color.</summary>
        private void FillGlyphOutline(int glyphId, ColrAffine transform, XColor color)
        {
            if (!_descriptor.TryGetGlyphOutline(glyphId, out GlyphOutline outline) || outline.IsEmpty)
                return;

            _gfx.DrawPath(new XSolidBrush(color), BuildPath(outline, transform));
        }

        private static XGraphicsPath BuildPath(GlyphOutline outline, ColrAffine transform)
        {
            var path = new XGraphicsPath { FillMode = XFillMode.Winding };

            foreach (GlyphContour contour in outline.Contours)
            {
                XPoint current = Map(transform, contour.Start.X, contour.Start.Y);
                foreach (GlyphSegment segment in contour.Segments)
                {
                    XPoint end = Map(transform, segment.End.X, segment.End.Y);
                    if (segment.IsCubic)
                    {
                        XPoint c1 = Map(transform, segment.Control1.X, segment.Control1.Y);
                        XPoint c2 = Map(transform, segment.Control2.X, segment.Control2.Y);
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

        private static XPoint Map(ColrAffine t, double x, double y)
            => new(t.XX * x + t.XY * y + t.DX, t.YX * x + t.YY * y + t.DY);

        /// <summary>The design-units-&gt;world placement affine for a glyph at the given pen origin.</summary>
        private ColrAffine Placement(double originX)
        {
            // Font em-square is y-up; the page (when downwards) is y-down, so flip Y.
            double yy = _pageDownwards ? -_scale : _scale;
            return new ColrAffine(_scale, 0, 0, yy, originX, _baselineY);
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
