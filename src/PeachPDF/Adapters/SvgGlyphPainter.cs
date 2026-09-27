using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Drawing.Pdf;
using PeachPDF.Svg;
using System.Collections.Generic;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Draws the SVG document of a font's glyph (OpenType SVG) through the SVG renderer, onto a graphics: the PDF one a
    /// <see cref="GraphicsAdapter"/> wraps, or a raster surface.
    /// </summary>
    /// <remarks>
    /// A document is built once per glyph, palette and text colour (<see cref="SvgGlyphDocument"/>) and painted through
    /// <see cref="SvgRenderer.RenderCachedInto"/>. On the PDF graphics that puts the artwork in a Form XObject shared by every occurrence at
    /// one size, so a repeated glyph is in the PDF once and referenced; a graphics with no PDF document to own a form (the raster one) is
    /// painted into directly, so a filter, a shadow or a flattened region sees the real glyph. A document that cannot be built is remembered
    /// as such and the glyph is drawn as its outline.
    /// </remarks>
    internal sealed class SvgGlyphPainter : ISvgGlyphPainter
    {
        private readonly RGraphics _host;
        private readonly RAdapter _adapter;
        // One set of drawings for each PDF document, so a glyph repeated across pages is built once and its form is shared: the form cache
        // of the SVG renderer is keyed by the drawing's identity. A graphics with no PDF document (the raster one) shares them per adapter.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<ColorGlyphFormCache.Selector, SvgDocument?>> Documents = new();

        private readonly Dictionary<ColorGlyphFormCache.Selector, SvgDocument?> _documents;

        /// <summary>The graphics this draws on.</summary>
        internal RGraphics Host => _host;

        internal SvgGlyphPainter(RGraphics host, RAdapter adapter)
        {
            _host = host;
            _adapter = adapter;
            _documents = Documents.GetOrCreateValue(host.FormCacheOwner ?? adapter);
        }

        public bool TryPaint(Typeface typeface, ushort glyph, SvgGlyph svg, double fontSize, double originX, double baselineY,
            XColor foreground, int paletteIndex, IReadOnlyDictionary<int, XColor>? overrides)
        {
            var key = new ColorGlyphFormCache.Selector(typeface, glyph, paletteIndex, foreground, overrides);
            if (!_documents.TryGetValue(key, out var document))
            {
                document = SvgGlyphDocument.Build(svg, entry => PaletteColour(typeface, paletteIndex, overrides, entry),
                    typeface.ColorPalette?.EntriesPerPalette ?? 0, ToRColor(foreground), _adapter);
                _documents[key] = document;
            }

            if (document is null)
            {
                return false;
            }

            double scale = fontSize / svg.UnitsPerEm;
            double ppp = _host.PixelsPerPoint;

            // The canvas the document holds is in font units around the glyph origin, y down.
            var canvas = document.ViewBox!.Value;
            var viewport = new RRect(
                (originX + canvas.X * scale) * ppp,
                (baselineY + canvas.Y * scale) * ppp,
                canvas.Width * scale * ppp,
                canvas.Height * scale * ppp);
            SvgRenderer.RenderCachedInto(_host, document, viewport);
            return true;
        }

        private static RColor ToRColor(XColor c) => RColor.FromArgb((int)System.Math.Round(c.A * 255), c.R, c.G, c.B);

        private static RColor? PaletteColour(Typeface typeface, int paletteIndex, IReadOnlyDictionary<int, XColor>? overrides, int entry)
        {
            if (overrides is not null && overrides.TryGetValue(entry, out var over))
            {
                return ToRColor(over);
            }

            if (typeface.ColorPalette is { } palette && palette.TryGetColor(paletteIndex, entry, out var c))
            {
                return RColor.FromArgb(c.A, c.R, c.G, c.B);
            }

            return null;
        }
    }
}
