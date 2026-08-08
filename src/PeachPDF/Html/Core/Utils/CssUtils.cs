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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Utility method for handling CSS stuff.
    /// </summary>
    internal static class CssUtils
    {
        /// <summary>
        /// Gets the white space width of the specified box
        /// </summary>
        /// <param name="g"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public static double WhiteSpace(RGraphics g, CssBox box)
        {
            var w = box.ActualFont.GetWhitespaceWidth(g);

            if (box.WordSpacing.Value is { IsValue: true, Value: { } wordSpacing })
            {
                // word-spacing is a plain length in the same layout coordinate space as margin/padding/
                // width/etc.; ParseLength resolves every unit (including spec-correct CSS px) through
                // the shared Length.ToPixels conversion.
                w += CssValueParser.ParseLength(wordSpacing, 0, box);
            }

            return w;
        }

        /// <summary>
        /// Get CSS box property value by the CSS name.<br/>
        /// Used as a mapping between CSS property and the class property.
        /// </summary>
        /// <param name="cssBox">the CSS box to get it's property value</param>
        /// <param name="propName">the name of the CSS property</param>
        /// <returns>the value of the property, null if no such property exists</returns>
        public static string? GetPropertyValue(CssBox cssBox, string propName) =>
            CssPropertyRegistry.Get(cssBox, propName);

        /// <summary>
        /// Snapshots all known property values from a CssBox into a dictionary.
        /// Used to capture the revert target between cascade origin phases.
        /// </summary>
        public static Dictionary<string, string?> SnapshotProperties(CssBox box)
        {
            var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in CssPropertyRegistry.SnapshotPropertyNames)
                snapshot[name] = GetPropertyValue(box, name);
            return snapshot;
        }

        /// <summary>
        /// Snapshots this box's custom property values, for use as the revert/revert-layer target of a
        /// later cascade phase. Custom property names are case-sensitive, unlike <see cref="SnapshotProperties"/>'s
        /// known-property snapshot, so this uses a separate, ordinal-case-sensitive dictionary.
        /// </summary>
        public static Dictionary<string, string>? SnapshotCustomProperties(CssBox box)
        {
            return box.CustomProperties is { Count: > 0 } ? new Dictionary<string, string>(box.CustomProperties) : null;
        }

        /// <summary>
        /// Set CSS box property value by the CSS name.<br/>
        /// Used as a mapping between CSS property and the class property.
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="cssBox">the CSS box to set it's property value</param>
        /// <param name="propName">the name of the CSS property</param>
        /// <param name="value">the value to set</param>
        public static void SetPropertyValue(CssValueParser valueParser, CssBox cssBox, string propName, string value)
        {
            CssPropertyRegistry.TrySet(valueParser, cssBox, propName, value);
        }

        /// <summary>
        /// Assigns a property's already-parsed, strongly-typed value straight onto a <see cref="CssBox"/> from
        /// its Layer A <see cref="ITypedPropertyValue{T}"/> carrier, without re-parsing the authored string.
        /// Returns false when <paramref name="propName"/> has no typed setter, or when
        /// <paramref name="declaredValue"/> is not the matching typed carrier (e.g. a global-keyword value) — the
        /// caller then falls back to the string setter. The per-name handler knows the concrete <c>T</c>.
        /// </summary>
        /// <remarks>
        /// Not yet subsumed by <see cref="CssPropertyRegistry"/>: the generator doesn't emit a typed fast path
        /// yet (only grid-template-columns/-rows have one, and only here) — see CLAUDE.md's generator section.
        /// </remarks>
        public static bool TrySetTypedPropertyValue(CssBox cssBox, string propName, IPropertyValue declaredValue)
        {
            return declaredValue is not null
                   && _typedPropertySetters.TryGetValue(propName, out var setter)
                   && setter(cssBox, declaredValue);
        }

        private static readonly FrozenDictionary<string, Func<CssBox, IPropertyValue, bool>> _typedPropertySetters =
            new Dictionary<string, Func<CssBox, IPropertyValue, bool>>
            {
                ["grid-template-columns"] = (b, dv) =>
                {
                    if (!dv.TryGetValue<GridTemplate>(out var t)) return false;
                    b.GridTemplateColumns = t;
                    return true;
                },
                ["grid-template-rows"] = (b, dv) =>
                {
                    if (!dv.TryGetValue<GridTemplate>(out var t)) return false;
                    b.GridTemplateRows = t;
                    return true;
                },
            }.ToFrozenDictionary(StringComparer.Ordinal);

        public static void ApplyCurrentColor(CssBox box, CssValueParser valueParser)
        {
            string[] colorProperties =
            [
                "border-top-color",
                "border-bottom-color",
                "border-left-color",
                "border-right-color",
                "background-color",
                "column-rule-color"
            ];

            var colorValue = GetPropertyValue(box, "color") ?? Keywords.Initial;

            foreach (var propertyName in colorProperties)
            {
                var value = GetPropertyValue(box, propertyName);

                if (value is not null && value.Equals(Keywords.CurrentColor, StringComparison.OrdinalIgnoreCase))
                {
                    SetPropertyValue(valueParser, box, propertyName, colorValue);
                }
            }
        }
    }
}
