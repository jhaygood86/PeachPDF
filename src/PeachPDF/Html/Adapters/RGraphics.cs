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

using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Adapter for platform specific graphics rendering object - used to render graphics and text in platform specific context.<br/>
    /// The core HTML Renderer components use this class for rendering logic, extending this
    /// class in different platform: WinForms, WPF, Metro, PDF, etc.
    /// </summary>
    internal abstract class RGraphics : IDisposable
    {
        #region Fields/Consts

        /// <summary>
        /// the global adapter
        /// </summary>
        protected readonly RAdapter _adapter;

        /// <summary>
        /// The clipping bound stack as clips are pushed/poped to/from the graphics
        /// </summary>
        protected readonly Stack<RRect> _clipStack = [];

        /// <summary>
        /// The suspended clips
        /// </summary>
        private readonly Stack<RRect> _suspendedClips = [];

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        protected RGraphics(RAdapter adapter, RRect initialClip)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _adapter = adapter;
            _clipStack.Push(initialClip);
        }

        /// <summary>
        /// Get color pen.
        /// </summary>
        /// <param name="color">the color to get the pen for</param>
        /// <returns>pen instance</returns>
        public RPen GetPen(RColor color)
        {
            return _adapter.GetPen(color);
        }

        /// <summary>
        /// Get a pen that strokes with the given brush, e.g. for an SVG <c>stroke="url(#gradient)"</c>.
        /// </summary>
        public RPen GetPen(RBrush brush)
        {
            return _adapter.GetPen(brush);
        }

        /// <summary>
        /// Get solid color brush.
        /// </summary>
        /// <param name="color">the color to get the brush for</param>
        /// <returns>solid color brush instance</returns>
        public RBrush GetSolidBrush(RColor color)
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
        public RBrush GetLinearGradientBrush(RRect rect, RColor color1, RColor color2, double angle)
        {
            return _adapter.GetLinearGradientBrush(rect, color1, color2, angle);
        }

        public RBrush GetLinearGradientBrush(RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool isRepeating = false)
        {
            return _adapter.GetLinearGradientBrush(p1, p2, stops, isRepeating);
        }

        public RBrush GetRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null)
        {
            return _adapter.GetRadialGradientBrush(center, radiusX, radiusY, stops, isRepeating, focalCenter);
        }

        public RBrush GetConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad)
        {
            return _adapter.GetConicGradientBrush(center, outerRadius, colors, anglesRad);
        }

        /// <summary>
        /// Gets a Rectangle structure that bounds the clipping region of this Graphics.
        /// </summary>
        /// <returns>A rectangle structure that represents a bounding rectangle for the clipping region of this Graphics.</returns>
        public RRect GetClip()
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
        public abstract void PushClip(RRect rect);

        /// <summary>
        /// Push the clipping region of this Graphics to intersection of the current clip and the given
        /// (possibly non-rectangular) path. Used for SVG <c>clip-path</c>, where the clip region isn't
        /// necessarily axis-aligned.
        /// </summary>
        /// <param name="path">Path to clip to.</param>
        public abstract void PushClip(RGraphicsPath path);

        /// <summary>
        /// Push the clipping region of this Graphics to exclude the given rectangle from the current clipping rectangle.
        /// </summary>
        /// <param name="rect">Rectangle to exclude clipping in.</param>
        public abstract void PushClipExclude(RRect rect);

        /// <summary>
        /// Push a 2D affine transform (composed before/with the current transform), saving state so it can
        /// later be undone by <see cref="PopTransform"/>. Used to implement the CSS <c>transform</c> property.
        /// </summary>
        /// <param name="matrix">Matrix to apply.</param>
        public abstract void PushTransform(RMatrix matrix);

        /// <summary>
        /// Pop the most recent <see cref="PushTransform"/>, restoring the prior transform state.
        /// </summary>
        public abstract void PopTransform();


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
        public abstract RGraphicsPath GetGraphicsPath();

        /// <summary>
        /// Creates a fresh, independent (<paramref name="width"/> x <paramref name="height"/>)
        /// drawing surface for tile-based content (e.g. an SVG <c>&lt;pattern&gt;</c>'s cell): draw
        /// into the returned <c>Graphics</c> using ordinary <see cref="RGraphics"/> calls, then use the
        /// returned <c>Image</c> with <see cref="DrawImage(RImage, RRect)"/> - repeated calls tile it,
        /// each one a real reference to the same underlying vector content (a PDF Form XObject), never
        /// rasterized. Null when creating one isn't supported in the current rendering context (e.g. a
        /// measure-only pass with no real PDF page to own the new object).
        /// </summary>
        public abstract (RGraphics Graphics, RImage Image)? CreateTile(double width, double height);

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
        /// placement <see cref="DrawImage(RImage, RRect)"/> already uses for pattern tiles - relying
        /// on whatever transform happens to be ambient in the page's own content stream at some
        /// arbitrary "push" point silently mispositions the mask relative to the content it's meant
        /// to mask. A no-op if either image wasn't created via <see cref="CreateTile"/> on this same
        /// <see cref="RGraphics"/>.
        /// </summary>
        public abstract void DrawImageMasked(RImage image, RImage maskImage, RRect destRect);

        /// <summary>
        /// Draws <paramref name="image"/> (a tile from <see cref="CreateTile"/>) at
        /// <paramref name="destRect"/>, composited as a single flattened result at constant
        /// <paramref name="opacity"/> - the mechanism behind CSS/SVG group <c>opacity</c>. Unlike simply
        /// multiplying the alpha of each shape painted into the tile, this flattens the tile's own
        /// (possibly overlapping) content once before applying <paramref name="opacity"/> to the
        /// flattened result, so overlapping content doesn't double-darken where it overlaps. A no-op if
        /// <paramref name="image"/> wasn't created via <see cref="CreateTile"/> on this same <see cref="RGraphics"/>.
        /// </summary>
        public abstract void DrawImageWithOpacity(RImage image, RRect destRect, double opacity);

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
        /// Measure the width and height of string <paramref name="str"/> when drawn on device context HDC
        /// using the given font <paramref name="font"/>.
        /// </summary>
        /// <param name="str">the string to measure</param>
        /// <param name="font">the font to measure string with</param>
        /// <returns>the size of the string</returns>
        public abstract RSize MeasureString(string str, RFont font);

        /// <summary>
        /// Measure the width of string under max width restriction calculating the number of characters that can fit and the width those characters take.<br/>
        /// Not relevant for platforms that don't render HTML on UI element.
        /// </summary>
        /// <param name="str">the string to measure</param>
        /// <param name="font">the font to measure string with</param>
        /// <param name="maxWidth">the max width to calculate fit characters</param>
        /// <param name="charFit">the number of characters that will fit under <paramref name="maxWidth"/> restriction</param>
        /// <param name="charFitWidth">the width that only the characters that fit into max width take</param>
        public abstract void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth);

        /// <summary>
        /// Draw the given string using the given font and foreground color at given location.
        /// </summary>
        /// <param name="str">the string to draw</param>
        /// <param name="font">the font to use to draw the string</param>
        /// <param name="color">the text color to set</param>
        /// <param name="point">the location to start string draw (top-left)</param>
        /// <param name="size">used to know the size of the rendered text for transparent text support</param>
        /// <param name="rtl">is to render the string right-to-left (true - RTL, false - LTR)</param>
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
        public abstract void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null);

        /// <summary>
        /// Builds the vector outline of a glyph run as a fillable/strokeable <see cref="RGraphicsPath"/>,
        /// with the text baseline at <paramref name="baselineOrigin"/> (user-space units) and glyphs
        /// advancing left-to-right. Unlike <see cref="DrawString"/> (a single-color PDF text show), the
        /// returned path can be filled with a gradient/pattern brush or stroked - used by the SVG
        /// renderer for gradient/pattern <c>fill</c>, <c>stroke</c>, and <c>&lt;textPath&gt;</c> on text.
        /// Returns <c>null</c> when the font produces no glyph outlines (e.g. a CFF/bitmap font, which
        /// has no <c>glyf</c> table) - the caller's cue to fall back to <see cref="DrawString"/>.
        /// </summary>
        /// <param name="str">the run to outline</param>
        /// <param name="font">the font to outline with</param>
        /// <param name="baselineOrigin">the pen origin on the text baseline (user-space units)</param>
        /// <param name="letterSpacing">extra advance between glyphs (same units as <paramref name="baselineOrigin"/>)</param>
        public abstract RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0);

        /// <summary>
        /// Draws a line connecting the two points specified by the coordinate pairs.
        /// </summary>
        /// <param name="pen">Pen that determines the color, width, and style of the line. </param>
        /// <param name="x1">The x-coordinate of the first point. </param>
        /// <param name="y1">The y-coordinate of the first point. </param>
        /// <param name="x2">The x-coordinate of the second point. </param>
        /// <param name="y2">The y-coordinate of the second point. </param>
        public abstract void DrawLine(RPen pen, double x1, double y1, double x2, double y2);

        /// <summary>
        /// Draws a rectangle specified by a coordinate pair, a width, and a height.
        /// </summary>
        /// <param name="pen">A Pen that determines the color, width, and style of the rectangle. </param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to draw. </param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to draw. </param>
        /// <param name="width">The width of the rectangle to draw. </param>
        /// <param name="height">The height of the rectangle to draw. </param>
        public abstract void DrawRectangle(RPen pen, double x, double y, double width, double height);

        /// <summary>
        /// Fills the interior of a rectangle specified by a pair of coordinates, a width, and a height.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to fill. </param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to fill. </param>
        /// <param name="width">Width of the rectangle to fill. </param>
        /// <param name="height">Height of the rectangle to fill. </param>
        public abstract void DrawRectangle(RBrush brush, double x, double y, double width, double height);

        /// <summary>
        /// Draws the specified portion of the specified <see cref="RImage"/> at the specified location and with the specified size.
        /// </summary>
        /// <param name="image">Image to draw. </param>
        /// <param name="destRect">Rectangle structure that specifies the location and size of the drawn image. The image is scaled to fit the rectangle. </param>
        /// <param name="srcRect">Rectangle structure that specifies the portion of the <paramref name="image"/> object to draw. </param>
        public abstract void DrawImage(RImage image, RRect destRect, RRect srcRect);

        /// <summary>
        /// Draws the specified Image at the specified location and with the specified size.
        /// </summary>
        /// <param name="image">Image to draw. </param>
        /// <param name="destRect">Rectangle structure that specifies the location and size of the drawn image. </param>
        public abstract void DrawImage(RImage image, RRect destRect);

        /// <summary>
        /// Draws a GraphicsPath.
        /// </summary>
        /// <param name="pen">Pen that determines the color, width, and style of the path. </param>
        /// <param name="path">GraphicsPath to draw. </param>
        public abstract void DrawPath(RPen pen, RGraphicsPath path);

        /// <summary>
        /// Fills the interior of a GraphicsPath.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="path">GraphicsPath that represents the path to fill. </param>
        public abstract void DrawPath(RBrush brush, RGraphicsPath path);

        /// <summary>
        /// Fills the interior of a polygon defined by an array of points specified by Point structures.
        /// </summary>
        /// <param name="brush">Brush that determines the characteristics of the fill. </param>
        /// <param name="points">Array of Point structures that represent the vertices of the polygon to fill. </param>
        public abstract void DrawPolygon(RBrush brush, RPoint[] points);

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public abstract void Dispose();
    }
}