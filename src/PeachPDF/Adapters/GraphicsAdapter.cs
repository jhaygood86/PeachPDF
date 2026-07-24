// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Utilities;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms Graphics for core.
    /// </summary>
    internal sealed class GraphicsAdapter : RGraphics
    {
        /// <summary>
        /// The wrapped WinForms graphics object
        /// </summary>
        private readonly XGraphics _g;

        /// <summary>
        /// if to release the graphics object on dispose
        /// </summary>
        private readonly bool _releaseGraphics;

        private double PixelsPerPoint { get; }

        /// <summary>
        /// _releaseGraphics is set true exactly for tile-backed instances (see the constructor
        /// comment and CreateTile below), making it the same signal as "paints into an offscreen
        /// tile" - see RGraphics.IsOffscreenTile.
        /// </summary>
        public override bool IsOffscreenTile => _releaseGraphics;

        /// <summary>
        /// Used to measure and draw strings
        /// </summary>
        private static readonly XStringFormat _stringFormat;

        static GraphicsAdapter()
        {
            _stringFormat = new XStringFormat
            {
                Alignment = XStringAlignment.Near,
                LineAlignment = XLineAlignment.Near
            };
        }

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="adapter">the adapter</param>
        /// <param name="g">the win forms graphics object to use</param>
        /// <param name="pixelsPerPoint">The number of pixels in each point</param>
        /// <param name="releaseGraphics">optional: if to release the graphics object on dispose (default - false)</param>
        public GraphicsAdapter(RAdapter adapter, XGraphics g, double pixelsPerPoint, bool releaseGraphics = false)
            : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue))
        {
            ArgumentNullException.ThrowIfNull(g);

            _g = g;
            _releaseGraphics = releaseGraphics;

            PixelsPerPoint = pixelsPerPoint;
        }

        public override void PopClip()
        {
            _clipStack.Pop();
            _g.Restore();
        }

        public override void PushClip(RRect rect)
        {
            _clipStack.Push(rect);
            _g.Save();
            _g.IntersectClip(Utils.Convert(rect, PixelsPerPoint));
        }

        public override void PushClip(RGraphicsPath path)
        {
            // No simple bounding rectangle for an arbitrary path, so keep the tracked clip bound
            // conservative (unchanged) - it's only used for culling, and an over-wide bound never
            // hides content that should actually be visible.
            _clipStack.Push(_clipStack.Peek());
            _g.Save();
            _g.IntersectClip(((GraphicsPathAdapter)path).GraphicsPath);
        }

        public override void PushClipExclude(RRect rect)
        { }

        public override void PushTransform(RMatrix matrix)
        {
            _g.Save();
            _g.MultiplyTransform(new XMatrix(
                matrix.M11, matrix.M12, matrix.M21, matrix.M22,
                matrix.OffsetX / PixelsPerPoint, matrix.OffsetY / PixelsPerPoint));
        }

        public override void PopTransform()
        {
            _g.Restore();
        }

        public override object SetAntiAliasSmoothingMode()
        {
            var prevMode = _g.SmoothingMode;
            _g.SmoothingMode = XSmoothingMode.AntiAlias;
            return prevMode;
        }

        public override void ReturnPreviousSmoothingMode(object? prevMode)
        {
            if (prevMode != null)
            {
                _g.SmoothingMode = (XSmoothingMode)prevMode;
            }
        }

        public override RSize MeasureString(string str, RFont font)
        {
            var fontAdapter = (FontAdapter)font;
            var realFont = fontAdapter.Font;
            var size = _g.MeasureString(str, realFont, _stringFormat);

            if (!(font.Height < 0)) return Utils.Convert(size, PixelsPerPoint);

            var height = realFont.Height;
            // Read ascent/descent/em-height directly off realFont's OWN already-resolved descriptor
            // instead of re-deriving them via XFontFamily.GetCellAscent/GetCellDescent/GetEmHeight, which
            // re-resolve a font by realFont.FontFamily.Name (the physical font's own internal name, e.g.
            // "Source Code Pro" - not the CSS-facing family alias that was actually registered) through
            // IFontResolver - for a custom/@font-face-registered family this can resolve to an entirely
            // unrelated font (no family is registered under the font's own internal name), and even when
            // it does find something, it bypasses the per-instance cache routing that keeps two
            // PdfGenerators' same-named custom fonts from colliding (see XFont.Descriptor and
            // XGlyphTypeface.OwningInstanceResolver).
            var descriptor = realFont.Descriptor;
            var descent = realFont.Size * descriptor.Descender / descriptor.UnitsPerEm;
            var ascent = realFont.Size * descriptor.Ascender / descriptor.UnitsPerEm;
            fontAdapter.SetMetrics(height, (int)Math.Round((height - descent + 1f)), (int)Math.Round(ascent));

            return Utils.Convert(size, PixelsPerPoint);
        }

        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
        {
            // there is no need for it - used for text selection
            throw new NotSupportedException();
        }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null)
        {
            var xBrush = ((BrushAdapter)_adapter.GetSolidBrush(color)).Brush;
            var xPoint = Utils.Convert(point, PixelsPerPoint);

            // Realized via the PDF `Tc` character-spacing operator (XGraphicsPdfRenderer/
            // PdfGraphicsState) rather than drawing character-by-character - `Tc` applies additively to
            // every glyph shown by the one text-showing operation below, so letter-spacing needs no
            // extra draw calls and the string stays a single, contiguous, copy/paste- and
            // tagged-PDF-friendly text run regardless of its value.
            var xLetterSpacing = letterSpacing / PixelsPerPoint;
            _g.DrawString(str, ((FontAdapter)font).Font, xBrush, xPoint.X, xPoint.Y, _stringFormat, xLetterSpacing, ToGlyphPalette(fontPalette));
        }

        /// <summary>
        /// Converts a resolved <see cref="RFontPalette"/> (adapter layer, <see cref="RColor"/> overrides) into the
        /// backend <see cref="XGlyphPalette"/> (<see cref="XColor"/> overrides). Null passes straight through.
        /// </summary>
        private static XGlyphPalette? ToGlyphPalette(RFontPalette? palette)
        {
            if (palette is null)
                return null;

            var overrides = new Dictionary<int, XColor>(palette.Overrides.Count);
            foreach (var (entryIndex, color) in palette.Overrides)
                overrides[entryIndex] = XColor.FromArgb(color.A, color.R, color.G, color.B);

            return new XGlyphPalette(palette.BasePaletteIndex, overrides);
        }

        public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0)
        {
            var realFont = ((FontAdapter)font).Font;
            var descriptor = realFont.Descriptor;
            if (descriptor is null || descriptor.UnitsPerEm == 0)
                return null;

            // Design units -> SVG user space. font.Size is in points (XFont.Size = css/svg size / PixelsPerPoint),
            // while these path coordinates reach the backend un-scaled by PixelsPerPoint (see GraphicsPathAdapter.
            // Transform), the same space shape paths are built in - so multiply back by PixelsPerPoint. The em-square
            // is y-up; user space is y-down, so glyph Y is subtracted from the baseline.
            double scale = realFont.Size * PixelsPerPoint / descriptor.UnitsPerEm;

            var path = GetGraphicsPath();
            path.FillMode = RFillMode.Nonzero;

            double penX = baselineOrigin.X;
            double baseY = baselineOrigin.Y;
            bool anyGeometry = false;

            foreach (var rune in str.EnumerateRunes())
            {
                int glyphId = descriptor.CharCodeToGlyphIndex(rune);

                // TryGetGlyphOutline returns false for an empty glyph (e.g. space) or a CFF/bitmap font
                // with no `glyf` table - either way there's nothing to add for this glyph.
                if (descriptor.TryGetGlyphOutline(glyphId, out GlyphOutline outline))
                {
                    foreach (GlyphContour contour in outline.Contours)
                    {
                        path.AddMove(penX + contour.Start.X * scale, baseY - contour.Start.Y * scale);

                        foreach (GlyphSegment segment in contour.Segments)
                        {
                            if (segment.IsCubic)
                            {
                                path.AddBezierTo(
                                    penX + segment.Control1.X * scale, baseY - segment.Control1.Y * scale,
                                    penX + segment.Control2.X * scale, baseY - segment.Control2.Y * scale,
                                    penX + segment.End.X * scale, baseY - segment.End.Y * scale);
                            }
                            else
                            {
                                path.LineTo(penX + segment.End.X * scale, baseY - segment.End.Y * scale);
                            }
                        }

                        path.CloseFigure();
                        anyGeometry = true;
                    }
                }

                penX += descriptor.GlyphIndexToWidth(glyphId) * scale + letterSpacing;
            }

            // No geometry at all means the font produced no `glyf` outlines (CFF/bitmap) - signal the
            // caller to fall back to DrawString.
            if (!anyGeometry)
            {
                path.Dispose();
                return null;
            }

            return path;
        }

        public override RGraphicsPath GetGraphicsPath()
        {
            return new GraphicsPathAdapter();
        }

        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height)
        {
            // XForm/XGraphics.FromForm() is real, working PdfSharpCore infrastructure for drawing into
            // a separate PDF Form XObject's own content stream (rather than the page's) - the same
            // mechanism this method's own XGraphics (_g) is itself built on when bound to a page. XGraphics.Owner
            // falls back to the owning document of a Form XObject (not just a page), so this also works
            // when called recursively from inside another tile (e.g. nested opacity) - it's still null
            // outside any real page/document-paint context (e.g. a measure-only pass).
            var document = _g.Owner;
            if (document is null || width <= 0 || height <= 0)
                return null;

            var form = new XForm(document, new XSize(width, height));
            var formGraphics = XGraphics.FromForm(form);
            // releaseGraphics: true - disposing the returned tile RGraphics must dispose the
            // underlying XGraphics, which is what actually calls XForm.Finish() and closes out the
            // Form XObject's content stream (see XGraphics.Dispose()). Without this, the tile's
            // drawing commands would never get flushed into the PDF at all.
            var tileGraphics = new GraphicsAdapter(_adapter, formGraphics, PixelsPerPoint, releaseGraphics: true);
            return (tileGraphics, new ImageAdapter(form));
        }

        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect)
        {
            if (((ImageAdapter)image).Image is XForm imageForm && ((ImageAdapter)maskImage).Image is XForm maskForm)
                _g.DrawImageMasked(imageForm, maskForm, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity)
        {
            if (((ImageAdapter)image).Image is XForm imageForm)
                _g.DrawImageWithOpacity(imageForm, Utils.Convert(destRect, PixelsPerPoint), opacity);
        }

        public override void BeginMarkedContent(string structureType, int mcid)
        {
            _g.BeginMarkedContent(structureType, mcid);
        }

        public override void EndMarkedContent()
        {
            _g.EndMarkedContent();
        }

        public override void BeginArtifact()
        {
            _g.BeginArtifact();
        }

        public override void Dispose()
        {
            if (_releaseGraphics)
                _g.Dispose();
        }


        #region Delegate graphics methods

        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2)
        {
            _g.DrawLine(((PenAdapter)pen).Pen, x1 / PixelsPerPoint, y1 / PixelsPerPoint, x2 / PixelsPerPoint, y2 / PixelsPerPoint);
        }

        public override void DrawRectangle(RPen pen, double x, double y, double width, double height)
        {
            _g.DrawRectangle(((PenAdapter)pen).Pen, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
        }

        public override void DrawRectangle(RBrush brush, double x, double y, double width, double height)
        {
            var xBrush = ((BrushAdapter)brush).Brush;
            if (xBrush is XBaseGradientBrush)
            {
                // Wrap in q/Q so the SMask applied for transparent gradients does not
                // leak into subsequent operations (e.g. border drawing).
                var state = _g.Save();
                _g.DrawRectangle(xBrush, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
                _g.Restore(state);

                // handle bug in PdfSharp that keeps the brush color for next string draw
                if (xBrush is XLinearGradientBrush)
                    _g.DrawRectangle(XBrushes.White, 0, 0, 0.1 / PixelsPerPoint, 0.1 / PixelsPerPoint);
            }
            else
            {
                _g.DrawRectangle(xBrush, x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
            }
        }

        public override void DrawImage(RImage image, RRect destRect, RRect srcRect)
        {
            _g.DrawImage(((ImageAdapter)image).Image, Utils.Convert(destRect, PixelsPerPoint), Utils.Convert(srcRect, PixelsPerPoint), XGraphicsUnit.Point);
        }

        public override void DrawImage(RImage image, RRect destRect)
        {
            _g.DrawImage(((ImageAdapter)image).Image, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawPath(RPen pen, RGraphicsPath path)
        {
            var xPen = ((PenAdapter)pen).Pen;
            if (xPen.Brush is XBaseGradientBrush)
            {
                // Wrap in q/Q so the SMask applied for a transparent gradient stroke does not
                // leak into subsequent operations. A later paint that emits no SMask of its own
                // (an opaque gradient stroke, or any non-gradient content) would otherwise inherit
                // this stroke's luminosity mask and be masked away (issue #135).
                var state = _g.Save();
                _g.DrawPath(xPen, ((GraphicsPathAdapter)path).GraphicsPath);
                _g.Restore(state);
            }
            else
            {
                _g.DrawPath(xPen, ((GraphicsPathAdapter)path).GraphicsPath);
            }
        }

        public override void DrawPath(RBrush brush, RGraphicsPath path)
        {
            var xBrush = ((BrushAdapter)brush).Brush;
            if (xBrush is XBaseGradientBrush)
            {
                var state = _g.Save();
                _g.DrawPath(xBrush, ((GraphicsPathAdapter)path).GraphicsPath);
                _g.Restore(state);
            }
            else
            {
                _g.DrawPath(xBrush, ((GraphicsPathAdapter)path).GraphicsPath);
            }
        }

        public override void DrawPolygon(RBrush brush, RPoint[] points)
        {
            if (points is { Length: > 0 })
            {
                _g.DrawPolygon((XBrush)((BrushAdapter)brush).Brush, Utils.Convert(points, PixelsPerPoint), XFillMode.Winding);
            }
        }

        #endregion
    }
}