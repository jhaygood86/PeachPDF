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

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Handlers
{
    /// <summary>
    /// Utilities for fonts and fonts families handling.
    /// </summary>
    internal sealed class FontsHandler
    {
        #region Fields and Consts

        /// <summary>
        /// 
        /// </summary>
        private readonly RAdapter _adapter;

        /// <summary>
        /// Allow to map not installed fonts to different
        /// </summary>
        private readonly Dictionary<string, string> _fontsMapping = new(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// collection of all installed and added font families to check if font exists
        /// </summary>
        private readonly Dictionary<string, RFontFamily> _existingFontFamilies = new(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// cache of all the font used not to create same font again and again - keyed by (style, weight,
        /// stretch, obliqueSkewSinus) since two different CSS numeric weights (e.g. 300 and 600) can
        /// share the same RFontStyle.Bold bit (both "not bold" by the &gt;=700 threshold) while still
        /// resolving to different nearest-weight-matched faces, and must not collide in this cache;
        /// likewise for stretch (e.g. condensed vs. normal at the same weight/style). The declared
        /// oblique angle (when any - see FontObliqueAngleResolver) is a rendering-only detail that
        /// doesn't affect face selection, but two requests differing only in it would otherwise
        /// incorrectly share one cached RFont and silently keep whichever angle was cached first.
        /// </summary>
        private readonly Dictionary<string, Dictionary<double, Dictionary<(RFontStyle Style, int Weight, int Stretch, double? ObliqueSkewSinus), RFont?>>> _fontsCache = new(StringComparer.InvariantCultureIgnoreCase);

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public FontsHandler(RAdapter adapter)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _adapter = adapter;
        }

        public void ClearCache()
        {
            _fontsCache.Clear();
        }

        /// <summary>
        /// Check if the given font family exists by name
        /// </summary>
        /// <param name="family">the font to check</param>
        /// <returns>true - font exists by given family name, false - otherwise</returns>
        public bool IsFontExists(string family)
        {
            bool exists = _existingFontFamilies.ContainsKey(family);
            if (!exists)
            {
                if (_fontsMapping.TryGetValue(family, out var mappedFamily))
                {
                    exists = _existingFontFamilies.ContainsKey(mappedFamily);
                }
            }
            return exists;
        }

        /// <summary>
        /// Adds a font family to be used.
        /// </summary>
        /// <param name="fontFamily">The font family to add.</param>
        public void AddFontFamily(RFontFamily fontFamily)
        {
            ArgumentNullException.ThrowIfNull(fontFamily, "family");

            _existingFontFamilies[fontFamily.Name] = fontFamily;
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
            ArgChecker.AssertArgNotNullOrEmpty(fromFamily, "fromFamily");
            ArgChecker.AssertArgNotNullOrEmpty(toFamily, "toFamily");

            _fontsMapping[fromFamily] = toFamily;
        }

        /// <summary>
        /// Get cached font instance for the given font properties.<br/>
        /// Improve performance not to create same font multiple times.
        /// </summary>
        /// <param name="family">the font family name</param>
        /// <param name="size">font size</param>
        /// <param name="style">font style</param>
        /// <param name="weight">The real CSS Fonts numeric weight (1-1000) - defaults to a value derived
        /// from <paramref name="style"/>'s Bold bit (700/400) for callers that don't have a numeric
        /// weight to hand.</param>
        /// <param name="stretch">The real CSS Fonts numeric stretch (1-9, 5 = normal) - defaults to
        /// normal for callers that don't have one to hand.</param>
        /// <param name="obliqueSkewSinus">The sine of a declared <c>oblique &lt;angle&gt;</c>, when any.</param>
        /// <returns>cached font instance</returns>
        public RFont? GetCachedFont(string family, double size, RFontStyle style, int? weight = null, int? stretch = null, double? obliqueSkewSinus = null)
        {
            var resolvedWeight = weight ?? ((style & RFontStyle.Bold) != 0 ? 700 : 400);
            var resolvedStretch = stretch ?? 5;
            var font = TryGetFont(family, size, style, resolvedWeight, resolvedStretch, obliqueSkewSinus);

            if (font == null)
            {
                if (!_existingFontFamilies.TryGetValue(family, out var existingFontFamily))
                {
                    if (_fontsMapping.TryGetValue(family, out var mappedFamily))
                    {
                        font = TryGetFont(mappedFamily, size, style, resolvedWeight, resolvedStretch, obliqueSkewSinus);
                        if (font == null)
                        {
                            font = CreateFont(mappedFamily, size, style, resolvedWeight, resolvedStretch, obliqueSkewSinus);
                            _fontsCache[mappedFamily][size][(style, resolvedWeight, resolvedStretch, obliqueSkewSinus)] = font;
                        }
                    }
                }

                if (existingFontFamily is not null)
                {
                    font = CreateFont(existingFontFamily.Name, size, style, resolvedWeight, resolvedStretch, obliqueSkewSinus);
                }

                _fontsCache[family][size][(style, resolvedWeight, resolvedStretch, obliqueSkewSinus)] = font;
            }

            return font;
        }


        #region Private methods

        /// <summary>
        /// Get cached font if it exists in cache or null if it is not.
        /// </summary>
        private RFont? TryGetFont(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus)
        {
            RFont? font = null;

            if (_fontsCache.TryGetValue(family, out var a))
            {
                if (a.TryGetValue(size, out var b))
                {
                    b.TryGetValue((style, weight, stretch, obliqueSkewSinus), out font);
                }
                else
                {
                    _fontsCache[family][size] = [];
                }
            }
            else
            {
                _fontsCache[family] = new Dictionary<double, Dictionary<(RFontStyle, int, int, double?), RFont?>>
                {
                    [size] = new()
                };
            }
            return font;
        }

        /// <summary>
        /// create font (try using existing font family to support custom fonts)
        /// </summary>
        private RFont CreateFont(string family, double size, RFontStyle style, int weight, int stretch, double? obliqueSkewSinus)
        {
            RFontFamily? fontFamily;
            try
            {
                return _existingFontFamilies.TryGetValue(family, out fontFamily)
                    ? _adapter.CreateFont(fontFamily, size, style, weight, stretch, obliqueSkewSinus)
                    : _adapter.CreateFont(family, size, style, weight, stretch, obliqueSkewSinus);
            }
            catch
            {
                // handle possibility of no requested style exists for the font, use regular then
                return _existingFontFamilies.TryGetValue(family, out fontFamily)
                    ? _adapter.CreateFont(fontFamily, size, RFontStyle.Regular, weight, stretch, obliqueSkewSinus)
                    : _adapter.CreateFont(family, size, RFontStyle.Regular, weight, stretch, obliqueSkewSinus);
            }
        }

        #endregion
    }
}