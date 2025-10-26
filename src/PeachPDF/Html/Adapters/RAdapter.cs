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
    public abstract class RAdapter
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
        public async Task<CssData> GetDefaultCssData() => _defaultCssData ??= await CssData.Parse(this, CssDefaults.DefaultStyleSheet, false);

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

        /// <summary>
        /// Convert image object returned from <see cref="HtmlImageLoadEventArgs"/> to <see cref="RImage"/>.
        /// </summary>
        /// <param name="image">the image returned from load event</param>
        /// <returns>converted image or null</returns>
        public RImage? ConvertImage(object image)
        {
            // TODO:a remove this by creating better API.
            return ConvertImageInt(image);
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

        public async Task AddFontFamilyFromUrl(string fontFamilyName, string url, string? format)
        {
            var resourceStream = await GetResourceStream(new RUri(url));

            if (resourceStream?.ResourceStream is not null)
            {
                await AddFontFromStream(fontFamilyName, resourceStream.ResourceStream, format);
            }
        }

        public async Task<bool> AddLocalFontFamily(string fontFamilyName, string localFontFaceName)
        {
            return await AddLocalFont(fontFamilyName, localFontFaceName);
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
        /// <returns>font instance</returns>
        public RFont? GetFont(string family, double size, RFontStyle style)
        {
            return _fontsHandler.GetCachedFont(family, size, style);
        }

        /// <summary>
        /// Get font instance by given font family name, size and style.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <returns>font instance</returns>
        internal RFont CreateFont(string family, double size, RFontStyle style)
        {
            return CreateFontInt(family, size, style);
        }

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <returns>font instance</returns>
        internal RFont CreateFont(RFontFamily family, double size, RFontStyle style)
        {
            return CreateFontInt(family, size, style);
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

        /// <summary>
        /// Convert image object returned from <see cref="HtmlImageLoadEventArgs"/> to <see cref="RImage"/>.
        /// </summary>
        /// <param name="image">the image returned from load event</param>
        /// <returns>converted image or null</returns>
        protected abstract RImage? ConvertImageInt(object image);

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
        /// <returns>font instance</returns>
        protected abstract RFont CreateFontInt(string family, double size, RFontStyle style);

        /// <summary>
        /// Get font instance by given font family instance, size and style.<br/>
        /// Used to support custom fonts that require explicit font family instance to be created.
        /// </summary>
        /// <param name="family">the font family instance</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <returns>font instance</returns>
        protected abstract RFont CreateFontInt(RFontFamily family, double size, RFontStyle style);

        protected abstract Task AddFontFromStream(string fontFamilyName, Stream stream, string? format);

        protected abstract Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName);

        #endregion
    }
}