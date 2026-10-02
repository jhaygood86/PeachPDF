using PeachPDF.CSS;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Expands the text shorthands (<c>font</c>, <c>font-variant</c>, <c>text-decoration</c>) that SVG text reads from an inline
    /// <c>style=""</c> or a presentation attribute into the longhands the tree builder consumes - through the CSS-OM's own shorthand
    /// grammars, so the expansion (including the reset of every longhand the shorthand does not list) is the HTML cascade's.
    /// </summary>
    internal static class SvgTextShorthands
    {
        /// <summary>Shorthand → the longhand names it sets (the ones SVG text reads).</summary>
        private static readonly (string Shorthand, string[] Longhands)[] Shorthands =
        [
            ("font", ["font-style", "font-variant-caps", "font-weight", "font-stretch", "font-size", "font-family",
                "font-variant-ligatures", "font-variant-position", "font-variant-numeric", "font-variant-east-asian",
                "font-variant-alternates", "font-variant-emoji", "font-feature-settings", "font-kerning", "font-variation-settings"]),
            ("font-variant", ["font-variant-caps", "font-variant-ligatures", "font-variant-position", "font-variant-numeric",
                "font-variant-east-asian", "font-variant-alternates", "font-variant-emoji"]),
            ("text-decoration", ["text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness"]),
        ];

        private static readonly string[] None = [];

        /// <summary>Longhand → the shorthands that set it, widest first (<c>font</c> before <c>font-variant</c>).</summary>
        private static readonly Dictionary<string, string[]> ByLonghand = BuildIndex();

        private static Dictionary<string, string[]> BuildIndex()
        {
            var index = new Dictionary<string, List<string>>();
            foreach (var (shorthand, longhands) in Shorthands)
            {
                foreach (var longhand in longhands)
                {
                    if (!index.TryGetValue(longhand, out var list))
                        index[longhand] = list = [];
                    list.Add(shorthand);
                }
            }

            return index.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        /// <summary>The shorthands that set <paramref name="longhand"/>, widest first (<c>font</c> before <c>font-variant</c>); empty for most properties.</summary>
        public static string[] ShorthandsOf(string longhand) => ByLonghand.GetValueOrDefault(longhand, None);

        /// <summary>
        /// The value <paramref name="shorthandValue"/> gives <paramref name="longhand"/>, or null when the value does not parse as that
        /// shorthand. A longhand the shorthand does not mention comes back as its initial value (a CSS shorthand resets it).
        /// </summary>
        public static string? Expand(string shorthand, string shorthandValue, string longhand)
        {
            var declaration = new StyleDeclaration(new StylesheetParser());
            declaration.SetProperty(shorthand, shorthandValue);
            if (declaration.GetProperty(shorthand) is null && declaration.GetProperty(longhand) is null)
                return null;

            var value = declaration.GetPropertyValue(longhand);
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
