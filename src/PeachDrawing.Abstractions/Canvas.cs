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

using PeachDrawing.Text.OpenType;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Abstractions
{
    /// <summary>
    /// Adapter for platform specific graphics rendering object - used to render graphics and text in platform specific context.<br/>
    /// The core HTML Renderer components use this class for rendering logic, extending this
    /// class in different platform: WinForms, WPF, Metro, PDF, etc.
    /// </summary>
    public abstract class Canvas : IDisposable
    {
        #region Fields/Consts

        /// <summary>
        /// the global adapter
        /// </summary>
        protected readonly RenderContext _adapter;

        /// <summary>
        /// The clipping bound stack as clips are pushed/poped to/from the graphics
        /// </summary>
        protected readonly Stack<Rect> _clipStack = [];

        /// <summary>
        /// The suspended clips
        /// </summary>
        private readonly Stack<Rect> _suspendedClips = [];

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        protected Canvas(RenderContext adapter, Rect initialClip)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _adapter = adapter;
            _clipStack.Push(initialClip);
        }

        /// <summary>
        /// The scale between this graphics context's own coordinate space (matching
        /// <c>Core.Dom.CssBox</c> geometry - <c>PdfGenerateConfig.PixelsPerInch / 72</c>-inflated
        /// relative to a true PDF point, see <c>HtmlContainer.PageSize</c>) and true PDF points.
        /// <c>1.0</c> (the base, no-op default) is correct for every test-only <see cref="Canvas"/>
        /// mock, which never inflates its own coordinate space this way; <c>PeachPDF.Adapters.
        /// GraphicsAdapter</c> - the only implementation that actually paints a PDF - overrides this with
        /// its real, adapter-driven value. A caller that needs to compute a transform matrix whose
        /// *linear* (scale/rotation) part won't itself be divided by this factor before use -
        /// <c>GraphicsAdapter.PushTransform</c> only divides a matrix's translation, not
        /// <c>M11</c>/<c>M12</c>/<c>M21</c>/<c>M22</c> - reads this to pre-divide that part itself (issue
        /// #814's SVG viewBox-to-viewport transform is the first such case: its "scale" is a ratio of an
        /// inflated length to a never-inflated, dimensionless SVG user unit, so it is not already
        /// scale-neutral the way an ordinary CSS <c>transform: scale()</c> is).
        /// </summary>
        public virtual double PixelsPerPoint => 1.0;

        /// <summary>
        /// Identity of the document that owns forms created by <see cref="CreateTile"/>. A tile may be
        /// reused on another page of that document, but never in another document. Null for graphics
        /// contexts without a PDF document (including test and measure-only graphics).
        /// </summary>
        internal virtual object? FormCacheOwner => null;

        /// <summary>
        /// Get color pen.
        /// </summary>
        /// <param name="color">the color to get the pen for</param>
        /// <returns>pen instance</returns>
        public Pen GetPen(PaintColor color)
        {
            return _adapter.GetPen(color);
        }

        /// <summary>
        /// Get a pen that strokes with the given brush, e.g. for an SVG <c>stroke="url(#gradient)"</c>.
        /// </summary>
        public Pen GetPen(Brush brush)
        {
            return _adapter.GetPen(brush);
        }

        /// <summary>
        /// Get solid color brush.
        /// </summary>
        /// <param name="color">the color to get the brush for</param>
        /// <returns>solid color brush instance</returns>
        public Brush GetSolidBrush(PaintColor color)
        {
            return _adapter.GetSolidBrush(color);
        }

        /// <summary>
        /// Get linear gradient color brush from <paramref name="color1"/> to <paramref name="color2"/>.
        /// </summary>
        /// <param name="rect">the rectangle to get the brush for</param>
        /// <param name="color1">the start color of the gradient</param>
        /// <param name="color2">the end color of the gradient</param>
        /// <param name="angle">the angle to move the gradient from start color to end color in the rectangle</param>
        /// <returns>linear gradient color brush instance</returns>
        public Brush GetLinearGradientBrush(Rect rect, PaintColor color1, PaintColor color2, double angle)
        {
            return _adapter.GetLinearGradientBrush(rect, color1, color2, angle);
        }

        /// <summary>Convenience wrapper for <c>RenderContext.GetLinearGradientBrush(PaintPoint, PaintPoint, (PaintColor, double)[], bool)</c> on the <see cref="RenderContext"/> this graphics was built from.</summary>
        public Brush GetLinearGradientBrush(PaintPoint p1, PaintPoint p2, (PaintColor PaintColor, double Position)[] stops, bool isRepeating = false)
        {
            return _adapter.GetLinearGradientBrush(p1, p2, stops, isRepeating);
        }

        /// <summary>Convenience wrapper for <see cref="RenderContext.GetRadialGradientBrush"/> on the <see cref="RenderContext"/> this graphics was built from.</summary>
        public Brush GetRadialGradientBrush(PaintPoint center, double radiusX, double radiusY, (PaintColor PaintColor, double Position)[] stops, bool isRepeating = false, PaintPoint? focalCenter = null)
        {
            return _adapter.GetRadialGradientBrush(center, radiusX, radiusY, stops, isRepeating, focalCenter);
        }

        /// <summary>Convenience wrapper for <see cref="RenderContext.GetConicGradientBrush"/> on the <see cref="RenderContext"/> this graphics was built from.</summary>
        public Brush GetConicGradientBrush(PaintPoint center, double outerRadius, PaintColor[] colors, double[] anglesRad)
        {
            return _adapter.GetConicGradientBrush(center, outerRadius, colors, anglesRad);
        }

        /// <summary>
        /// Gets a Rectangle structure that bounds the clipping region of this Graphics.
        /// </summary>
        /// <returns>A rectangle structure that represents a bounding rectangle for the clipping region of this Graphics.</returns>
        public Rect GetClip()
        {
            return _clipStack.Peek();
        }

        /// <summary>
        /// Pop the latest clip push.
        /// </summary>
        public abstract void PopClip();

        /// <summary>
        /// Push the clipping region of this Graphics to interception of current clipping rectangle and the given rectangle.
        /// </summary>
        /// <param name="rect">Rectangle to clip to.</param>
        public abstract void PushClip(Rect rect);

        /// <summary>
        /// Push the clipping region of this Graphics to intersection of the current clip and the given
        /// (possibly non-rectangular) path. Used for SVG <c>clip-path</c>, where the clip region isn't
        /// necessarily axis-aligned.
        /// </summary>
        /// <param name="path">Path to clip to.</param>
        public abstract void PushClip(GraphicsPath path);

        /// <summary>
        /// Push the clipping region of this Graphics to exclude the given rectangle from the current clipping rectangle.
        /// </summary>
        /// <param name="rect">Rectangle to exclude clipping in.</param>
        public abstract void PushClipExclude(Rect rect);

        /// <summary>
        /// Push a 2D affine transform (composed before/with the current transform), saving state so it can
        /// later be undone by <see cref="PopTransform"/>. Used to implement the CSS <c>transform</c> property.
        /// </summary>
        /// <param name="matrix">Matrix to apply.</param>
        public abstract void PushTransform(Matrix3x2 matrix);

        /// <summary>
        /// Pop the most recent <see cref="PushTransform"/>, restoring the prior transform state.
        /// </summary>
        public abstract void PopTransform();

        /// <summary>
        /// Push a PDF blend mode for subsequent drawing, saving state so it can later be undone by
        /// <see cref="PopBlendMode"/> (stack-shaped, same convention as <see cref="PushClip(Rect)"/>/
        /// <see cref="PopClip"/>). Used e.g. to composite a fill against the content underneath it via
        /// <see cref="PaintBlendMode.Difference"/> for a true color inversion (CSS <c>outline-color: invert</c>).
        /// </summary>
        /// <param name="mode">Blend mode to apply to subsequent drawing.</param>
        public abstract void PushBlendMode(PaintBlendMode mode);

        /// <summary>
        /// Pop the most recent <see cref="PushBlendMode"/>, restoring the prior blend mode.
        /// </summary>
        public abstract void PopBlendMode();


        /// <summary>
        /// Restore the clipping region to the initial clip.
        /// </summary>
        public void SuspendClipping()
        {
            while (_clipStack.Count > 1)
            {
                var clip = GetClip();
                _suspendedClips.Push(clip);
                PopClip();
            }
        }

        /// <summary>
        /// Resumes the suspended clips.
        /// </summary>
        public void ResumeClipping()
        {
            while (_suspendedClips.Count > 0)
            {
                var clip = _suspendedClips.Pop();
                PushClip(clip);
            }
        }

        /// <summary>
        /// Set the graphics smooth mode to use anti-alias.<br/>
        /// Use <see cref="ReturnPreviousSmoothingMode"/> to return back the mode used.
        /// </summary>
        /// <returns>the previous smooth mode before the change</returns>
        public abstract object SetAntiAliasSmoothingMode();

        /// <summary>
        /// Return to previous smooth mode before anti-alias was set as returned from <see cref="SetAntiAliasSmoothingMode"/>.
        /// </summary>
        /// <param name="prevMode">the previous mode to set</param>
        public abstract void ReturnPreviousSmoothingMode(object? prevMode);

        /// <summary>
        /// Get GraphicsPath object.
        /// </summary>
        /// <returns>graphics path instance</returns>
        public abstract GraphicsPath GetGraphicsPath();

        /// <summary>
        /// Creates a fresh, independent (<paramref name="width"/> x <paramref name="height"/>)
        /// drawing surface for tile-based content (e.g. an SVG <c>&lt;pattern&gt;</c>'s cell): draw
        /// into the returned <c>Graphics</c> using ordinary <see cref="Canvas"/> calls, then use the
        /// returned <c>Image</c> with <see cref="DrawImage(Image, Rect)"/> - repeated calls tile it,
        /// each one a real reference to the same underlying vector content (a PDF Form XObject), never
        /// rasterized. Null when creating one isn't supported in the current rendering context (e.g. a
        /// measure-only pass with no real PDF page to own the new object).
        /// </summary>
        /// <remarks>
        /// The returned <c>Graphics</c>'s own <see cref="CurrentTransform"/> starts seeded from this graphics'
        /// current one, same as <see cref="BeginRasterSurface"/>'s (see its doc remarks for why) - a caller
        /// implementing this should seed it too, or a reader consulting <see cref="CurrentTransform"/> inside
        /// the tile gets a coordinate space it can't relate back to anything outside the tile.
        /// </remarks>
        public abstract (Canvas Graphics, Image Image)? CreateTile(double width, double height);

        /// <summary>
        /// Asks this graphics for a pixel surface to paint an effect PDF cannot express as vector content
        /// (a blur, a cross-channel colour filter, ...) into. The returned scope has this graphics' own
        /// coordinate system, so the same paint code that would have drawn to this graphics draws to it
        /// unchanged; when finished, hand the surface back through <see cref="DrawRaster"/>.
        /// </summary>
        /// <param name="layoutBounds">the region to cover, in this graphics' layout units</param>
        /// <param name="dpiOverride">pixels per inch of paper for this surface; null uses the document's rasterization DPI</param>
        /// <returns>
        /// null when this graphics cannot rasterize (a measure-only pass, a test double), the region is empty, or it
        /// cannot be allocated - callers fall back to whatever they did before the raster backend existed.
        /// <para>
        /// Untyped (<c>object?</c>, not the concrete scope type) because the concrete raster surface still
        /// lives in <c>PeachPDF.Raster</c>, which this project cannot reference without a circular
        /// dependency (PeachPDF references this project, not the other way around) - the same reason
        /// <see cref="CreateTransparencyProbe"/> and <see cref="SvgBackdrop"/> are <c>object?</c>-typed.
        /// A concrete <see cref="Canvas"/> that implements this (e.g. <c>GraphicsAdapter</c>) overrides it
        /// with a covariant return of its own real scope type; every caller that received the object back
        /// from a call typed as <see cref="Canvas"/> must pattern-match it to that concrete type before use.
        /// </para>
        /// </returns>
        internal virtual object? BeginRasterSurface(Rect layoutBounds, double? dpiOverride = null) => null;

        /// <summary>
        /// While true, text this graphics draws is laid out and embedded as usual but paints nothing (PDF text render mode 3), so
        /// it stays selectable and searchable over content drawn some other way - a bitmap of the same text, say. Backends with no
        /// notion of text extraction (a raster graphics) draw no text at all while it is set. Everything other than text is
        /// unaffected; callers set it around the text-only pass and clear it afterwards.
        /// </summary>
        internal bool InvisibleText { get; set; }

        /// <summary>
        /// Whether group effects (opacity, blend modes, colour functions) are best done by rendering the element into a tight
        /// bitmap and compositing that, rather than through <see cref="CreateTile"/>. True for a raster graphics, whose tile
        /// would otherwise span from the page origin; false for a PDF graphics, where a tile is a cheap vector Form XObject.
        /// </summary>
        internal virtual bool PrefersRasterGroups => false;

        /// <summary>
        /// Whether the document being written forbids transparency (PDF/A-1, PDF/X-1a/X-3) and was asked to flatten it instead of
        /// rejecting it (<c>PdfGenerateConfig.TransparencyPolicy</c>). The painter then renders what needs transparency as an
        /// opaque bitmap.
        /// </summary>
        internal virtual bool FlattensTransparency => false;

        /// <summary>
        /// A probe that answers whether painting something would need transparency, or null when this
        /// graphics cannot tell. Untyped here so the base rendering abstraction never has to name a
        /// PDF-specific type - the one caller (<c>FragmentPainter.OwnPaintNeedsTransparency</c>) knows to
        /// expect <c>PeachPDF.Adapters.TransparencyProbe</c> and casts; a graphics with no notion of
        /// transparency (a raster surface, or a third party's <c>Canvas</c>) just returns null, same as
        /// before.
        /// </summary>
        internal virtual object? CreateTransparencyProbe() => null;

        /// <summary>
        /// How much the transforms pushed so far magnify a unit along each axis: the lengths of the images of the unit x and y
        /// vectors under the accumulated linear part. A raster region renders at the physical resolution the document asked for
        /// <em>after</em> those transforms are applied, so it needs this to pick its pixel pitch; a graphics that does not
        /// track transforms reports no magnification.
        /// </summary>
        internal virtual (double X, double Y) TransformScale => (1.0, 1.0);

        /// <summary>
        /// The accumulated transform of every <see cref="PushTransform"/> in effect, mapping this graphics' current user space to
        /// the layout space it started in (the space page content is laid out in). Identity for a graphics that does not track transforms.
        /// Bookkeeping only, read by callers (gradient/pattern objectBoundingBox math, an SVG backdrop repaint, context-fill/
        /// context-stroke's coordinate-space mapping) - it plays no part in what this graphics actually draws, which a concrete
        /// backend positions its own way. A tile this graphics hands out (<see cref="CreateTile"/>'s <c>Graphics</c>, or
        /// <see cref="BeginRasterSurface"/>'s) starts seeded from this value, not <see cref="Matrix3x2.Identity"/>, precisely so a
        /// reader can still relate the tile's own coordinate space back to whatever space content outside the tile is measured in.
        /// </summary>
        internal virtual Matrix3x2 CurrentTransform => Matrix3x2.Identity;

        /// <summary>
        /// What an SVG whose filters read <c>BackgroundImage</c> needs while it is being painted here: how to repaint what lies behind
        /// an element. Null except while such an SVG (or a repaint of the part of it painted before an element) is being drawn.
        /// Untyped here so the base rendering abstraction never has to name an SVG-specific type - only
        /// <c>SvgRenderer</c> (which both sets and reads it) ever needs to know it's really a
        /// <c>PeachPDF.Svg.SvgBackdropContext</c>.
        /// </summary>
        internal object? SvgBackdrop { get; set; }

        /// <summary>
        /// Draws a surface obtained from <see cref="BeginRasterSurface"/> into this graphics at the rectangle the
        /// surface itself records, so its physical size is exact. Honours this graphics' current transform,
        /// clip and blend mode.
        /// </summary>
        /// <param name="surface">
        /// Untyped for the same reason <see cref="BeginRasterSurface"/>'s return is - see its doc remarks.
        /// Always the exact object <see cref="BeginRasterSurface"/> handed back for this graphics; a
        /// concrete override casts it to its own real surface type.
        /// </param>
        internal virtual void DrawRaster(object? surface) { }

        /// <summary>
        /// Whether this instance paints into an offscreen tile (e.g. one returned by
        /// <see cref="CreateTile"/>, used by <c>CssBox.PaintWithOpacity</c> and SVG pattern/mask
        /// content) rather than directly into the real page's own content stream. Tagged-PDF output
        /// does not emit marked-content sequences into tile content streams in the current
        /// implementation (doing so correctly needs <c>/MCR</c> with <c>/Stm</c>/<c>/StmOwn</c>
        /// pointing at the tile's own content stream, not yet wired up) - callers use this to skip
        /// MCID/BDC emission while still creating the struct element itself, so the tree shape stays
        /// well-formed even though this particular occurrence contributes no reachable MCID.
        /// Defaults to <c>false</c>.
        /// </summary>
        public virtual bool IsOffscreenTile => false;

        /// <summary>
        /// Draws <paramref name="image"/> (a tile from <see cref="CreateTile"/>) at
        /// <paramref name="destRect"/> with <paramref name="maskImage"/> (another same-adapter tile,
        /// sharing <paramref name="image"/>'s own local width/height) applied as a luminosity soft
        /// mask, scoped to just this one placement. White areas of <paramref name="maskImage"/> are
        /// fully visible, black fully transparent, matching PDF's/SVG's own <c>&lt;mask&gt;</c>
        /// semantics. Deliberately NOT a "push mask, draw normally, pop mask" pair (which an earlier
        /// version of this API was): a tile's own content is Y-flipped relative to its own (small)
        /// size, not the page's, so positioning it correctly requires the same explicit destRect
        /// placement <see cref="DrawImage(Image, Rect)"/> already uses for pattern tiles - relying
        /// on whatever transform happens to be ambient in the page's own content stream at some
        /// arbitrary "push" point silently mispositions the mask relative to the content it's meant
        /// to mask. A no-op if either image wasn't created via <see cref="CreateTile"/> on this same
        /// <see cref="Canvas"/>.
        /// </summary>
        public abstract void DrawImageMasked(Image image, Image maskImage, Rect destRect);

        /// <summary>
        /// Draws <paramref name="image"/> (a tile from <see cref="CreateTile"/>) at
        /// <paramref name="destRect"/>, composited as a single flattened result at constant
        /// <paramref name="opacity"/> - the mechanism behind CSS/SVG group <c>opacity</c>. Unlike simply
        /// multiplying the alpha of each shape painted into the tile, this flattens the tile's own
        /// (possibly overlapping) content once before applying <paramref name="opacity"/> to the
        /// flattened result, so overlapping content doesn't double-darken where it overlaps. A no-op if
        /// <paramref name="image"/> wasn't created via <see cref="CreateTile"/> on this same <see cref="Canvas"/>.
        /// <paramref name="blendMode"/> composites the flattened tile against the destination with a
        /// non-Normal PDF blend mode in the same <c>gs</c> as <paramref name="opacity"/> - the mechanism
        /// behind CSS <c>mix-blend-mode</c> combined with group <c>opacity</c>, a single ExtGState rather
        /// than two nested ones.
        /// </summary>
        public abstract void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal);

        /// <summary>
        /// Draws <paramref name="image"/> (a tile from <see cref="CreateTile"/>) at
        /// <paramref name="destRect"/>, composited through a CSS/SVG <c>filter</c> color-matrix
        /// transform (<c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c>,
        /// <c>invert()</c>, <c>brightness()</c>, <c>contrast()</c>, or <c>feColorMatrix</c>) expressed as
        /// <paramref name="matrix"/>. Only representable when <see cref="ColorMatrix.IsChannelIndependent"/>
        /// is true - see <see cref="ColorMatrix"/>'s remarks for why a PDF ExtGState transfer function
        /// (the mechanism this goes through) cannot express a matrix that mixes color channels, and what
        /// a caller needs instead for one that does. A no-op if <paramref name="image"/> wasn't created
        /// via <see cref="CreateTile"/> on this same <see cref="Canvas"/>.
        /// </summary>
        public abstract void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix);

        /// <summary>
        /// Draws <paramref name="image"/> (a tile from <see cref="CreateTile"/>) at
        /// <paramref name="destRect"/> with <paramref name="maskImage"/> (another same-adapter tile)
        /// attached as an <c>/Alpha</c>-subtype soft mask (ISO 32000-1 §11.6.4.3) - unlike
        /// <see cref="DrawImageMasked"/>'s <c>/Luminosity</c> mask, which derives mask values from the
        /// mask tile's rendered COLOR converted to grayscale, this derives them directly from the mask
        /// tile's own computed ALPHA, disregarding whatever color it painted with. The motivating future
        /// use case is SVG's <c>SourceAlpha</c> filter input and <c>feComposite</c>, both of which are
        /// defined in terms of a source's alpha channel specifically, not a luminosity conversion of it.
        /// When <paramref name="invert"/> is true, the mask's <c>/TR</c> is set to <c>1 - x</c>
        /// (<c>PdfSharpCore.Pdf.Advanced.PdfType4Function.BuildInvertFunction</c>), for a
        /// complemented alpha mask without needing a second tile painted with inverted alpha. A no-op if
        /// either image wasn't created via <see cref="CreateTile"/> on this same <see cref="Canvas"/>.
        /// </summary>
        public abstract void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false);

        /// <summary>
        /// Paints <paramref name="bottom"/> normally at <paramref name="destRect"/>, then
        /// <paramref name="top"/> on top of it at the same rect composited with <paramref name="blendMode"/> -
        /// both same-adapter tiles from <see cref="CreateTile"/>, sized/positioned identically in their
        /// own local coordinate systems. The motivating future use case is SVG's <c>feBlend</c>, which
        /// blends two independently-rendered filter inputs together inside a fresh tile rather than
        /// blending freshly-painted content against whatever the page already has underneath it (which
        /// is what <see cref="PushBlendMode"/>/<see cref="PopBlendMode"/> do). A no-op if either image
        /// wasn't created via <see cref="CreateTile"/> on this same <see cref="Canvas"/>.
        /// </summary>
        public abstract void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode);

        /// <summary>
        /// Begins a tagged marked-content sequence in the page content stream, associated with the
        /// given PDF structure type (e.g. "/H1", "/P") and marked-content identifier. Only called
        /// when tagged PDF output is enabled. Must be paired with <see cref="EndMarkedContent"/>,
        /// wrapping a whole leaf box's own paint calls - never part of one.
        /// </summary>
        public abstract void BeginMarkedContent(string structureType, int mcid);

        /// <summary>
        /// Ends a marked-content sequence started by <see cref="BeginMarkedContent"/> or
        /// <see cref="BeginArtifact"/>.
        /// </summary>
        public abstract void EndMarkedContent();

        /// <summary>
        /// Begins an artifact marked-content sequence - marks the content that follows as not part
        /// of the document's logical structure (e.g. a decorative &lt;hr&gt;). Must be paired with
        /// <see cref="EndMarkedContent"/>.
        /// </summary>
        public abstract void BeginArtifact();

        /// <summary>
        /// Begins the <c>/Tx</c> marked-content sequence ISO 32000-1 §12.7.3.3 requires around the
        /// value drawn into an interactive form field's appearance stream, and
        /// <see cref="EndVariableText"/> closes it. It is what tells a PDF reader which part of the
        /// generated appearance to replace when the user edits the field - without it the reader
        /// draws the new value over the generated one instead of in place of it. Deliberately a pair
        /// of its own rather than reusing <see cref="EndMarkedContent"/>: these two must sit outside
        /// any text object, while that one has a caller that is legitimately inside one.
        /// </summary>
        public abstract void BeginVariableText();

        /// <summary>Closes the sequence <see cref="BeginVariableText"/> opened.</summary>
        public abstract void EndVariableText();

        /// <summary>
        /// Measure the width and height of string <paramref name="str"/> when drawn on device context HDC
        /// using the given font <paramref name="font"/>.
        /// </summary>
        /// <param name="str">the string to measure</param>
        /// <param name="font">the font to measure string with</param>
        /// <param name="features">
        /// which GSUB features (see <see cref="ShapeSettings"/>) to apply when shaping
        /// <paramref name="str"/> - must match what <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
        /// will use for the same text, so the measured width matches what's actually drawn.
        /// </param>
        /// <returns>the size of the string</returns>
        public abstract Size MeasureString(string str, Font font, ShapeSettings? features = null);

        /// <summary>
        /// The number of glyphs <paramref name="str"/> shapes into once GSUB substitution is applied -
        /// always &lt;= <paramref name="str"/>'s character count, less whenever a ligature merges more
        /// than one character into a single glyph (single substitution never changes the count). Used
        /// to size the per-glyph <c>letter-spacing</c> gap count a word's box must reserve (the PDF
        /// <c>Tc</c> operator adds one gap per glyph actually shown, not per source character), so it
        /// stays in sync with what <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
        /// paints for the same text/font/<paramref name="features"/>.
        /// </summary>
        public abstract int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null);

        /// <summary>
        /// Measure the width of string under max width restriction calculating the number of characters that can fit and the width those characters take.<br/>
        /// Not relevant for platforms that don't render HTML on UI element.
        /// </summary>
        /// <param name="str">the string to measure</param>
        /// <param name="font">the font to measure string with</param>
        /// <param name="maxWidth">the max width to calculate fit characters</param>
        /// <param name="charFit">the number of characters that will fit under <paramref name="maxWidth"/> restriction</param>
        /// <param name="charFitWidth">the width that only the characters that fit into max width take</param>
        public abstract void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth);

        /// <summary>
        /// Draw the given string using the given font and foreground color at given location.
        /// </summary>
        /// <param name="str">the string to draw</param>
        /// <param name="font">the font to use to draw the string</param>
        /// <param name="color">the text color to set</param>
        /// <param name="point">the location to start string draw (top-left)</param>
        /// <param name="size">used to know the size of the rendered text for transparent text support</param>
        /// <param name="letterSpacing">
        /// extra space to add between each pair of adjacent characters (CSS <c>letter-spacing</c>),
        /// in the same units as <paramref name="point"/>. 0 (the default/common case) must draw
        /// <paramref name="str"/> as a single atomic string, identically to how this always worked
        /// before this parameter existed - implementations should only fall back to a slower
        /// per-character draw loop when this is non-zero.
        /// </param>
        /// <param name="fontPalette">
        /// the resolved CSS <c>font-palette</c> selection (a CPAL palette index + per-entry color overrides)
        /// for a COLR/CPAL color font; <c>null</c> (the default/common case) selects palette 0 with no
        /// overrides, identical to how color-glyph drawing always worked before this parameter existed.
        /// </param>
        /// <param name="features">
        /// which GSUB features (see <see cref="ShapeSettings"/>) to apply when shaping
        /// <paramref name="str"/> - the resolved CSS <c>font-variant-*</c>/<c>font-feature-settings</c>
        /// values.
        /// </param>
        public abstract void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null);

        /// <summary>
        /// Same as the other <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
        /// overload, plus <paramref name="logicalText"/>: the
        /// true logical-order (pre-bidi-mirroring) source text <paramref name="str"/> was derived from,
        /// when the two differ - <c>null</c> (the default) means they're the same (the overwhelming
        /// common case: LTR text, or any word that was never reversed/mirrored for RTL display) and the
        /// real implementation should behave identically to the other overload. A word whose own
        /// <c>Text</c> WAS reversed/mirrored for RTL display (<c>CssLayoutEngine.MirrorWordTextIfNeeded</c>'s
        /// ordinary path - not an Arabic-family joining word, which never mutates its own text at all)
        /// passes its own stable <c>PreMirrorText</c> here, so the PDF's ToUnicode CMap records each
        /// glyph's true source character(s) rather than whichever reversed/mirrored character happens to
        /// occupy that glyph's position in the *painted* string - see this overload's own introduction
        /// for the real-world extraction corruption this fixes (a parenthesized RTL word extracting with
        /// its parentheses in the wrong position, confirmed against real MuPDF/PDFium output).
        ///
        /// A default (non-abstract) implementation forwarding to the other overload - ignoring
        /// <paramref name="logicalText"/> - is deliberate: only a real PDF-writing backend
        /// (<c>PeachPDF.Adapters.GraphicsAdapter</c>) needs to act on it; every other
        /// implementation (test mocks recording draw calls, measuring-only contexts) is unaffected by
        /// this overload's mere existence and needs no changes to keep compiling/behaving identically.
        /// </summary>
        public virtual void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing, FontPalette? fontPalette, ShapeSettings? features, string? logicalText) =>
            DrawString(str, font, color, point, size, letterSpacing, fontPalette, features);

        /// <summary>
        /// Draws each of <paramref name="glyphs"/> at its own explicit position, addressed directly by
        /// font glyph index rather than by Unicode character - unlike
        /// <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>,
        /// this never re-shapes/re-maps through cmap/GSUB, so it can draw a glyph with no Unicode
        /// mapping at all (e.g. an OpenType MATH table's stretchy-operator assembly parts or
        /// pre-sized size variants, which are reached only via <c>MathVariantsTable</c> glyph ids, not
        /// through any character). Each <see cref="GlyphPlacement"/>'s X/Y is that glyph's own baseline
        /// origin (not a bounding-box corner), in the same working unit space as
        /// <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>'s
        /// own <c>point</c> parameter - unlike that method, there is no separate ascent-relative
        /// adjustment, since every glyph here already carries its own exact target baseline position.
        /// </summary>
        public abstract void DrawGlyphs(System.Collections.Generic.IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color);

        /// <summary>
        /// Builds the vector outline of a glyph run as a fillable/strokeable <see cref="GraphicsPath"/>,
        /// with the text baseline at <paramref name="baselineOrigin"/> (user-space units) and glyphs
        /// advancing left-to-right. Unlike <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
        /// (a single-color PDF text show), the
        /// returned path can be filled with a gradient/pattern brush or stroked - used by the SVG
        /// renderer for gradient/pattern <c>fill</c>, <c>stroke</c>, and <c>&lt;textPath&gt;</c> on text.
        /// Returns <c>null</c> when the font produces no glyph outlines (a CID-keyed CFF font, or a
        /// bitmap font, neither of which this engine can decode outlines from) - the caller's cue to fall back to
        /// <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>.
        /// </summary>
        /// <param name="str">the run to outline</param>
        /// <param name="font">the font to outline with</param>
        /// <param name="baselineOrigin">the pen origin on the text baseline (user-space units)</param>
        /// <param name="letterSpacing">extra advance between glyphs (same units as <paramref name="baselineOrigin"/>)</param>
        /// <param name="features">which GSUB features (see <see cref="ShapeSettings"/>) to apply when shaping <paramref name="str"/></param>
        public abstract GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null);

        /// <summary>
        /// The horizontal ranges in which <paramref name="str"/>'s glyph ink crosses the horizontal band
        /// between <paramref name="bandTop"/> and <paramref name="bandBottom"/> — what
        /// <c>text-decoration-skip-ink</c>
        /// (<see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">CSS Text
        /// Decoration 4 §2.5</see>) removes from an underline or overline drawn through that band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Virtual rather than abstract, and returning <c>null</c> by default, so a backend with no glyph
        /// outlines to consult — including every test-only <see cref="Canvas"/> mock — needs no
        /// implementation and degrades to skipping nothing. <c>auto</c> is defined as UA discretion, so
        /// declining to skip is conformant; <c>all</c> silently does not skip either, which is the same
        /// accepted limitation a font with no decodable outlines has.
        /// </para>
        /// <para>
        /// Returns <c>null</c>, not an empty list, when no ink information is available at all — the
        /// caller cannot otherwise tell that apart from a run that genuinely crosses nothing.
        /// </para>
        /// <para>
        /// Each glyph contributes at most one range, spanning everything it puts in the band — so a
        /// glyph whose ink is in several pieces there (the two sides of an <c>o</c>, the separate dots
        /// of an ellipsis) yields one range covering the whole letter rather than one per piece. CSS
        /// Text Decoration 4 leaves this skip <i>shape</i> to the UA
        /// (<see href="https://www.w3.org/TR/css-text-decor-4/#ink-skip-shape">§2.10.5 Shaping
        /// Interruptions</see>), explicitly naming "whether to show the line within enclosed areas of a
        /// glyph" as a UA choice and warning that following each contour can strand
        /// "typographically-awkward wisps of underline"; this is also what Chrome and Firefox do.
        /// </para>
        /// </remarks>
        /// <param name="str">the run whose ink is measured</param>
        /// <param name="font">the font the run is drawn with</param>
        /// <param name="origin">
        /// the run's origin, in user-space units — the <i>same</i> point
        /// <see cref="DrawString(string, Font, PaintColor, PaintPoint, Size, double, FontPalette?, ShapeSettings?)"/>
        /// paints the run from, not its baseline. The implementation places the baseline from the font's
        /// own metrics exactly as the text-drawing path does, so the ink is measured where it is drawn
        /// rather than where a rounded ascent would put it.
        /// </param>
        /// <param name="bandTop">the band's upper edge, in user-space y (smaller than <paramref name="bandBottom"/>)</param>
        /// <param name="bandBottom">the band's lower edge</param>
        /// <param name="letterSpacing">extra advance between glyphs (same units as <paramref name="origin"/>)</param>
        /// <param name="features">which GSUB features to apply when shaping <paramref name="str"/></param>
        /// <returns>the crossings, left to right and already merged, or null when the ink is unknown</returns>
        public virtual IReadOnlyList<InkSpan>? GetInkCrossings(
            string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
            double letterSpacing = 0, ShapeSettings? features = null) => null;

        /// <summary>
        /// Draws a line connecting the two points specified by the coordinate pairs.
        /// </summary>
        /// <param name="pen">Pen that determines the color, width, and style of the line. </param>
        /// <param name="x1">The x-coordinate of the first point. </param>
        /// <param name="y1">The y-coordinate of the first point. </param>
        /// <param name="x2">The x-coordinate of the second point. </param>
        /// <param name="y2">The y-coordinate of the second point. </param>
        public abstract void DrawLine(Pen pen, double x1, double y1, double x2, double y2);

        /// <summary>
        /// Draws a rectangle specified by a coordinate pair, a width, and a height.
        /// </summary>
        /// <param name="pen">A Pen that determines the color, width, and style of the rectangle. </param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to draw. </param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to draw. </param>
        /// <param name="width">The width of the rectangle to draw. </param>
        /// <param name="height">The height of the rectangle to draw. </param>
        public abstract void DrawRectangle(Pen pen, double x, double y, double width, double height);

        /// <summary>
        /// Fills the interior of a rectangle specified by a pair of coordinates, a width, and a height.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to fill. </param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to fill. </param>
        /// <param name="width">Width of the rectangle to fill. </param>
        /// <param name="height">Height of the rectangle to fill. </param>
        public abstract void DrawRectangle(Brush brush, double x, double y, double width, double height);

        /// <summary>
        /// Draws the specified portion of the specified <see cref="Image"/> at the specified location and with the specified size.
        /// </summary>
        /// <param name="image">Image to draw. </param>
        /// <param name="destRect">Rectangle structure that specifies the location and size of the drawn image. The image is scaled to fit the rectangle. </param>
        /// <param name="srcRect">Rectangle structure that specifies the portion of the <paramref name="image"/> object to draw. </param>
        public abstract void DrawImage(Image image, Rect destRect, Rect srcRect);

        /// <summary>
        /// Draws the specified Image at the specified location and with the specified size.
        /// </summary>
        /// <param name="image">Image to draw. </param>
        /// <param name="destRect">Rectangle structure that specifies the location and size of the drawn image. </param>
        public abstract void DrawImage(Image image, Rect destRect);

        /// <summary>
        /// Draws a GraphicsPath.
        /// </summary>
        /// <param name="pen">Pen that determines the color, width, and style of the path. </param>
        /// <param name="path">GraphicsPath to draw. </param>
        public abstract void DrawPath(Pen pen, GraphicsPath path);

        /// <summary>
        /// Fills the interior of a GraphicsPath.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="path">GraphicsPath that represents the path to fill. </param>
        public abstract void DrawPath(Brush brush, GraphicsPath path);

        /// <summary>
        /// Fills the interior of a polygon defined by an array of points specified by PaintPoint structures.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="points">Array of PaintPoint structures that represent the vertices of the polygon to fill. </param>
        public abstract void DrawPolygon(Brush brush, PaintPoint[] points);

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public abstract void Dispose();
    }
}
