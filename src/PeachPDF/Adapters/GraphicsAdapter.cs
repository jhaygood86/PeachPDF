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
using PeachDrawing.Core;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachDrawing;
using PeachPDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Adapter for WinForms Graphics for core.
    /// </summary>
    internal sealed class GraphicsAdapter : Canvas, ITransparencyProbeSource
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
        private readonly Dictionary<InkCrossingKey, List<InkSpan>?> _inkCrossings = [];

        public override double PixelsPerPoint { get; }

        public override object? TileCacheOwner => _g.Owner;

        /// <summary>
        /// _releaseGraphics is set true exactly for tile-backed instances (see the constructor
        /// comment and CreateTile below), making it the same signal as "paints into an offscreen
        /// tile" - see Canvas.IsOffscreenTile.
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
        public GraphicsAdapter(RenderContext adapter, XGraphics g, double pixelsPerPoint, bool releaseGraphics = false)
            : base(adapter, new Rect(0, 0, double.MaxValue, double.MaxValue))
        {
            ArgumentNullException.ThrowIfNull(g);

            _g = g;
            _releaseGraphics = releaseGraphics;

            PixelsPerPoint = pixelsPerPoint;
            _previousSvgGlyphPainter = _g.SvgGlyphPainter;
            _g.SvgGlyphPainter = new SvgGlyphPainter(this, adapter);
        }

        private readonly PeachDrawing.Core.ISvgGlyphPainter? _previousSvgGlyphPainter;

        /// <summary>The adapter this graphics draws for, which resolves the fonts and images of what is drawn.</summary>
        internal RenderContext Adapter => _adapter;

        public override void PopClip()
        {
            _clipStack.Pop();
            _g.Restore();
        }

        public override void PushClip(Rect rect)
        {
            _clipStack.Push(rect);
            _g.Save();
            _g.IntersectClip(Utils.Convert(rect, PixelsPerPoint));
        }

        public override void PushClip(GraphicsPath path)
        {
            // No simple bounding rectangle for an arbitrary path, so keep the tracked clip bound
            // conservative (unchanged) - it's only used for culling, and an over-wide bound never
            // hides content that should actually be visible.
            _clipStack.Push(_clipStack.Peek());
            _g.Save();
            _g.IntersectClip(((GraphicsPathAdapter)path).GraphicsPath);
        }

        public override void PushClipExclude(Rect rect)
        { }

        // The accumulated pushed transforms, so a raster region can pick a pixel pitch that is right after the transforms are
        // applied (from the linear part) and an SVG backdrop repaint can map between coordinate spaces (the whole matrix).
        private readonly Stack<Matrix3x2> _transformStack = [];
        private Matrix3x2 _accumulated = Matrix3x2.Identity;

        public override Matrix3x2 CurrentTransform => _accumulated;

        /// <summary>Seeds <see cref="CurrentTransform"/> for a freshly created tile - see <see cref="Canvas.CreateTile"/>'s
        /// doc remarks for why. Bookkeeping only: the tile's own native PDF graphics state (<see cref="_g"/>) still starts at
        /// its own identity, so this has no effect on what actually gets drawn into it.</summary>
        internal void SeedTransform(Matrix3x2 requester) => _accumulated = requester;

        public override (double X, double Y) TransformScale
        {
            get
            {
                var x = Math.Sqrt(_accumulated.M11 * _accumulated.M11 + _accumulated.M12 * _accumulated.M12);
                var y = Math.Sqrt(_accumulated.M21 * _accumulated.M21 + _accumulated.M22 * _accumulated.M22);
                return (x > 1e-6 ? x : 1.0, y > 1e-6 ? y : 1.0);
            }
        }

        public override void PushTransform(Matrix3x2 matrix)
        {
            _transformStack.Push(_accumulated);
            _accumulated = matrix.Then(_accumulated);

            _g.Save();
            _g.MultiplyTransform(new XMatrix(
                matrix.M11, matrix.M12, matrix.M21, matrix.M22,
                matrix.M31 / PixelsPerPoint, matrix.M32 / PixelsPerPoint));
        }

        public override void PopTransform()
        {
            if (_transformStack.Count > 0)
                _accumulated = _transformStack.Pop();

            _g.Restore();
        }

        public override void PushBlendMode(PaintBlendMode mode)
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

        public override Size MeasureString(string str, Font font, ShapeSettings? features = null)
        {
            var realFont = ((FontAdapter)font).Font;
            var size = _g.MeasureString(str, realFont, _stringFormat, features ?? ShapeSettings.Default);
            return Utils.Convert(size, PixelsPerPoint);
        }

        public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null)
        {
            // Real production fonts always resolve a typeface (FontAdapter.Typeface never returns null);
            // only a test double's font stub can, and this PDF-writing path never sees one of those.
            return Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count;
        }

        public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
        {
            // there is no need for it - used for text selection
            throw new NotSupportedException();
        }

        public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null) =>
            DrawString(str, font, color, point, size, letterSpacing, fontPalette, features, logicalText: null);

        /// <summary>See <see cref="Canvas.DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?, string?)"/>'s
        /// own remarks for <paramref name="logicalText"/> - threaded straight through to
        /// <see cref="XGraphics.DrawString(string, XFont, XBrush, double, double, XStringFormat, double, XGlyphPalette?, ShapeSettings?, string?)"/>,
        /// the one real PDF-writing path that acts on it.</summary>
        public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing, FontPalette? fontPalette, ShapeSettings? features, string? logicalText)
        {
            // Invisible text paints nothing, so its colour is irrelevant - and an opaque one keeps it clear of the alpha and
            // colour-space guards a real colour would pass through.
            var xBrush = ToXBrush(_adapter.GetSolidBrush(InvisibleText ? PaintColor.Black : color));
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

        public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color)
        {
            if (InvisibleText)
                return;

            var xBrush = ToXBrush(_adapter.GetSolidBrush(color));
            var positioned = new (int GlyphIndex, double X, double Y)[glyphs.Count];
            for (var i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];
                var point = Utils.Convert(new PaintPoint(glyph.X, glyph.Y), PixelsPerPoint);
                positioned[i] = (glyph.GlyphIndex, point.X, point.Y);
            }

            _g.DrawGlyphsAtPositions(positioned, ((FontAdapter)font).Font, xBrush);
        }

        /// <summary>
        /// Converts a resolved <see cref="FontPalette"/> (adapter layer, <see cref="PaintColor"/> overrides) into the
        /// backend <see cref="XGlyphPalette"/> (<see cref="XColor"/> overrides). Null passes straight through.
        /// </summary>
        private static XGlyphPalette? ToGlyphPalette(FontPalette? palette)
        {
            if (palette is null)
                return null;

            var overrides = new Dictionary<int, XColor>(palette.Overrides.Count);
            foreach (var (entryIndex, color) in palette.Overrides)
                overrides[entryIndex] = XColor.FromArgb(color.A, color.R, color.G, color.B);

            return new XGlyphPalette(palette.BasePaletteIndex, overrides);
        }

        public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) =>
            TextOutlineBuilder.Build(GetGraphicsPath(), font, PixelsPerPoint, str, baselineOrigin, letterSpacing,
                features ?? ShapeSettings.Default);

        public override IReadOnlyList<InkSpan>? GetInkCrossings(
            string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
            double letterSpacing = 0, ShapeSettings? features = null)
        {
            var realFont = ((FontAdapter)font).Font;
            var typeface = realFont.Typeface;
            if (typeface.Metrics.UnitsPerEm == 0 || bandBottom <= bandTop)
                return null;

            // The baseline this run is actually painted at. Deliberately recomputed here from the font's
            // own metrics, exactly as XGraphicsPdfRenderer.DrawString does, rather than taken as
            // `origin.Y + Font.Ascent`: that property rounds to a whole unit (FontAdapter.Ascent), and
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

            var spans = new InkSpan[relative.Count];
            for (var i = 0; i < relative.Count; i++)
            {
                spans[i] = new InkSpan(relative[i].Start + origin.X, relative[i].End + origin.X);
            }

            return spans;
        }

        /// <summary>
        /// <see cref="GetInkCrossings"/>'s actual measurement, in coordinates relative to the run's own origin and baseline - the form
        /// <see cref="_inkCrossings"/> caches. Null means no glyph in the run had a decodable outline at all.
        /// </summary>
        private static List<InkSpan>? MeasureInkCrossings(
            Typeface typeface, XFont realFont, string str, in InkCrossingKey key,
            double pixelsPerPoint)
        {
            // Same design-units-to-user-space scale GetTextOutline resolves; see its own remarks.
            var scale = realFont.Size * pixelsPerPoint / typeface.Metrics.UnitsPerEm;
            return InkCrossings.Measure(typeface, str, scale, key.BandTop, key.BandBottom, key.LetterSpacing, key.Features) is { } spans
                ? [.. spans]
                : null;
        }

        /// <summary>
        /// What one <see cref="GetInkCrossings"/> answer depends on, once the run's absolute position is
        /// factored out: the font, the text, the band relative to the baseline, and how the run is shaped.
        /// </summary>
        private readonly record struct InkCrossingKey(
            XFont Font, string Text, double BandTop, double BandBottom, double LetterSpacing,
            ShapeSettings Features);

        public override GraphicsPath GetGraphicsPath()
        {
            return new GraphicsPathAdapter();
        }

        public override (Canvas Graphics, Image Image)? CreateTile(double width, double height)
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
            // SVG filter region - the same space every other Canvas call operates in), but an XForm's
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
            // releaseGraphics: true - disposing the returned tile Canvas must dispose the
            // underlying XGraphics, which is what actually calls XForm.Finish() and closes out the
            // Form XObject's content stream (see XGraphics.Dispose()). Without this, the tile's
            // drawing commands would never get flushed into the PDF at all.
            var tileGraphics = new GraphicsAdapter(_adapter, formGraphics, PixelsPerPoint, releaseGraphics: true);
            // See CreateTile's own doc remarks: seeding CurrentTransform (bookkeeping only, see SeedTransform)
            // lets a reader inside the tile (a gradient/pattern reaching it through context-fill/context-stroke,
            // chiefly) relate the tile's coordinate space back to whatever space content outside it is measured
            // in - the tile's own drawing commands are unaffected, since the form's native PDF graphics state
            // (formGraphics, just created above) starts at its own identity regardless.
            tileGraphics.SeedTransform(_accumulated);
            return (tileGraphics, new ImageAdapter(form));
        }

        protected override bool SupportsLayerEffects => true;

        protected override void ApplyLayerEffects(RasterSurface surface, IReadOnlyList<LayerEffect> effects) =>
            RasterLayerEffects.Apply(surface, effects);

        public override RasterRegion? BeginRasterSurface(Rect layoutBounds, double? dpiOverride = null) =>
            RasterSurfaceFactory.Create(_adapter, PixelsPerPoint, layoutBounds, dpiOverride ?? _adapter.RasterizationDpi, _adapter.MaxRasterPixels, TransformScale, _accumulated);

        public override bool FlattensTransparency =>
            _g.Owner is { } owner && owner.Options.FlattenTransparency &&
            (owner.Options.PdfAConformance is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A ||
             owner.Options.PdfXConformance is PdfXConformance.X1a or PdfXConformance.X3);

        private TransparencyProbe? _probe;

        public TransparencyProbe CreateTransparencyProbe() => _probe ??= new TransparencyProbe(_adapter, PixelsPerPoint);

        public override void DrawRaster(RasterSurface surface)
        {

            // A bitmap with soft edges needs an image soft mask (/SMask), a transparency construct PDF/A-1 and
            // PDF/X-1a/X-3 forbid. Rejected up front, with a message naming the CSS feature rather than the
            // image, like every other transparency-requiring paint path.
            // A surface with no soft edge at all (a flattened region, or a backdrop over opaque paper) embeds without an alpha plane
            // and needs none of that.
            var opaque = RasterEmbedding.IsOpaque(surface);
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
            _g.DrawImage(RasterEmbedding.ToXImage(surface, asCmyk, richBlack), Utils.Convert(surface.LayoutRect, PixelsPerPoint));
        }

        public override void DrawImageMasked(Image image, Image maskImage, Rect destRect)
        {
            if (((ImageAdapter)image).Image is XForm imageForm && ((ImageAdapter)maskImage).Image is XForm maskForm)
                _g.DrawImageMasked(imageForm, maskForm, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal)
        {
            if (((ImageAdapter)image).Image is XForm imageForm)
                _g.DrawImageWithOpacity(imageForm, Utils.Convert(destRect, PixelsPerPoint), opacity, blendMode.ToString());
        }

        public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix)
        {
            if (((ImageAdapter)image).Image is XForm imageForm)
                _g.DrawImageWithColorMatrix(imageForm, Utils.Convert(destRect, PixelsPerPoint), matrix);
        }

        public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false)
        {
            if (((ImageAdapter)image).Image is XForm imageForm && ((ImageAdapter)maskImage).Image is XForm maskForm)
                _g.DrawImageAlphaMasked(imageForm, maskForm, Utils.Convert(destRect, PixelsPerPoint), invert);
        }

        public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode)
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

            // Graphics wrapped one after another over one XGraphics: what draws SVG glyphs goes back to the adapter that had it.
            if (_g.SvgGlyphPainter is SvgGlyphPainter own && ReferenceEquals(own.Host, this))
                _g.SvgGlyphPainter = _previousSvgGlyphPainter;

            if (_releaseGraphics)
                _g.Dispose();
        }


        #region Delegate graphics methods

        public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2)
        {
            _g.DrawLine(ToXPen(pen), x1 / PixelsPerPoint, y1 / PixelsPerPoint, x2 / PixelsPerPoint, y2 / PixelsPerPoint);
        }

        public override void DrawRectangle(Pen pen, double x, double y, double width, double height)
        {
            _g.DrawRectangle(ToXPen(pen), x / PixelsPerPoint, y / PixelsPerPoint, width / PixelsPerPoint, height / PixelsPerPoint);
        }

        public override void DrawRectangle(Brush brush, double x, double y, double width, double height)
        {
            var xBrush = ToXBrush(brush);
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

        public override void DrawImage(Image image, Rect destRect, Rect srcRect)
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
        private void DrawWhole(Image image, Rect rect)
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
        private static bool IsWholeImage(Rect srcRect, double naturalWidth, double naturalHeight)
        {
            const double epsilon = 0.001;
            return srcRect.X <= epsilon && srcRect.Y <= epsilon &&
                   srcRect.Width >= naturalWidth - epsilon && srcRect.Height >= naturalHeight - epsilon;
        }

        /// <summary>
        /// The rectangle the whole image must be drawn into so that its <paramref name="srcRect"/> portion -
        /// in the image's own natural units, as <see cref="Image.Width"/>/<see cref="Image.Height"/>
        /// report them (pixels for a raster, points for an <see cref="XForm"/> tile) - covers
        /// <paramref name="destRect"/> exactly. Clipping to <paramref name="destRect"/> then leaves only
        /// that portion visible. Exposed (not private) so the arithmetic can be asserted directly.
        /// </summary>
        internal static Rect ComputeCroppedPlacement(Rect destRect, Rect srcRect, double naturalWidth, double naturalHeight)
        {
            var scaleX = destRect.Width / srcRect.Width;
            var scaleY = destRect.Height / srcRect.Height;

            return new Rect(
                destRect.X - srcRect.X * scaleX,
                destRect.Y - srcRect.Y * scaleY,
                naturalWidth * scaleX,
                naturalHeight * scaleY);
        }

        public override void DrawImage(Image image, Rect destRect)
        {
            _g.DrawImage(((ImageAdapter)image).Image, Utils.Convert(destRect, PixelsPerPoint));
        }

        public override void DrawPath(Pen pen, GraphicsPath path)
        {
            var xPen = ToXPen(pen);
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

        public override void DrawPath(Brush brush, GraphicsPath path)
        {
            var xBrush = ToXBrush(brush);
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

        public override void DrawPolygon(Brush brush, PaintPoint[] points)
        {
            if (points is { Length: > 0 })
            {
                _g.DrawPolygon(ToXBrush(brush), Utils.Convert(points, PixelsPerPoint), XFillMode.Winding);
            }
        }

        #endregion

        #region Brush/pen translation

        /// <summary>
        /// Builds this backend's own <see cref="XBrush"/> from a self-describing <see cref="Brush"/> -
        /// the PDF-specific half of the split <see cref="RenderContext"/>'s brush factories used to do
        /// themselves before brushes became plain, backend-agnostic data (see <see cref="Brush"/>'s own
        /// remarks). Not cached: a brush is typically drawn once or a handful of times, and a real PDF
        /// shading brush is cheap to construct.
        /// </summary>
        private XBrush ToXBrush(Brush brush) => brush switch
        {
            SolidBrush solid => ToXSolidBrush(solid.PaintColor),
            LinearGradientBrush linear => new XLinearGradientBrush(
                Utils.Convert(linear.Start, PixelsPerPoint), Utils.Convert(linear.End, PixelsPerPoint),
                linear.Stops.Select(s => Utils.Convert(s.PaintColor)).ToArray(),
                linear.Stops.Select(s => s.Position).ToArray())
            { IsRepeating = linear.Spread == GradientSpread.Repeat },
            RadialGradientBrush radial => new XRadialGradientBrush(
                Utils.Convert(radial.Center, PixelsPerPoint), radial.RadiusX / PixelsPerPoint, radial.RadiusY / PixelsPerPoint,
                radial.Stops.Select(s => Utils.Convert(s.PaintColor)).ToArray(),
                radial.Stops.Select(s => s.Position).ToArray(),
                Utils.Convert(radial.Focus, PixelsPerPoint))
            { IsRepeating = radial.Spread == GradientSpread.Repeat },
            ConicGradientBrush conic => new XConicGradientBrush(
                Utils.Convert(conic.Center, PixelsPerPoint), conic.OuterRadius / PixelsPerPoint,
                conic.Stops.Select(s => Utils.Convert(s.PaintColor)).ToArray(),
                conic.AnglesRadians.ToArray()),
            _ => throw new NotSupportedException($"Unknown brush type {brush.GetType()}"),
        };

        /// <summary>Reuses PdfSharpCore's built-in static brushes for the common opaque black/white/transparent
        /// cases, the same optimization <c>PdfSharpAdapter.CreateSolidBrush</c> used to apply.</summary>
        private static XBrush ToXSolidBrush(PaintColor color)
        {
            if (color == PaintColor.White)
                return XBrushes.White;
            if (color == PaintColor.Black)
                return XBrushes.Black;
            if (color.A < 1)
                return XBrushes.Transparent;
            return new XSolidBrush(Utils.Convert(color));
        }

        /// <summary>
        /// Builds this backend's own <see cref="XPen"/> from a self-describing <see cref="Pen"/> - the
        /// PDF-specific half of what <c>PenAdapter</c> used to do directly.
        /// </summary>
        /// <remarks>
        /// A solid-colour pen goes through <see cref="XPen"/>'s <c>XColor</c> constructor rather than its
        /// <c>XBrush</c> one, and must: <c>PdfGraphicsState.RealizePen</c> (the PDF writer's own
        /// stroke-alpha/colour realization) reads <c>pen.PaintColor</c> - the <c>_color</c> field the
        /// <c>XColor</c> constructor sets - not anything derived from <c>pen.Brush</c>, so a solid pen
        /// built via the brush constructor would keep <c>_color</c>'s default (opaque) value and silently
        /// lose the colour's real alpha (e.g. from an SVG <c>stroke-opacity</c>). Only a gradient-painted
        /// pen (<see cref="RenderContext.GetPen(Brush)"/>, e.g. SVG <c>stroke="url(#gradient)"</c>) needs the
        /// brush constructor - mirroring the split PdfSharpAdapter's own <c>CreatePen(PaintColor)</c>/
        /// <c>CreatePen(Brush)</c> used to make explicitly, before both collapsed into this one method.
        /// </remarks>
        private XPen ToXPen(Pen pen)
        {
            var xPen = pen.Paint is SolidBrush solid
                ? new XPen(Utils.Convert(solid.PaintColor), pen.Width)
                : new XPen(ToXBrush(pen.Paint), pen.Width);

            xPen.MiterLimit = pen.MiterLimit;
            xPen.LineCap = pen.LineCap switch
            {
                LineCap.Round => XLineCap.Round,
                LineCap.Square => XLineCap.Square,
                _ => XLineCap.Flat,
            };
            xPen.LineJoin = pen.LineJoin switch
            {
                LineJoin.Round => XLineJoin.Round,
                LineJoin.Bevel => XLineJoin.Bevel,
                _ => XLineJoin.Miter,
            };

            switch (pen.DashStyle)
            {
                case DashStyle.Solid:
                    xPen.DashStyle = XDashStyle.Solid;
                    break;
                case DashStyle.Dash:
                    xPen.DashStyle = XDashStyle.Dash;
                    if (pen.Width < 2)
                        xPen.DashPattern = [4, 4]; // better looking
                    break;
                case DashStyle.Dot:
                    xPen.DashStyle = XDashStyle.Dot;
                    break;
                case DashStyle.DashDot:
                    xPen.DashStyle = XDashStyle.DashDot;
                    break;
                case DashStyle.DashDotDot:
                    xPen.DashStyle = XDashStyle.DashDotDot;
                    break;
                case DashStyle.Custom when pen.Width > 0:
                    // XPen's custom dash array is expressed as multiples of pen width (a GDI+ convention -
                    // see PdfGraphicsState.RealizePen, which multiplies each entry by pen._width when
                    // writing the PDF "d" operator), whereas SVG's stroke-dasharray/stroke-dashoffset are
                    // absolute user-space lengths - normalize by dividing through by the pen's width.
                    xPen.DashStyle = XDashStyle.Custom;
                    xPen.DashPattern = pen.DashPattern.Select(v => v / pen.Width).ToArray();
                    xPen.DashOffset = pen.DashOffset / pen.Width;
                    break;
                default:
                    xPen.DashStyle = XDashStyle.Solid;
                    break;
            }

            return xPen;
        }

        #endregion
    }
}
