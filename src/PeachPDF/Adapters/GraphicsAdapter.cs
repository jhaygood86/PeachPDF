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

using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Raster;
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

        /// <summary>
        /// <c>text-decoration-skip-ink</c>'s measured crossings, keyed by everything they depend on once
        /// the run's absolute position is factored out - see <see cref="GetInkCrossings"/>. Per adapter,
        /// so it lives and dies with one render and needs no synchronization (a <c>PdfGenerator</c> is not
        /// thread-safe by contract). Worth having because the measurement shapes the run a second time and
        /// decodes every glyph's outline, and a decorated paragraph repeats the same words on line after
        /// line.
        /// </summary>
        private readonly Dictionary<InkCrossingKey, List<RInkSpan>?> _inkCrossings = [];

        public override double PixelsPerPoint { get; }

        internal override object? FormCacheOwner => _g.Owner;

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

        // The accumulated pushed transforms, so a raster region can pick a pixel pitch that is right after the transforms are
        // applied (from the linear part) and an SVG backdrop repaint can map between coordinate spaces (the whole matrix).
        private readonly Stack<RMatrix> _transformStack = [];
        private RMatrix _accumulated = RMatrix.Identity;

        internal override RMatrix CurrentTransform => _accumulated;

        internal override (double X, double Y) TransformScale
        {
            get
            {
                var x = Math.Sqrt(_accumulated.M11 * _accumulated.M11 + _accumulated.M12 * _accumulated.M12);
                var y = Math.Sqrt(_accumulated.M21 * _accumulated.M21 + _accumulated.M22 * _accumulated.M22);
                return (x > 1e-6 ? x : 1.0, y > 1e-6 ? y : 1.0);
            }
        }

        public override void PushTransform(RMatrix matrix)
        {
            _transformStack.Push(_accumulated);
            _accumulated = matrix.Then(_accumulated);

            _g.Save();
            _g.MultiplyTransform(new XMatrix(
                matrix.M11, matrix.M12, matrix.M21, matrix.M22,
                matrix.OffsetX / PixelsPerPoint, matrix.OffsetY / PixelsPerPoint));
        }

        public override void PopTransform()
        {
            if (_transformStack.Count > 0)
                _accumulated = _transformStack.Pop();

            _g.Restore();
        }

        public override void PushBlendMode(RBlendMode mode)
        {
            _g.Save();
            _g.SetBlendMode(mode.ToString());
        }

        public override void PopBlendMode()
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

        public override RSize MeasureString(string str, RFont font, ShapeSettings? features = null)
        {
            var realFont = ((FontAdapter)font).Font;
            var size = _g.MeasureString(str, realFont, _stringFormat, features ?? ShapeSettings.Default);
            return Utils.Convert(size, PixelsPerPoint);
        }

        public override int CountShapedGlyphs(string str, RFont font, ShapeSettings? features = null)
        {
            return Shaper.Shape(((FontAdapter)font).Font.Typeface, str, features ?? ShapeSettings.Default).Glyphs.Count;
        }

        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
        {
            // there is no need for it - used for text selection
            throw new NotSupportedException();
        }

        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, ShapeSettings? features = null) =>
            DrawString(str, font, color, point, size, letterSpacing, fontPalette, features, logicalText: null);

        /// <summary>See <see cref="RGraphics.DrawString(string, RFont, RColor, RPoint, RSize, double, RFontPalette?, ShapeSettings?, string?)"/>'s
        /// own remarks for <paramref name="logicalText"/> - threaded straight through to
        /// <see cref="XGraphics.DrawString(string, XFont, XBrush, double, double, XStringFormat, double, XGlyphPalette?, ShapeSettings?, string?)"/>,
        /// the one real PDF-writing path that acts on it.</summary>
        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing, RFontPalette? fontPalette, ShapeSettings? features, string? logicalText)
        {
            // Invisible text paints nothing, so its colour is irrelevant - and an opaque one keeps it clear of the alpha and
            // colour-space guards a real colour would pass through.
            var xBrush = ((BrushAdapter)_adapter.GetSolidBrush(InvisibleText ? RColor.Black : color)).Brush;
            _g.InvisibleText = InvisibleText;
            var xPoint = Utils.Convert(point, PixelsPerPoint);

            // Realized via the PDF `Tc` character-spacing operator (XGraphicsPdfRenderer/
            // PdfGraphicsState) rather than drawing character-by-character - `Tc` applies additively to
            // every glyph shown by the one text-showing operation below, so letter-spacing needs no
            // extra draw calls and the string stays a single, contiguous, copy/paste- and
            // tagged-PDF-friendly text run regardless of its value.
            var xLetterSpacing = letterSpacing / PixelsPerPoint;
            try
            {
                _g.DrawString(str, ((FontAdapter)font).Font, xBrush, xPoint.X, xPoint.Y, _stringFormat, xLetterSpacing, ToGlyphPalette(fontPalette), features ?? ShapeSettings.Default, logicalText);
            }
            finally
            {
                _g.InvisibleText = false;
            }
        }

        public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color)
        {
            if (InvisibleText)
                return;

            var xBrush = ((BrushAdapter)_adapter.GetSolidBrush(color)).Brush;
            var positioned = new (int GlyphIndex, double X, double Y)[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];
                var point = Utils.Convert(new RPoint(glyph.X, glyph.Y), PixelsPerPoint);
                positioned[i] = (glyph.GlyphIndex, point.X, point.Y);
            }

            _g.DrawGlyphsAtPositions(positioned, ((FontAdapter)font).Font, xBrush);
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

        public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) =>
            TextOutlineBuilder.Build(GetGraphicsPath(), ((FontAdapter)font).Font, PixelsPerPoint, str, baselineOrigin, letterSpacing,
                features ?? ShapeSettings.Default);

        public override IReadOnlyList<RInkSpan>? GetInkCrossings(
            string str, RFont font, RPoint origin, double bandTop, double bandBottom,
            double letterSpacing = 0, ShapeSettings? features = null)
        {
            var realFont = ((FontAdapter)font).Font;
            var typeface = realFont.Typeface;
            if (typeface.Metrics.UnitsPerEm == 0 || bandBottom <= bandTop)
                return null;

            // The baseline this run is actually painted at. Deliberately recomputed here from the font's
            // own metrics, exactly as XGraphicsPdfRenderer.DrawString does, rather than taken as
            // `origin.Y + RFont.Ascent`: that property rounds to a whole unit (FontAdapter.Ascent), and
            // an underline's band is one unit tall at the default thickness, so borrowing the rounded
            // value would shift the band by up to half its own height and flip whether a glyph that just
            // grazes the line is skipped.
            var baselineY = origin.Y + realFont.GetHeight() * realFont.CellAscent / realFont.CellSpace * PixelsPerPoint;

            // Measured relative to the run's own origin and baseline, so the same word on a later line -
            // with the same band at a different absolute y - is a cache hit rather than a second full
            // shape-and-decode. Underlined prose repeats words heavily, and this call is otherwise the
            // most expensive thing a decorated line does.
            var key = new InkCrossingKey(realFont, str, bandTop - baselineY, bandBottom - baselineY,
                letterSpacing, features ?? ShapeSettings.Default);

            if (!_inkCrossings.TryGetValue(key, out var relative))
            {
                relative = MeasureInkCrossings(typeface, realFont, str, key, PixelsPerPoint);
                _inkCrossings[key] = relative;
            }

            if (relative is null) return null;
            if (relative.Count == 0) return [];

            var spans = new RInkSpan[relative.Count];
            for (var i = 0; i < relative.Count; i++)
            {
                spans[i] = new RInkSpan(relative[i].Start + origin.X, relative[i].End + origin.X);
            }

            return spans;
        }

        /// <summary>
        /// <see cref="GetInkCrossings"/>'s actual measurement, in coordinates relative to the run's own
        /// origin and baseline - the form <see cref="_inkCrossings"/> caches. Null means no glyph in the
        /// run had a decodable outline at all.
        /// </summary>
        private static List<RInkSpan>? MeasureInkCrossings(
            Typeface typeface, XFont realFont, string str, in InkCrossingKey key,
            double pixelsPerPoint)
        {
            // Same design-units-to-user-space scale GetTextOutline resolves; see its own remarks. The
            // em-square is y-up and user space is y-down, so the band's top edge is the HIGH design y.
            var scale = realFont.Size * pixelsPerPoint / typeface.Metrics.UnitsPerEm;
            if (scale <= 0) return null;

            List<RInkSpan> spans = [];
            var sawOutline = false;
            double penX = 0;

            foreach (var glyph in Shaper.Shape(typeface, str, key.Features).Glyphs)
            {
                var glyphId = glyph.GlyphIndex;

                if (typeface.TryGetOutline((ushort)glyphId, out var outline))
                {
                    sawOutline = true;

                    // GPOS positioning shifts where this glyph paints without changing its outline -
                    // exactly as GetTextOutline applies it, so ink is measured where it is drawn. A mark
                    // attached with a negative XOffset therefore lands left of the base it follows, which
                    // is why the whole list is sorted and merged below rather than assumed ordered.
                    var glyphX = penX + glyph.XOffset * scale;
                    var glyphY = -glyph.YOffset * scale;

                    var crossings = outline.Crossings(
                        (glyphY - key.BandBottom) / scale, (glyphY - key.BandTop) / scale);

                    // One span per glyph, hulling everything the glyph puts in the band, rather than one
                    // span per ink run. CSS Text Decoration 4 §2.10.5 leaves the skip shape to the UA and
                    // names this exact choice - "whether to show the line within enclosed areas of a
                    // glyph" - noting that hiding it "gives a cleaner look to the type" and that following
                    // each contour can leave "typographically-awkward wisps of underline". Per-run spans
                    // produced precisely those wisps: a stub of underline stranded inside the bowl of a
                    // 'g' or the counter of an 'o'. Both Chrome and Firefox hull per glyph - measured on
                    // 'o', 'g', 'n', 'v', 'H' and U+2026, whose three separate dots become a single gap in
                    // both - so this is also what a document author will have proofed against.
                    //
                    // Crossings is sorted and disjoint, so its first start and last end are the extremes.
                    if (crossings.Count > 0)
                    {
                        spans.Add(new RInkSpan(
                            glyphX + crossings[0].Start * scale,
                            glyphX + crossings[^1].End * scale));
                    }
                }

                penX += (typeface.GetAdvance((ushort)glyphId) + glyph.XAdvanceDelta) * scale + key.LetterSpacing;
            }

            // No glyph in the run had a decodable outline at all - a CFF/bitmap font, or a run of
            // nothing but spaces. Null rather than an empty list, so the caller can tell "no ink
            // information" from "this run genuinely crosses nothing"; see RGraphics.GetInkCrossings.
            if (!sawOutline) return null;

            return MergeSpans(spans);
        }

        /// <summary>
        /// <paramref name="spans"/> sorted left to right and unioned, so the result honours
        /// <see cref="RGraphics.GetInkCrossings"/>'s documented contract regardless of the order the
        /// glyph walk produced them in.
        /// </summary>
        private static List<RInkSpan> MergeSpans(List<RInkSpan> spans)
        {
            if (spans.Count <= 1) return spans;

            spans.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            List<RInkSpan> merged = [spans[0]];

            for (var i = 1; i < spans.Count; i++)
            {
                var last = merged[^1];
                var next = spans[i];

                if (next.Start <= last.End)
                {
                    merged[^1] = new RInkSpan(last.Start, Math.Max(last.End, next.End));
                }
                else
                {
                    merged.Add(next);
                }
            }

            return merged;
        }

        /// <summary>
        /// What one <see cref="GetInkCrossings"/> answer depends on, once the run's absolute position is
        /// factored out: the font, the text, the band relative to the baseline, and how the run is shaped.
        /// </summary>
        private readonly record struct InkCrossingKey(
            XFont Font, string Text, double BandTop, double BandBottom, double LetterSpacing,
            ShapeSettings Features);

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

            // width/height arrive in this adapter's own "inflated" layout-unit space (the caller always
            // sizes a tile from a layout rect - a box's own clip, a background layer's resolved size, an
            // SVG filter region - the same space every other RGraphics call operates in), but an XForm's
            // /BBox is a real PDF construct measured in actual page points with no conversion of its own.
            // Divide by PixelsPerPoint here so the form's declared size already matches what the content
            // painted into it will occupy once ITS OWN drawing calls apply this same division (every
            // GraphicsAdapter method does, via Utils.Convert). Skipping this - as an earlier version did -
            // left the /BBox sized in undivided layout units while the content inside was already
            // correctly point-sized; XGraphicsPdfRenderer.DrawImage's own placement scale
            // (destRect.Width / image.PointWidth) then divided the whole form by PixelsPerPoint a SECOND
            // time on top of that (image.PointWidth reading back the oversized BBox), visibly shrinking
            // and mispositioning every tiled box - opacity<1, and now filter/mix-blend-mode/SVG
            // pattern/mask/filter content, all of which paint through a tile - whenever PixelsPerPoint
            // differs from 1 (ShrinkToFit/ScaleToPageSize, or a non-72 PixelsPerInch). A sibling that
            // never needed a tile (e.g. a plain box painted directly onto the page) was never subject to
            // this second division, so it rendered correctly while its tiled neighbors visibly shrank and
            // drifted toward the page origin - reading, at a glance, as if the plain box were the one
            // that had gone wrong.
            var form = new XForm(document, new XSize(width / PixelsPerPoint, height / PixelsPerPoint));
            var formGraphics = XGraphics.FromForm(form);
            // releaseGraphics: true - disposing the returned tile RGraphics must dispose the
            // underlying XGraphics, which is what actually calls XForm.Finish() and closes out the
            // Form XObject's content stream (see XGraphics.Dispose()). Without this, the tile's
            // drawing commands would never get flushed into the PDF at all.
            var tileGraphics = new GraphicsAdapter(_adapter, formGraphics, PixelsPerPoint, releaseGraphics: true);
            return (tileGraphics, new ImageAdapter(form));
        }

        internal override RasterSurfaceScope? BeginRasterSurface(RRect layoutBounds, double? dpiOverride = null)
        {
            var scope = RasterSurfaceFactory.Create(_adapter, PixelsPerPoint, layoutBounds, dpiOverride ?? _adapter.RasterizationDpi, _adapter.MaxRasterPixels, TransformScale);
            scope?.Graphics.SeedTransform(_accumulated);
            return scope;
        }

        internal override bool FlattensTransparency =>
            _g.Owner is { } owner && owner.Options.FlattenTransparency &&
            (owner.Options.PdfAConformance is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A ||
             owner.Options.PdfXConformance is PdfXConformance.X1a or PdfXConformance.X3);

        private TransparencyProbe? _probe;

        internal override TransparencyProbe? CreateTransparencyProbe() => _probe ??= new TransparencyProbe(_adapter, PixelsPerPoint);

        internal override void DrawRaster(RasterSurface surface)
        {
            // A bitmap with soft edges needs an image soft mask (/SMask), a transparency construct PDF/A-1 and
            // PDF/X-1a/X-3 forbid. Rejected up front, with a message naming the CSS feature rather than the
            // image, like every other transparency-requiring paint path.
            // A surface with no soft edge at all (a flattened region, or a backdrop over opaque paper) embeds without an alpha plane
            // and needs none of that.
            var opaque = RasterEmbedder.IsOpaque(surface);
            if (!opaque && _g.Owner is { } document)
            {
                PdfATransparencyGuard.RequireAllowed(document,
                    "An effect PeachPDF renders as a bitmap (a CSS filter such as blur() or grayscale(), a shadow with a blur radius, or an SVG filter with a blur, lighting or other pixel primitive)");
            }

            // PDF/X-1a allows only CMYK content, so an opaque bitmap goes in as DeviceCMYK there.
            var asCmyk = opaque && _g.Owner?.Options.PdfXConformance == PdfXConformance.X1a;

            // The image is placed at the surface's own snapped layout rectangle, converted to points once here
            // (the same division every other draw call makes), so its physical size is exact.
            var richBlack = _g.Owner?.Options.ColorOptions?.BlackGeneration == ColorBlackGeneration.UseRichBlack;
            _g.DrawImage(RasterEmbedder.ToXImage(surface, asCmyk, richBlack), Utils.Convert(surface.LayoutRect, PixelsPerPoint));
        }

        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect)
        {
            if (((ImageAdapter)image).Image is XForm imageForm && ((ImageAdapter)maskImage).Image is XForm maskForm)
                _g.DrawImageMasked(imageForm, maskForm, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal)
        {
            if (((ImageAdapter)image).Image is XForm imageForm)
                _g.DrawImageWithOpacity(imageForm, Utils.Convert(destRect, PixelsPerPoint), opacity, blendMode.ToString());
        }

        public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix)
        {
            if (((ImageAdapter)image).Image is XForm imageForm)
                _g.DrawImageWithColorMatrix(imageForm, Utils.Convert(destRect, PixelsPerPoint), matrix);
        }

        public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false)
        {
            if (((ImageAdapter)image).Image is XForm imageForm && ((ImageAdapter)maskImage).Image is XForm maskForm)
                _g.DrawImageAlphaMasked(imageForm, maskForm, Utils.Convert(destRect, PixelsPerPoint), invert);
        }

        public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode)
        {
            if (((ImageAdapter)top).Image is XForm topForm && ((ImageAdapter)bottom).Image is XForm bottomForm)
                _g.DrawImageBlendedOver(topForm, bottomForm, Utils.Convert(destRect, PixelsPerPoint), blendMode.ToString());
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

        public override void BeginVariableText()
        {
            _g.BeginVariableText();
        }

        public override void EndVariableText()
        {
            _g.EndVariableText();
        }

        public override void Dispose()
        {
            _probe?.Dispose();
            _probe = null;

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
            var naturalWidth = image.Width;
            var naturalHeight = image.Height;

            if (naturalWidth <= 0 || naturalHeight <= 0 || srcRect.Width <= 0 || srcRect.Height <= 0)
                return;

            if (IsWholeImage(srcRect, naturalWidth, naturalHeight))
            {
                DrawWhole(image, destRect);
                return;
            }

            // PDF has no "draw this sub-rectangle of an XObject" operator, and PdfSharpCore's own
            // srcRect overload never implemented one either - it silently drew the whole image into
            // destRect, so every border-image slice painted the entire source squashed into its own
            // region rather than the region's own ninth of it. Crop the only way the imaging model
            // allows: clip to destRect, then place the WHOLE image at the scale/offset that lands
            // srcRect exactly on destRect.
            var placement = ComputeCroppedPlacement(destRect, srcRect, naturalWidth, naturalHeight);

            PushClip(destRect);
            try
            {
                DrawWhole(image, placement);
            }
            finally
            {
                PopClip();
            }
        }

        /// <summary>
        /// Draws all of <paramref name="image"/> into <paramref name="rect"/>, through the same
        /// <see cref="XGraphics"/> call every un-cropped draw has always used - so a whole-image draw
        /// still emits exactly the operators it did before cropping existed, and the cropped draw's own
        /// placement emits the same shape.
        /// </summary>
        private void DrawWhole(RImage image, RRect rect)
        {
            var xImage = ((ImageAdapter)image).Image;
            _g.DrawImage(xImage, Utils.Convert(rect, PixelsPerPoint),
                new XRect(0, 0, xImage.PointWidth, xImage.PointHeight), XGraphicsUnit.Point);
        }

        /// <summary>
        /// True when <paramref name="srcRect"/> selects the image in full (every background layer's own
        /// draw does, <c>BackgroundImageDrawHandler</c> passing <c>(0, 0, image.Width, image.Height)</c>),
        /// so the draw needs neither a clip nor an off-destination placement.
        /// </summary>
        private static bool IsWholeImage(RRect srcRect, double naturalWidth, double naturalHeight)
        {
            const double epsilon = 0.001;
            return srcRect.X <= epsilon && srcRect.Y <= epsilon &&
                   srcRect.Width >= naturalWidth - epsilon && srcRect.Height >= naturalHeight - epsilon;
        }

        /// <summary>
        /// The rectangle the whole image must be drawn into so that its <paramref name="srcRect"/> portion -
        /// in the image's own natural units, as <see cref="RImage.Width"/>/<see cref="RImage.Height"/>
        /// report them (pixels for a raster, points for an <see cref="XForm"/> tile) - covers
        /// <paramref name="destRect"/> exactly. Clipping to <paramref name="destRect"/> then leaves only
        /// that portion visible. Exposed (not private) so the arithmetic can be asserted directly.
        /// </summary>
        internal static RRect ComputeCroppedPlacement(RRect destRect, RRect srcRect, double naturalWidth, double naturalHeight)
        {
            var scaleX = destRect.Width / srcRect.Width;
            var scaleY = destRect.Height / srcRect.Height;

            return new RRect(
                destRect.X - srcRect.X * scaleX,
                destRect.Y - srcRect.Y * scaleY,
                naturalWidth * scaleX,
                naturalHeight * scaleY);
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
