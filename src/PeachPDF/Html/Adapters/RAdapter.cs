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

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Html.Adapters
{
    /// <summary>
    /// Platform adapter to bridge platform specific objects to HTML Renderer core library.<br/>
    /// Core uses abstract renderer objects (RAdapter/RControl/REtc...) to access platform specific functionality, the concrete platforms 
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
    internal abstract class RAdapter
    {
        #region Fields/Consts

        /// <summary>
        /// cache of brush color to brush instance
        /// </summary>
        private readonly Dictionary<RColor, RBrush> _brushesCache = [];

        /// <summary>
        /// cache of pen color to pen instance
        /// </summary>
        private readonly Dictionary<RColor, RPen> _penCache = [];

        /// <summary>
        /// cache of all the font used not to create same font again and again
        /// </summary>
        private readonly FontsHandler _fontsHandler;

        /// <summary>
        /// default CSS parsed data singleton
        /// </summary>
        private CssData? _defaultCssData;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        protected RAdapter()
        {
            _fontsHandler = new FontsHandler(this);
        }

        /// <summary>
        /// Get the default CSS stylesheet data.
        /// </summary>
        public async Task<CssData> GetDefaultCssData()
        {
            if (_defaultCssData is null)
            {
                _defaultCssData = await CssData.Parse(this, CssDefaults.DefaultStyleSheet, false);
                foreach (var s in _defaultCssData.Stylesheets)
                    s.IsUserAgent = true;
            }
            return _defaultCssData;
        }

        public abstract RUri? BaseUri { get; }

        public void ClearFontCache() => _fontsHandler.ClearCache();

        /// <summary>
        /// Resolve color value from given color name.
        /// </summary>
        /// <param name="colorName">the color name</param>
        /// <returns>color value</returns>
        public RColor GetColor(string colorName)
        {
            ArgChecker.AssertArgNotNullOrEmpty(colorName, "colorName");
            return GetColorInt(colorName);
        }

        /// <summary>
        /// Get cached pen instance for the given color.
        /// </summary>
        /// <param name="color">the color to get pen for</param>
        /// <returns>pen instance</returns>
        public RPen GetPen(RColor color)
        {
            if (!_penCache.TryGetValue(color, out var pen))
            {
                _penCache[color] = pen = CreatePen(color);
            }
            return pen;
        }

        /// <summary>
        /// Get a (not cached - brushes aren't identity-stable/comparable the way colors are) pen that
        /// strokes with the given brush, e.g. for an SVG <c>stroke="url(#gradient)"</c>.
        /// </summary>
        public RPen GetPen(RBrush brush)
        {
            return CreatePen(brush);
        }

        /// <summary>
        /// Get cached solid brush instance for the given color.
        /// </summary>
        /// <param name="color">the color to get brush for</param>
        /// <returns>brush instance</returns>
        public RBrush GetSolidBrush(RColor color)
        {
            if (!_brushesCache.TryGetValue(color, out var brush))
            {
                _brushesCache[color] = brush = CreateSolidBrush(color);
            }
            return brush;
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
            return CreateLinearGradientBrush(rect, color1, color2, angle);
        }

        public RBrush GetLinearGradientBrush(RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool isRepeating = false)
        {
            return CreateLinearGradientBrush(p1, p2, stops, isRepeating);
        }

        /// <summary>
        /// Create an <see cref="RImage"/> object from the given stream.
        /// </summary>
        /// <param name="memoryStream">the stream to create image from</param>
        /// <returns>new image instance</returns>
        public RImage ImageFromStream(Stream memoryStream)
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
        public void AddFontFamily(RFontFamily fontFamily)
        {
            _fontsHandler.AddFontFamily(fontFamily);
        }

        /// <param name="fontFamilyName">the font family name declared by the <c>@font-face</c> rule</param>
        /// <param name="url">the (possibly relative) <c>url()</c> the font file is served from</param>
        /// <param name="format">the <c>@font-face</c> <c>format()</c> hint, if any</param>
        /// <param name="baseUri">
        /// The location <paramref name="url"/> should be resolved against when it's relative — normally
        /// the <c>@font-face</c> rule's own stylesheet location (see <see cref="PeachPDF.CSS.Stylesheet.BaseUri"/>),
        /// not the document's base, matching how relative <c>url()</c> references in fetched CSS resolve
        /// against the CSS file's own location. Null falls back to treating <paramref name="url"/> as
        /// already-absolute, which fails gracefully (font simply doesn't load) instead of throwing when
        /// it isn't.
        /// </param>
        /// <param name="weightOverride">the <c>@font-face</c> rule's own <c>font-weight</c> descriptor, resolved to a concrete numeric weight - authoritative over the file's own sniffed weight when present</param>
        /// <param name="isItalicOverride">the <c>@font-face</c> rule's own <c>font-style</c> descriptor, resolved to italic-or-not - authoritative over the file's own sniffed style when present</param>
        /// <param name="stretchOverride">the <c>@font-face</c> rule's own <c>font-stretch</c> descriptor, resolved to a concrete numeric stretch - authoritative over the file's own sniffed stretch when present</param>
        /// <param name="unicodeRanges">the <c>@font-face</c> rule's own <c>unicode-range</c> descriptor, parsed to codepoint ranges - restricts which characters this face is used for; null means no restriction</param>
        public async Task<bool> AddFontFamilyFromUrl(string fontFamilyName, string url, string? format, RUri? baseUri = null, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneRange>? unicodeRanges = null)
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
            return await AddFontFromStream(fontFamilyName, fontStream, format, weightOverride, isItalicOverride, stretchOverride, unicodeRanges);
        }

        public async Task<bool> AddLocalFontFamily(string fontFamilyName, string localFontFaceName, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneRange>? unicodeRanges = null)
        {
            return await AddLocalFont(fontFamilyName, localFontFaceName, weightOverride, isItalicOverride, stretchOverride, unicodeRanges);
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
        /// <param name="stretch">the real CSS Fonts numeric stretch (1-9, 5 = normal), when the caller has one</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when the caller has one - drives the faux-italic shear amount instead of the renderer's fixed default</param>
        /// <returns>font instance</returns>
        public RFont? GetFont(string family, double size, RFontStyle style, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return _fontsHandler.GetCachedFont(family, size, style, weight, stretch, obliqueSkewSinus);
        }

        /// <summary>
        /// Resolves a font for <paramref name="family"/> restricted to faces that cover
        /// <paramref name="codepoint"/> (its <c>unicode-range</c> or, absent that, its cmap coverage).
        /// Returns null when the family has no face covering the codepoint, so per-codepoint matching can
        /// move on to the next family in the <c>font-family</c> stack.
        /// </summary>
        public RFont? GetFontForCodepoint(string family, double size, RFontStyle style, System.Text.Rune codepoint, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            return _fontsHandler.GetCachedFontForCodepoint(family, size, style, codepoint, weight, stretch, obliqueSkewSinus);
        }

        /// <summary>
        /// Whether any face registered for <paramref name="family"/> declares an explicit
        /// <c>unicode-range</c> - used by layout to decide when a fully-covered word must still be
        /// resolved per-codepoint to honor a ranged face.
        /// </summary>
        public bool FamilyHasExplicitUnicodeRanges(string family) => FamilyHasExplicitUnicodeRangesInt(family);

        internal RFont? CreateFontForCodepoint(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint)
        {
            return CreateFontForCodepointInt(family, size, style, weight, stretch, obliqueSkewSinus, codepoint);
        }

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the real CSS Fonts numeric stretch (1-9, 5 = normal)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <returns>font instance</returns>
        internal RFont CreateFont(string family, double size, RFontStyle style, int weight, int stretch = 5, double? obliqueSkewSinus = null)
        {
            return CreateFontInt(family, size, style, weight, stretch, obliqueSkewSinus);
        }

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the real CSS Fonts numeric stretch (1-9, 5 = normal)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <returns>font instance</returns>
        internal RFont CreateFont(RFontFamily family, double size, RFontStyle style, int weight, int stretch = 5, double? obliqueSkewSinus = null)
        {
            return CreateFontInt(family, size, style, weight, stretch, obliqueSkewSinus);
        }

        public abstract string GetCssMediaType(IEnumerable<string> mediaTypesAvailable);

        /// <summary>
        /// Gets the given resource using the provided network loader
        /// </summary>
        /// <param name="uri">Uri to load</param>
        /// <returns>The stream of the contents</returns>
        public abstract Task<RNetworkResponse?> GetResourceStream(RUri uri);

        #region Private/Protected methods

        /// <summary>
        /// Resolve color value from given color name.
        /// </summary>
        /// <param name="colorName">the color name</param>
        /// <returns>color value</returns>
        protected abstract RColor GetColorInt(string colorName);

        /// <summary>
        /// Get cached pen instance for the given color.
        /// </summary>
        /// <param name="color">the color to get pen for</param>
        /// <returns>pen instance</returns>
        protected abstract RPen CreatePen(RColor color);

        /// <summary>
        /// Creates a pen that strokes with the given brush.
        /// </summary>
        protected abstract RPen CreatePen(RBrush brush);

        /// <summary>
        /// Get cached solid brush instance for the given color.
        /// </summary>
        /// <param name="color">the color to get brush for</param>
        /// <returns>brush instance</returns>
        protected abstract RBrush CreateSolidBrush(RColor color);

        /// <summary>
        /// Get linear gradient color brush from <paramref name="color1"/> to <paramref name="color2"/>.
        /// </summary>
        /// <param name="rect">the rectangle to get the brush for</param>
        /// <param name="color1">the start color of the gradient</param>
        /// <param name="color2">the end color of the gradient</param>
        /// <param name="angle">the angle to move the gradient from start color to end color in the rectangle</param>
        /// <returns>linear gradient color brush instance</returns>
        protected abstract RBrush CreateLinearGradientBrush(RRect rect, RColor color1, RColor color2, double angle);

        protected abstract RBrush CreateLinearGradientBrush(RPoint p1, RPoint p2, (RColor Color, double Position)[] stops, bool isRepeating = false);

        public RBrush GetRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null)
        {
            return CreateRadialGradientBrush(center, radiusX, radiusY, stops, isRepeating, focalCenter);
        }

        protected abstract RBrush CreateRadialGradientBrush(RPoint center, double radiusX, double radiusY, (RColor Color, double Position)[] stops, bool isRepeating = false, RPoint? focalCenter = null);

        public RBrush GetConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad)
        {
            return CreateConicGradientBrush(center, outerRadius, colors, anglesRad);
        }

        protected abstract RBrush CreateConicGradientBrush(RPoint center, double outerRadius, RColor[] colors, double[] anglesRad);

        /// <summary>
        /// Create an <see cref="RImage"/> object from the given stream.
        /// </summary>
        /// <param name="memoryStream">the stream to create image from</param>
        /// <returns>new image instance</returns>
        protected abstract RImage ImageFromStreamInt(Stream memoryStream);

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the real CSS Fonts numeric stretch (1-9, 5 = normal)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <returns>font instance</returns>
        protected abstract RFont CreateFontInt(string family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null);

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">the real CSS Fonts numeric weight (1-1000)</param>
        /// <param name="stretch">the real CSS Fonts numeric stretch (1-9, 5 = normal)</param>
        /// <param name="obliqueSkewSinus">the sine of a declared <c>oblique &lt;angle&gt;</c>, when any</param>
        /// <returns>font instance</returns>
        protected abstract RFont CreateFontInt(RFontFamily family, double size, RFontStyle style, int weight = 400, int stretch = 5, double? obliqueSkewSinus = null);

        /// <summary>
        /// Builds a font for <paramref name="family"/> that covers <paramref name="codepoint"/>, or returns
        /// null when the family has no covering face (so the caller can try the next family).
        /// </summary>
        protected abstract RFont? CreateFontForCodepointInt(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune codepoint);

        /// <summary>Whether any face of <paramref name="family"/> declares an explicit <c>unicode-range</c>.</summary>
        protected abstract bool FamilyHasExplicitUnicodeRangesInt(string family);

        /// <returns>true if the format was recognized and a load was actually attempted, false if the declared format is one this adapter can't handle (so a caller trying a multi-source <c>@font-face src</c> fallback list knows to move on to the next candidate)</returns>
        protected abstract Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneRange>? unicodeRanges = null);

        protected abstract Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, int? weightOverride = null, bool? isItalicOverride = null, int? stretchOverride = null, IReadOnlyList<RuneRange>? unicodeRanges = null);

        #endregion
    }
}