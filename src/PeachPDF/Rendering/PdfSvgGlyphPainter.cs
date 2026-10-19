using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine.Rendering;
using PeachPDF.Svg;
using System.Collections.Generic;

namespace PeachPDF.Rendering
{
    /// <summary>
    /// Draws the SVG document of a font's glyph (OpenType SVG) through the SVG renderer, onto a graphics: the PDF one a
    /// <c>PdfCanvas</c> wraps, or a raster surface.
    /// </summary>
    /// <remarks>
    /// A document is built once per glyph, palette and text colour (<see cref="SvgGlyphDocument"/>) and painted through
    /// <see cref="SvgRenderer.RenderCachedInto"/>. On the PDF graphics that puts the artwork in a Form XObject shared by every occurrence at
    /// one size, so a repeated glyph is in the PDF once and referenced; a graphics with no PDF document to own a form (the raster one) is
    /// painted into directly, so a filter, a shadow or a flattened region sees the real glyph. A document that cannot be built is remembered
    /// as such and the glyph is drawn as its outline.
    /// </remarks>
    internal sealed class PdfSvgGlyphPainter : ISvgGlyphPainter
    {
        private readonly Canvas _host;
        private readonly RenderContext _renderContext;
        // One set of drawings for each PDF document, so a glyph repeated across pages is built once and its form is shared: the form cache
        // of the SVG renderer is keyed by the drawing's identity. A graphics with no PDF document (the raster one) shares them per adapter.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<GlyphDocumentKey, SvgDocument?>> Documents = new();

        private readonly Dictionary<GlyphDocumentKey, SvgDocument?> _documents;

        /// <summary>The graphics this draws on.</summary>
        internal Canvas Host => _host;

        internal PdfSvgGlyphPainter(Canvas host, RenderContext renderContext)
        {
            _host = host;
            _renderContext = renderContext;
            _documents = Documents.GetOrCreateValue(host.TileCacheOwner ?? renderContext);
        }

        public bool TryPaint(Typeface typeface, ushort glyph, SvgGlyph svg, double fontSize, double originX, double baselineY,
            PaintColor foreground, int paletteIndex, IReadOnlyDictionary<int, PaintColor>? overrides)
        {
            var xForeground = PaintColor.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B);
            var xOverrides = overrides is null ? null : ToXColorOverrides(overrides);
            var key = new GlyphDocumentKey(typeface, glyph, paletteIndex, xForeground, xOverrides);
            if (!_documents.TryGetValue(key, out var document))
            {
                document = SvgGlyphDocument.Build(svg, entry => PaletteColour(typeface, paletteIndex, overrides, entry),
                    typeface.ColorPalette?.EntriesPerPalette ?? 0, foreground, _renderContext);
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
            var viewport = new Rect(
                (originX + canvas.X * scale) * ppp,
                (baselineY + canvas.Y * scale) * ppp,
                canvas.Width * scale * ppp,
                canvas.Height * scale * ppp);
            SvgRenderer.RenderCachedInto(_host, document, viewport);
            return true;
        }

        private static Dictionary<int, PaintColor> ToXColorOverrides(IReadOnlyDictionary<int, PaintColor> overrides)
        {
            var result = new Dictionary<int, PaintColor>(overrides.Count);
            foreach (var (entry, colour) in overrides)
                result[entry] = PaintColor.FromArgb(colour.A, colour.R, colour.G, colour.B);

            return result;
        }

        private static PaintColor? PaletteColour(Typeface typeface, int paletteIndex, IReadOnlyDictionary<int, PaintColor>? overrides, int entry)
        {
            if (overrides is not null && overrides.TryGetValue(entry, out var over))
            {
                return over;
            }

            if (typeface.ColorPalette is { } palette && palette.TryGetColor(paletteIndex, entry, out var c))
            {
                return PaintColor.FromArgb(c.A, c.R, c.G, c.B);
            }

            return null;
        }
    }
}
