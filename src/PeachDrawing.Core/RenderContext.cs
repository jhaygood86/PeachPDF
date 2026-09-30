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

#nullable enable

using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Platform adapter to bridge platform specific objects to HTML Renderer core library.<br/>
    /// Core uses abstract renderer objects (RenderContext/RControl/REtc...) to access platform specific functionality, the concrete platforms 
    /// implements those objects to provide concrete platform implementation. Those allowing the core library to be platform agnostic.
    /// <para>
    /// Platforms: WinForms, WPF, Metro, PDF renders, etc.<br/>
    /// Objects: UI elements(Controls), Graphics(Render context), Colors, Brushes, Pens, Fonts, Images, Clipboard, etc.<br/>
    /// </para>
    /// </summary>
    /// <remarks>
    /// It is best to have a singleton instance of this class for concrete implementation!<br/>
    /// This is because it holds caches of default CssData, Images, Fonts and Brushes.
    /// </remarks>
    public abstract class RenderContext
    {
        #region Fields/Consts

        /// <summary>
        /// cache of brush color to brush instance
        /// </summary>
        private readonly Dictionary<PaintColor, Brush> _brushesCache = [];

        /// <summary>
        /// cache of pen color to pen instance
        /// </summary>
        private readonly Dictionary<PaintColor, Pen> _penCache = [];

        /// <summary>
        /// cache of all the font used not to create same font again and again
        /// </summary>
        private readonly FontsHandler _fontsHandler;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        protected RenderContext()
        {
            _fontsHandler = new FontsHandler(this);
        }

        /// <summary>
        /// The document's base URI resource loading resolves relative references against, or
        /// <c>null</c> when this adapter has none (the default - resource loading is entirely optional
        /// for a render context that doesn't need it, e.g. one driving a standalone raster surface with
        /// no document/network concept at all). PeachPDF's own <c>PdfSharpAdapter</c> overrides this.
        /// </summary>
        public virtual RUri? BaseUri => null;

        /// <summary>Discards every font this context has resolved and cached so far.</summary>
        public void ClearFontCache() => _fontsHandler.ClearCache();

        /// <summary>
        /// The factor between the layout units font sizes are requested in and the points the font behind
        /// them is built at (<c>points = size / LayoutUnitsPerPoint</c>). It is part of a cached font's identity:
        /// the same requested size under a different scale is a different physical font, so
        /// <see cref="FontsHandler"/> keys every font cache by it. 1 for an adapter whose layout unit is
        /// the point.
        /// </summary>
        public virtual double LayoutUnitsPerPoint => 1.0;

        /// <summary>
        /// An <see cref="ISvgGlyphPainter"/> for <paramref name="host"/> to draw a glyph's OpenType SVG
        /// document with, or null when this backend has no SVG engine to render one with (a standalone
        /// raster canvas has none; a backend built on top of one, e.g. PeachPDF's own PDF/raster canvases,
        /// overrides this). A caller that gets a real instance back typically caches it (it is reused for
        /// every SVG glyph the host draws); a caller that gets null may ask again next time - an override
        /// with nothing to offer should stay cheap to call repeatedly.
        /// </summary>
        public virtual ISvgGlyphPainter? CreateSvgGlyphPainter(Canvas host) => null;

        /// <summary>
        /// The resolution, in pixels per inch of paper, the raster backend renders effects at
        /// (see <c>PdfGenerateConfig.RasterizationDpi</c>). Set per render by the owner of the adapter.
        /// </summary>
        public double RasterizationDpi { get; set; } = 300;

        /// <summary>How the raster backend fits glyph outlines to its pixel grid (see <c>PdfGenerateConfig.TextHinting</c>).</summary>
        public TextHinting TextHinting { get; set; } = TextHinting.None;

        /// <summary>Whether hinted text of a font with CFF outlines has its stems thickened (see <c>PdfGenerateConfig.TextStemDarkening</c>).</summary>
        public bool TextStemDarkening { get; set; }

        /// <summary>The most pixels one raster surface may have before its resolution is lowered to fit.</summary>
        public long MaxRasterPixels { get; set; } = 64_000_000;

        /// <summary>Whether the raster backend anti-aliases what it draws (see <c>PdfGenerateConfig.RasterAntiAliasing</c>).</summary>
        public bool RasterAntiAliasing { get; set; } = true;

        /// <summary>
        /// Resolve color value from given color name.
        /// </summary>
        /// <param name="colorName">the color name</param>
        /// <returns>color value</returns>
        public PaintColor GetColor(string colorName)
        {
            if (string.IsNullOrEmpty(colorName))
                throw new ArgumentNullException(nameof(colorName));
            return GetColorInt(colorName);
        }

        /// <summary>
        /// Get cached pen instance for the given color.
        /// </summary>
        /// <param name="color">the color to get pen for</param>
        /// <returns>pen instance</returns>
        /// <remarks>
        /// The cache exists to avoid re-allocating a pen per stroke, not to carry stroke state between
        /// callers - so a cached pen is reset to a freshly-created one's settings before it is handed
        /// back. Without that, a caller that sets only some properties silently inherits the rest from
        /// whoever last drew in the same color: a dotted border leaves behind a round cap and a
        /// zero-length dash array, which would turn the next same-colored solid stroke into a row of
        /// dots. Callers already set every property they care about, so resetting can only remove
        /// leakage, never a deliberate carry-over.
        /// </remarks>
        public Pen GetPen(PaintColor color)
        {
            if (!_penCache.TryGetValue(color, out var pen))
            {
                _penCache[color] = pen = new Pen { Paint = GetSolidBrush(color) };
                return pen;
            }

            pen.Width = 1;
            pen.MiterLimit = 0;
            pen.LineCap = LineCap.Butt;
            pen.LineJoin = LineJoin.Miter;
            pen.DashStyle = DashStyle.Solid;
            pen.Paint = GetSolidBrush(color);
            return pen;
        }

        /// <summary>
        /// Get a (not cached - brushes aren't identity-stable/comparable the way colors are) pen that
        /// strokes with the given brush, e.g. for an SVG <c>stroke="url(#gradient)"</c>.
        /// </summary>
        public Pen GetPen(Brush brush)
        {
            return new Pen { Paint = brush };
        }

        /// <summary>
        /// Get cached solid brush instance for the given color.
        /// </summary>
        /// <param name="color">the color to get brush for</param>
        /// <returns>brush instance</returns>
        public Brush GetSolidBrush(PaintColor color)
        {
            if (!_brushesCache.TryGetValue(color, out var brush))
            {
                _brushesCache[color] = brush = new SolidBrush(color);
            }
            return brush;
        }

        /// <summary>
        /// Get linear gradient color brush from <paramref name="color1"/> to <paramref name="color2"/>.
        /// Legacy two-color convenience form - every real CSS/SVG gradient goes through the multi-stop
        /// overload below; this one only picks a start/end point from <paramref name="angle"/>'s bucket
        /// and defers to it.
        /// </summary>
        /// <param name="rect">the rectangle to get the brush for</param>
        /// <param name="color1">the start color of the gradient</param>
        /// <param name="color2">the end color of the gradient</param>
        /// <param name="angle">the angle to move the gradient from start color to end color in the rectangle</param>
        /// <returns>linear gradient color brush instance</returns>
        public Brush GetLinearGradientBrush(Rect rect, PaintColor color1, PaintColor color2, double angle)
        {
            var (p1, p2) = angle switch
            {
                < 45 => (new PaintPoint(rect.Left, rect.Top), new PaintPoint(rect.Right, rect.Bottom)),
                < 90 => (new PaintPoint(rect.Left + rect.Width / 2, rect.Top), new PaintPoint(rect.Left + rect.Width / 2, rect.Bottom)),
                < 135 => (new PaintPoint(rect.Right, rect.Top), new PaintPoint(rect.Left, rect.Bottom)),
                _ => (new PaintPoint(rect.Left, rect.Top + rect.Height / 2), new PaintPoint(rect.Right, rect.Top + rect.Height / 2)),
            };

            return GetLinearGradientBrush(p1, p2, [(color1, 0), (color2, 1)]);
        }

        /// <summary>Gets a brush that paints a linear gradient along the line from <paramref name="p1"/> to <paramref name="p2"/>.</summary>
        /// <param name="p1">the point the gradient line starts at</param>
        /// <param name="p2">the point the gradient line ends at</param>
        /// <param name="stops">the gradient's colour/position stops, in ascending position order</param>
        /// <param name="isRepeating">whether the gradient repeats past its own two ends (CSS <c>repeating-linear-gradient()</c>) instead of padding (the default)</param>
        public Brush GetLinearGradientBrush(PaintPoint p1, PaintPoint p2, (PaintColor PaintColor, double Position)[] stops, bool isRepeating = false)
        {
            RejectMixedColorSpaceGradientStops(stops);
            return new LinearGradientBrush(p1, p2, ToStops(stops), isRepeating ? GradientSpread.Repeat : GradientSpread.Pad);
        }

        /// <summary>
        /// Create an <see cref="Image"/> object from the given stream.
        /// </summary>
        /// <param name="memoryStream">the stream to create image from</param>
        /// <returns>new image instance</returns>
        public Image ImageFromStream(Stream memoryStream)
        {
            return ImageFromStreamInt(memoryStream);
        }

        /// <summary>
        /// Check if the given font exists in the system by font family name.
        /// </summary>
        /// <param name="font">the font name to check</param>
        /// <returns>true - font exists by given family name, false - otherwise</returns>
        public bool IsFontExists(string font)
        {
            return _fontsHandler.IsFontExists(font);
        }

        /// <summary>
        /// Adds a font family to be used.
        /// </summary>
        /// <param name="fontFamily">The font family to add.</param>
        public void AddFontFamily(FontFamily fontFamily)
        {
            _fontsHandler.AddFontFamily(fontFamily);
        }

        /// <param name="fontFamilyName">the font family name declared by the <c>@font-face</c> rule</param>
        /// <param name="url">the (possibly relative) <c>url()</c> the font file is served from</param>
        /// <param name="format">the <c>@font-face</c> <c>format()</c> hint, if any</param>
        /// <param name="baseUri">
        /// The location <paramref name="url"/> should be resolved against when it's relative — normally
        /// the <c>@font-face</c> rule's own stylesheet location (see <c>PeachPDF.CSS.Stylesheet.BaseUri</c>),
        /// not the document's base, matching how relative <c>url()</c> references in fetched CSS resolve
        /// against the CSS file's own location. Null falls back to treating <paramref name="url"/> as
        /// already-absolute, which fails gracefully (font simply doesn't load) instead of throwing when
        /// it isn't.
        /// </param>
        /// <param name="descriptors">the <c>@font-face</c> rule's own <c>font-weight</c>, <c>font-style</c> and <c>font-stretch</c> descriptors, resolved to the ranges the face covers - authoritative over what the file itself declares; an unset member means the file's own value (a variable font then covers the range of its axes)</param>
        /// <param name="unicodeRanges">the <c>@font-face</c> rule's own <c>unicode-range</c> descriptor, parsed to codepoint ranges - restricts which characters this face is used for; null means no restriction</param>
        public async Task<bool> AddFontFamilyFromUrl(string fontFamilyName, string url, string? format, RUri? baseUri = null, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            RUri resolvedUri;

            try
            {
                resolvedUri = baseUri is not null ? new RUri(baseUri, url) : new RUri(url, UriKind.RelativeOrAbsolute);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!resolvedUri.IsAbsoluteUri)
            {
                return false;
            }

            var resourceStream = await GetResourceStream(resolvedUri);

            if (resourceStream?.ResourceStream is null)
            {
                return false;
            }

            // Dispose the response stream once the font is loaded - AddFontFromStream copies the bytes up
            // front (see PdfSharpAdapter.AddFont's CopyToAsync), so nothing needs it afterward. Critically,
            // for a local FileUriNetworkLoader font this is a FileStream, and leaving it open keeps a handle
            // on the file - which locks it on Windows (a real bug, and it broke temp-file cleanup in tests).
            using var fontStream = resourceStream.ResourceStream;
            return await AddFontFromStream(fontFamilyName, fontStream, format, descriptors, unicodeRanges);
        }

        /// <summary>Registers a locally-installed font face under <paramref name="fontFamilyName"/>, matching a CSS <c>@font-face</c> rule's <c>local()</c> source.</summary>
        /// <param name="fontFamilyName">the font family name declared by the <c>@font-face</c> rule</param>
        /// <param name="localFontFaceName">the installed face's own name, as <c>local()</c> names it</param>
        /// <param name="descriptors">the <c>@font-face</c> rule's own <c>font-weight</c>/<c>font-style</c>/<c>font-stretch</c> descriptors, resolved to the ranges the face covers</param>
        /// <param name="unicodeRanges">the <c>@font-face</c> rule's own <c>unicode-range</c> descriptor; null means no restriction</param>
        /// <returns>true if the face was found and registered, false if no locally-installed face by that name exists</returns>
        public async Task<bool> AddLocalFontFamily(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null)
        {
            return await AddLocalFont(fontFamilyName, localFontFaceName, descriptors, unicodeRanges);
        }

        /// <summary>
        /// Adds a font mapping from <paramref name="fromFamily"/> to <paramref name="toFamily"/> iff the <paramref name="fromFamily"/> is not found.<br/>
        /// When the <paramref name="fromFamily"/> font is used in rendered html and is not found in existing 
        /// fonts (installed or added) it will be replaced by <paramref name="toFamily"/>.<br/>
        /// </summary>
        /// <param name="fromFamily">the font family to replace</param>
        /// <param name="toFamily">the font family to replace with</param>
        public void AddFontFamilyMapping(string fromFamily, string toFamily)
        {
            _fontsHandler.AddFontFamilyMapping(fromFamily, toFamily);
        }

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000), when the caller has one - lets the resolver perform nearest-weight matching instead of only an exact Regular/Bold pick</param>
        /// <param name="stretch">the width as a percentage of the normal width (100 = normal; the CSS <c>font-stretch</c> keywords are 50 to 200), when the caller has one</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when the caller has one - drives the faux-italic shear amount instead of the renderer's fixed default</param>
        /// <param name="variations">the encoded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of the box, or null for the initial values (see <c>FontVariationSettingsResolver</c>)</param>
        /// <returns>font instance</returns>
        public Font? GetFont(string family, double size, PaintFontStyle style, double? weight = null, double? stretch = null, double? obliqueSkewSinus = null, string? variations = null)
        {
            return _fontsHandler.GetCachedFont(family, size, style, weight, stretch, obliqueSkewSinus, variations);
        }

        /// <summary>
        /// Resolves a font for <paramref name="family"/> restricted to faces that cover
        /// <paramref name="codepoint"/> (its <c>unicode-range</c> or, absent that, its cmap coverage).
        /// Returns null when the family has no face covering the codepoint, so per-codepoint matching can
        /// move on to the next family in the <c>font-family</c> stack.
        /// </summary>
        public Font? GetFontForCodepoint(string family, double size, PaintFontStyle style, System.Text.Rune codepoint, double? weight = null, double? stretch = null, double? obliqueSkewSinus = null, string? variations = null)
        {
            return _fontsHandler.GetCachedFontForCodepoint(family, size, style, codepoint, weight, stretch, obliqueSkewSinus, variations);
        }

        /// <summary>
        /// Whether any face registered for <paramref name="family"/> declares an explicit
        /// <c>unicode-range</c> - used by layout to decide when a fully-covered word must still be
        /// resolved per-codepoint to honor a ranged face.
        /// </summary>
        public bool FamilyHasExplicitUnicodeRanges(string family) => FamilyHasExplicitUnicodeRangesInt(family);

        internal Font? CreateFontForCodepoint(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations)
        {
            return CreateFontForCodepointInt(family, size, style, weight, stretch, obliqueSkewSinus, codepoint, variations);
        }

        /// <summary>
        /// The last-resort step of CSS Fonts 4 §5's font matching algorithm: when no family in a box's own
        /// <c>font-family</c> stack covers <paramref name="codepoint"/>, resolves a font from any OTHER
        /// family this adapter knows about (every system-discovered and explicitly-registered font) that
        /// does. Returns null when nothing registered covers it either, so the caller keeps today's
        /// <c>.notdef</c>/tofu-box behavior.
        /// </summary>
        public Font? GetSystemFallbackFontForCodepoint(double size, PaintFontStyle style, System.Text.Rune codepoint, double? weight = null, double? stretch = null, double? obliqueSkewSinus = null, PeachDrawing.Text.Unicode.EmojiPresentation presentation = PeachDrawing.Text.Unicode.EmojiPresentation.NoPreference, string? variations = null)
        {
            return _fontsHandler.GetCachedSystemFallbackFontForCodepoint(size, style, codepoint, weight, stretch, obliqueSkewSinus, presentation, variations);
        }

        internal Font? CreateSystemFallbackFontForCodepoint(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation, string? variations)
        {
            return CreateSystemFallbackFontForCodepointInt(size, style, weight, stretch, obliqueSkewSinus, codepoint, presentation, variations);
        }

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the width as a percentage of the normal width (100 = normal; the CSS <c>font-stretch</c> keywords are 50 to 200)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <param name="variations">the encoded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of the box, or null for the initial values (see <c>FontVariationSettingsResolver</c>)</param>
        /// <returns>font instance</returns>
        internal Font CreateFont(string family, double size, PaintFontStyle style, double weight, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null)
        {
            return CreateFontInt(family, size, style, weight, stretch, obliqueSkewSinus, variations);
        }

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the width as a percentage of the normal width (100 = normal; the CSS <c>font-stretch</c> keywords are 50 to 200)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <param name="variations">the encoded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of the box, or null for the initial values (see <c>FontVariationSettingsResolver</c>)</param>
        /// <returns>font instance</returns>
        internal Font CreateFont(FontFamily family, double size, PaintFontStyle style, double weight, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null)
        {
            return CreateFontInt(family, size, style, weight, stretch, obliqueSkewSinus, variations);
        }

        /// <summary>The CSS media type (e.g. <c>"print"</c>, <c>"screen"</c>) this render context evaluates media queries against, chosen from <paramref name="mediaTypesAvailable"/>.</summary>
        public abstract string GetCssMediaType(IEnumerable<string> mediaTypesAvailable);

        /// <summary>
        /// Gets the given resource using the provided network loader, or <c>null</c> by default (a
        /// render context with no resource-loading concept, e.g. a standalone raster surface, simply
        /// never resolves a URL-based resource - <see cref="AddFontFamilyFromUrl"/> then reports the
        /// load as failed rather than throwing). PeachPDF's own <c>PdfSharpAdapter</c> overrides this.
        /// </summary>
        /// <param name="uri">Uri to load</param>
        /// <returns>The stream of the contents</returns>
        public virtual Task<RNetworkResponse?> GetResourceStream(RUri uri) => Task.FromResult<RNetworkResponse?>(null);

        #region Private/Protected methods

        /// <summary>
        /// Resolve color value from given color name.
        /// </summary>
        /// <param name="colorName">the color name</param>
        /// <returns>color value</returns>
        protected abstract PaintColor GetColorInt(string colorName);

        /// <summary>Gets a brush that paints a radial gradient from <paramref name="focalCenter"/> out to an ellipse centered on <paramref name="center"/>.</summary>
        /// <param name="center">the center of the outer ellipse the gradient's last stop reaches</param>
        /// <param name="radiusX">the outer ellipse's horizontal radius</param>
        /// <param name="radiusY">the outer ellipse's vertical radius</param>
        /// <param name="stops">the gradient's colour/position stops, in ascending position order</param>
        /// <param name="isRepeating">whether the gradient repeats past its own outer ellipse (CSS <c>repeating-radial-gradient()</c>) instead of padding (the default)</param>
        /// <param name="focalCenter">the point the gradient's first stop starts at, or <paramref name="center"/> (a concentric radial gradient) if omitted</param>
        /// <param name="transform">an optional matrix carrying the geometry above into paint coordinates, for a gradient whose ellipse is rotated or skewed</param>
        public Brush GetRadialGradientBrush(PaintPoint center, double radiusX, double radiusY, (PaintColor PaintColor, double Position)[] stops, bool isRepeating = false, PaintPoint? focalCenter = null, Matrix3x2? transform = null)
        {
            RejectMixedColorSpaceGradientStops(stops);
            return new RadialGradientBrush(center, focalCenter ?? center, radiusX, radiusY, ToStops(stops), isRepeating ? GradientSpread.Repeat : GradientSpread.Pad, transform);
        }

        /// <summary>Gets a brush that paints a conic gradient sweeping around <paramref name="center"/>.</summary>
        /// <param name="center">the point the gradient sweeps around</param>
        /// <param name="outerRadius">the radius of the circle the gradient is painted over</param>
        /// <param name="colors">each stop's colour, matched positionally with <paramref name="anglesRad"/></param>
        /// <param name="anglesRad">each stop's angle, in radians clockwise from the gradient's own starting angle</param>
        public Brush GetConicGradientBrush(PaintPoint center, double outerRadius, PaintColor[] colors, double[] anglesRad)
        {
            RejectMixedColorSpaceGradientStops(colors);
            var stops = new GradientStop[colors.Length];
            for (var i = 0; i < colors.Length; i++)
                stops[i] = new GradientStop(colors[i], anglesRad[i]);
            return new ConicGradientBrush(center, outerRadius, stops, anglesRad);
        }

        /// <summary>
        /// Rejects a gradient whose stops mix <c>device-cmyk()</c> with RGB-authored colors, rather than
        /// silently building a brush no PDF shading dictionary can represent: a shading's <c>/ColorSpace</c>
        /// is one value for the whole object, and every stop's <c>/C0</c>/<c>/C1</c> component count must
        /// agree with it - a mixed-space stop list has no single component count that fits every stop. An
        /// all-CMYK or all-RGB stop list has no such conflict. This is a colour-model rule, not a PDF one
        /// (there is no defined RGB&lt;-&gt;CMYK conversion anywhere in this project), so it's enforced once
        /// here rather than by whichever <see cref="Canvas"/> backend eventually consumes the brush.
        /// </summary>
        private static void RejectMixedColorSpaceGradientStops(IEnumerable<PaintColor> colors)
        {
            var list = colors as IReadOnlyCollection<PaintColor> ?? colors.ToList();
            if (list.Any(c => c.IsCmyk) && list.Any(c => !c.IsCmyk))
            {
                throw new NotSupportedException(
                    "A gradient cannot mix device-cmyk() stops with RGB-authored stops - there is no defined " +
                    "conversion between the two color spaces. A gradient whose stops are all device-cmyk() " +
                    "(or all RGB-authored) is fully supported.");
            }
        }

        private static void RejectMixedColorSpaceGradientStops((PaintColor PaintColor, double Position)[] stops) =>
            RejectMixedColorSpaceGradientStops(stops.Select(s => s.PaintColor));

        private static GradientStop[] ToStops((PaintColor PaintColor, double Position)[] stops)
        {
            var result = new GradientStop[stops.Length];
            for (var i = 0; i < stops.Length; i++)
                result[i] = new GradientStop(stops[i].PaintColor, stops[i].Position);
            return result;
        }

        /// <summary>
        /// Create an <see cref="Image"/> object from the given stream.
        /// </summary>
        /// <param name="memoryStream">the stream to create image from</param>
        /// <returns>new image instance</returns>
        protected abstract Image ImageFromStreamInt(Stream memoryStream);

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the width as a percentage of the normal width (100 = normal; the CSS <c>font-stretch</c> keywords are 50 to 200)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <param name="variations">the encoded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of the box, or null for the initial values (see <c>FontVariationSettingsResolver</c>)</param>
        /// <returns>font instance</returns>
        protected abstract Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null);

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the width as a percentage of the normal width (100 = normal; the CSS <c>font-stretch</c> keywords are 50 to 200)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <param name="variations">the encoded <c>font-variation-settings</c> and <c>font-optical-sizing</c> of the box, or null for the initial values (see <c>FontVariationSettingsResolver</c>)</param>
        /// <returns>font instance</returns>
        protected abstract Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null);

        /// <summary>
        /// Builds a font for <paramref name="family"/> that covers <paramref name="codepoint"/>, or returns
        /// null when the family has no covering face (so the caller can try the next family).
        /// </summary>
        protected abstract Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations);

        /// <summary>
        /// Builds a font for whichever OTHER registered family (if any) covers <paramref name="codepoint"/>
        /// - the CSS Fonts 4 §5 system-fallback step, tried only after every family in the box's own
        /// <c>font-family</c> stack has already missed. Returns null when nothing registered covers it.
        /// </summary>
        protected abstract Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation, string? variations);

        /// <summary>Whether any face of <paramref name="family"/> declares an explicit <c>unicode-range</c>.</summary>
        protected abstract bool FamilyHasExplicitUnicodeRangesInt(string family);

        /// <returns>true if the format was recognized and a load was actually attempted, false if the declared format is one this adapter can't handle (so a caller trying a multi-source <c>@font-face src</c> fallback list knows to move on to the next candidate)</returns>
        protected abstract Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null);

        /// <returns>true if a locally-installed face by <paramref name="localFontFaceName"/> was found and registered, false otherwise</returns>
        protected abstract Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null);

        #endregion
    }
}