#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Draw-once/reference-many for color-font glyph artwork: renders a glyph's vector paint into a Form
// XObject the first time it is seen and invokes that form with `Do` at every later occurrence, so a
// document repeating one emoji carries the artwork once rather than once per occurrence.
//
// The artwork is size-independent, so the cache key (ColorGlyphFormCache.Selector) deliberately does
// not include the font size or the pen position - both are carried by the placement transform. Only
// the visible ink moves into the form; the rendering-mode-3 text object that makes a color glyph
// selectable stays on the page, once per occurrence, because its /ActualText is per occurrence.
//
#endregion

using System;
using System.Collections.Generic;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    internal sealed partial class ColorGlyphPainter
    {
        /// <summary>
        /// Breathing room, in canonical world units, added around the measured ink box. Absorbs the
        /// content stream's own coordinate rounding at the form's edges, and gives a hairline-thin
        /// glyph a form large enough for <see cref="XForm"/> to accept.
        /// </summary>
        private const double FormPadding = 2.0;

        /// <summary>True while a measure pass is running - see <see cref="MeasureGlyphBounds"/>.</summary>
        private bool _measuring;
        private bool _hasMeasuredBounds;
        private double _measuredLeft, _measuredTop, _measuredRight, _measuredBottom;

        /// <summary>
        /// Constructs a painter that draws one glyph's artwork into its own Form XObject at the
        /// canonical em size, with the glyph's baseline origin at (<paramref name="originX"/>,
        /// <paramref name="originY"/>) in the form's own (y-down) world space. Never paints invisible
        /// text, so it carries no <see cref="XFont"/>: the selectable text object belongs to the page,
        /// not to the artwork.
        /// </summary>
        private ColorGlyphPainter(XGraphicsPdfRenderer formRenderer, OpenTypeDescriptor descriptor, XColor foreground,
            int paletteIndex, IReadOnlyDictionary<int, XColor>? overrides, double originX, double originY)
        {
            _renderer = formRenderer;
            _gfx = formRenderer.Gfx;
            _descriptor = descriptor;
            _font = null!;
            _textBrush = null!;
            _scale = ColorGlyphFormCache.EmSize / descriptor.UnitsPerEm;
            _letterSpacing = 0;
            _pageDownwards = true;
            _foreground = foreground;
            _paletteIndex = paletteIndex;
            _overrides = overrides;
            _baselineX = originX;
            _baselineY = originY;
        }

        /// <summary>
        /// Paints one glyph by invoking its cached Form XObject, rendering that form first if this is
        /// the glyph's first occurrence in the document. Returns false when the artwork cannot be
        /// shared, leaving the caller to inline the paths as before: a form's own graphics is always
        /// y-down, so an upwards page - which this fork's own renderer never produces, but the backend
        /// still models - would place the cached artwork against the wrong Y convention.
        /// </summary>
        private bool TryPaintGlyphFromForm(int glyphId, double originX, double originYOffset)
        {
            if (!_pageDownwards)
                return false;

            PdfDocument document = _renderer.Owner;
            ColorGlyphFormCache cache = document.ColorGlyphTable;
            var selector = new ColorGlyphFormCache.Selector(_descriptor, glyphId, _paletteIndex, _foreground, _overrides);
            if (!cache.TryGetForm(selector, out ColorGlyphForm cached))
            {
                cached = RenderGlyphForm(document, glyphId);
                cache.AddForm(selector, cached);
            }

            if (cached.Form is null)
                return true; // Measured as painting nothing at all - inlining it would emit nothing either.

            // One form serves every size: scale canonical world units up to this run's font size.
            double scale = _scale * _descriptor.UnitsPerEm / ColorGlyphFormCache.EmSize;
            double baselineY = _baselineY - originYOffset;
            var destRect = new XRect(
                originX + cached.LeftX * scale,
                baselineY + cached.TopY * scale,
                cached.Form.PointWidth * scale,
                cached.Form.PointHeight * scale);

            _gfx.DrawImage(cached.Form, destRect, new XRect(0, 0, cached.Form.PointWidth, cached.Form.PointHeight),
                XGraphicsUnit.Point);
            return true;
        }

        /// <summary>
        /// Renders <paramref name="glyphId"/>'s artwork into a new Form XObject sized to the glyph's
        /// measured ink, or returns an empty <see cref="ColorGlyphForm"/> when it paints nothing.
        /// </summary>
        private ColorGlyphForm RenderGlyphForm(PdfDocument document, int glyphId)
        {
            if (!MeasureGlyphBounds(glyphId, out double left, out double top, out double right, out double bottom))
                return default;

            // The padding is also what keeps the box above XForm's one-unit minimum: a measured box is
            // never negative, so both sides always add up to at least 2 * FormPadding.
            left -= FormPadding;
            top -= FormPadding;
            right += FormPadding;
            bottom += FormPadding;

            var form = new XForm(document, new XSize(right - left, bottom - top));

            // A form's world space starts at its own top-left corner, so the glyph's baseline origin
            // sits at the negated ink offset inside it.
            var formGraphics = XGraphics.FromForm(form);
            try
            {
                var painter = new ColorGlyphPainter(form.PdfRenderer, _descriptor, _foreground, _paletteIndex,
                    _overrides, -left, -top);
                painter.PaintGlyph(glyphId, -left);
            }
            finally
            {
                // Closes the form's content stream (XGraphics.Dispose -> XForm.Finish), the same
                // ownership the tile graphics from GraphicsAdapter.CreateTile has.
                formGraphics.Dispose();
            }

            return new ColorGlyphForm(form, left, top);
        }

        /// <summary>
        /// Runs the glyph's paint through a drawing-free pass that accumulates the bounds of every fill
        /// it would make, in canonical world units relative to a baseline origin at (0, 0). The result
        /// is a superset of the real ink: a v1 leaf fill is bounded by its enclosing glyph clip, and a
        /// v0 layer (or a plain outline) by the outline itself.
        /// </summary>
        private bool MeasureGlyphBounds(int glyphId, out double left, out double top, out double right, out double bottom)
        {
            var measurer = new ColorGlyphPainter(_renderer, _descriptor, _foreground, _paletteIndex, _overrides, 0, 0)
            {
                _measuring = true,
            };
            measurer.PaintGlyph(glyphId, 0);

            left = measurer._measuredLeft;
            top = measurer._measuredTop;
            right = measurer._measuredRight;
            bottom = measurer._measuredBottom;
            return measurer._hasMeasuredBounds;
        }

        /// <summary>
        /// Grows the measure pass's accumulated bounds to include <paramref name="rect"/>, which callers
        /// have already established is a real (non-negative) fill region.
        /// </summary>
        private void IncludeInMeasuredBounds(XRect rect)
        {
            if (!_hasMeasuredBounds)
            {
                _hasMeasuredBounds = true;
                _measuredLeft = rect.X;
                _measuredTop = rect.Y;
                _measuredRight = rect.X + rect.Width;
                _measuredBottom = rect.Y + rect.Height;
                return;
            }

            _measuredLeft = Math.Min(_measuredLeft, rect.X);
            _measuredTop = Math.Min(_measuredTop, rect.Y);
            _measuredRight = Math.Max(_measuredRight, rect.X + rect.Width);
            _measuredBottom = Math.Max(_measuredBottom, rect.Y + rect.Height);
        }
    }
}
