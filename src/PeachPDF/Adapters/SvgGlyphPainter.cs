using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Drawing.Pdf;
using PeachPDF.Svg;
using System.Collections.Generic;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Draws the SVG document of a font's glyph (OpenType SVG) through the SVG renderer, onto the graphics a <see cref="GraphicsAdapter"/> wraps.
    /// </summary>
    /// <remarks>
    /// A document is built once per glyph, palette and text colour (<see cref="SvgGlyphDocument"/>) and painted through
    /// <see cref="SvgRenderer.RenderCachedInto"/>, which puts the artwork in a Form XObject shared by every occurrence at one size, so a
    /// repeated glyph is in the PDF once and referenced. A document that cannot be built is remembered as such and the glyph is drawn as its
    /// outline.
    /// </remarks>
    internal sealed class SvgGlyphPainter : ISvgGlyphPainter
    {
        private readonly GraphicsAdapter _host;
        // One set of drawings for each PDF document, so a glyph repeated across pages is built once and its form is shared: the form cache
        // of the SVG renderer is keyed by the drawing's identity.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<ColorGlyphFormCache.Selector, SvgDocument?>> Documents = new();

        private readonly Dictionary<ColorGlyphFormCache.Selector, SvgDocument?> _documents;

        /// <summary>The adapter whose graphics this draws on.</summary>
        internal GraphicsAdapter Host => _host;

        internal SvgGlyphPainter(GraphicsAdapter host)
        {
            _host = host;
            _documents = host.FormCacheOwner is { } owner ? Documents.GetOrCreateValue(owner) : [];
        }

        public bool TryPaint(Typeface typeface, ushort glyph, SvgGlyph svg, double fontSize, double originX, double baselineY,
            XColor foreground, int paletteIndex, IReadOnlyDictionary<int, XColor>? overrides)
        {
            var key = new ColorGlyphFormCache.Selector(typeface, glyph, paletteIndex, foreground, overrides);
            if (!_documents.TryGetValue(key, out var document))
            {
                document = SvgGlyphDocument.Build(svg, entry => PaletteColour(typeface, paletteIndex, overrides, entry),
                    typeface.ColorPalette?.EntriesPerPalette ?? 0, ToRColor(foreground), _host.Adapter);
                _documents[key] = document;
            }

            if (document is null)
            {
                return false;
            }

            double scale = fontSize / svg.UnitsPerEm;
            double ppp = _host.PixelsPerPoint;
            var viewport = new RRect(
                (originX - SvgGlyphDocument.CanvasLeftEms * svg.UnitsPerEm * scale) * ppp,
                (baselineY - SvgGlyphDocument.CanvasTopEms * svg.UnitsPerEm * scale) * ppp,
                SvgGlyphDocument.CanvasWidthEms * svg.UnitsPerEm * scale * ppp,
                SvgGlyphDocument.CanvasHeightEms * svg.UnitsPerEm * scale * ppp);
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
