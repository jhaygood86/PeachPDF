using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PeachPDF.CSS;
using PeachDrawing.Text.Internal.Text;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves the CSS Fonts Level 3/4 text-shaping properties (<c>font-variant-ligatures/-caps/
    /// -numeric/-east-asian</c>, <c>font-feature-settings</c>, <c>font-kerning</c>) from a raw
    /// cascaded-value string to the typed <see cref="PeachDrawing.Text.Internal.Text"/> request types
    /// <see cref="GsubShaper.Shape"/>/<see cref="GposPositioner"/> consume - factored out of
    /// <see cref="Dom.DerivedStyle"/>'s own <c>ActualFontVariant*</c>/<c>ActualFontFeatureSettings</c>/
    /// <c>ActualFontKerning</c> properties so SVG text (<see cref="Svg.SvgTreeBuilder"/>) can resolve
    /// the exact same grammar from its own presentation-attribute/style strings, per this repo's "one
    /// parser per grammar, not a second independently-derived one" convention (see CLAUDE.md). Each
    /// method here is a pure string-in/typed-out function - the one exception, <c>font-variant-caps</c>,
    /// is deliberately split into this ungated "which keyword was requested" resolver plus a
    /// capability-gating step callers do themselves once they have a resolved font to gate against (see
    /// <c>DerivedStyle.ActualFontVariantCaps</c> for the HTML-side gating; SVG gates the same way in
    /// <c>SvgTreeBuilder.BuildTextRun</c>).
    /// </summary>
    internal static class TextShapingFeatureResolver
    {
        internal static LigatureSet ResolveLigatures(string value)
        {
            if (value == Keywords.None)
                return LigatureSet.Required;

            var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolved = LigatureSet.Required;
            if (!Contains(tokens, Keywords.NoCommonLigatures)) resolved |= LigatureSet.Common;
            if (!Contains(tokens, Keywords.NoContextual)) resolved |= LigatureSet.Contextual;
            if (Contains(tokens, Keywords.DiscretionaryLigatures)) resolved |= LigatureSet.Discretionary;
            if (Contains(tokens, Keywords.HistoricalLigatures)) resolved |= LigatureSet.Historical;
            return resolved;
        }

        /// <summary>The caps feature <paramref name="value"/> requests, ungated - the caller must still
        /// check its own resolved font's <c>SupportsFontVariantCaps</c> capability before actually
        /// requesting this from the shaping layer (see this type's own doc comment, and
        /// <c>DerivedStyle.ActualFontVariantCaps</c> for the HTML-side gating).</summary>
        internal static CapsMode ResolveCapsRequested(FontVariantCapsMode value) => value switch
        {
            FontVariantCapsMode.SmallCaps => CapsMode.SmallCaps,
            FontVariantCapsMode.AllSmallCaps => CapsMode.AllSmallCaps,
            FontVariantCapsMode.PetiteCaps => CapsMode.PetiteCaps,
            FontVariantCapsMode.AllPetiteCaps => CapsMode.AllPetiteCaps,
            FontVariantCapsMode.Unicase => CapsMode.Unicase,
            FontVariantCapsMode.TitlingCaps => CapsMode.TitlingCaps,
            _ => CapsMode.None,
        };

        /// <summary>The same, for the keyword text of an SVG presentation attribute; an unrecognized keyword requests nothing.</summary>
        internal static CapsMode ResolveCapsRequested(string value) =>
            Map.FontVariantCapsModes.TryGetValue(value, out var mode) ? ResolveCapsRequested(mode) : CapsMode.None;

        /// <summary>The position feature <paramref name="value"/> requests, ungated - the caller must
        /// still check its own resolved font's <c>SupportsFontVariantPosition</c> capability to decide
        /// between real substitution and a synthesized sub/superscript (see
        /// <c>DerivedStyle.ActualFontVariantPosition</c> for the HTML-side gating).</summary>
        internal static SubSuperMode ResolvePositionRequested(FontVariantPositionMode value) => value switch
        {
            FontVariantPositionMode.Sub => SubSuperMode.Sub,
            FontVariantPositionMode.Super => SubSuperMode.Super,
            _ => SubSuperMode.None,
        };

        /// <summary>The same, for the keyword text of an SVG presentation attribute; an unrecognized keyword requests nothing.</summary>
        internal static SubSuperMode ResolvePositionRequested(string value) =>
            Map.FontVariantPositionModes.TryGetValue(value, out var mode) ? ResolvePositionRequested(mode) : SubSuperMode.None;

        internal static NumeralSet ResolveNumeric(string value)
        {
            var resolved = NumeralSet.None;
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                resolved |= token switch
                {
                    Keywords.LiningNums => NumeralSet.LiningNums,
                    Keywords.OldstyleNums => NumeralSet.OldstyleNums,
                    Keywords.ProportionalNums => NumeralSet.ProportionalNums,
                    Keywords.TabularNums => NumeralSet.TabularNums,
                    Keywords.DiagonalFractions => NumeralSet.DiagonalFractions,
                    Keywords.StackedFractions => NumeralSet.StackedFractions,
                    Keywords.Ordinal => NumeralSet.Ordinal,
                    Keywords.SlashedZero => NumeralSet.SlashedZero,
                    _ => NumeralSet.None,
                };
            }
            return resolved;
        }

        internal static EastAsianSet ResolveEastAsian(string value)
        {
            var resolved = EastAsianSet.None;
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                resolved |= token switch
                {
                    Keywords.Jis78Forms => EastAsianSet.Jis78,
                    Keywords.Jis83Forms => EastAsianSet.Jis83,
                    Keywords.Jis90Forms => EastAsianSet.Jis90,
                    Keywords.Jis04Forms => EastAsianSet.Jis04,
                    Keywords.Simplified => EastAsianSet.Simplified,
                    Keywords.Traditional => EastAsianSet.Traditional,
                    Keywords.FullWidth => EastAsianSet.FullWidth,
                    Keywords.ProportionalWidth => EastAsianSet.ProportionalWidth,
                    Keywords.Ruby => EastAsianSet.Ruby,
                    _ => EastAsianSet.None,
                };
            }
            return resolved;
        }

        /// <summary>Parses a cascaded <c>font-feature-settings</c> string (e.g. <c>"smcp" 1, "onum" 1</c>)
        /// into (tag, value) pairs - <c>on</c>/<c>off</c> resolve to 1/0, a bare tag with no value
        /// defaults to 1. <c>normal</c> resolves to an empty list.</summary>
        internal static IReadOnlyList<(string Tag, int Value)> ResolveFeatureSettings(string value)
        {
            if (value == Keywords.Normal)
                return [];

            var entries = new List<(string, int)>();
            foreach (var rawEntry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawEntry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                var tag = parts[0].Trim('"');
                var settingValue = 1;
                if (parts.Length > 1)
                {
                    var rawValue = parts[1];
                    if (rawValue == Keywords.Off) settingValue = 0;
                    else if (rawValue != Keywords.On) int.TryParse(rawValue, out settingValue);
                }

                entries.Add((tag, settingValue));
            }
            return entries;
        }

        /// <summary>
        /// The engine's own form of a resolved (tag, value) list, for <see cref="ShapeSettings.ExplicitFeatures"/>. An empty
        /// list is always the one shared empty array, because the shaper's lookup cache compares these lists by reference and
        /// nearly every box has none.
        /// </summary>
        internal static IReadOnlyList<FeatureSetting> ToFeatureSettings(IReadOnlyList<(string Tag, int Value)> settings)
        {
            if (settings.Count == 0) return Array.Empty<FeatureSetting>();

            // Converted once per source list: every SVG run of one font context passes the same list, and a fresh
            // array for each would make the shaper's lookup cache (which compares these lists by reference) miss for
            // every run and keep an entry alive for each.
            return ConvertedSettings.GetValue(settings, static source =>
            {
                var result = new FeatureSetting[source.Count];
                for (var i = 0; i < result.Length; i++)
                    result[i] = new FeatureSetting(source[i].Tag, source[i].Value);
                return result;
            });
        }

        private static readonly ConditionalWeakTable<IReadOnlyList<(string Tag, int Value)>, IReadOnlyList<FeatureSetting>> ConvertedSettings = new();

        /// <summary><c>false</c> only for <c>none</c> - both <c>auto</c> (the initial value) and
        /// <c>normal</c> mean "apply GPOS kerning when the font and script support it."</summary>
        internal static bool ResolveKerning(FontKerningMode value) => value != FontKerningMode.None;

        /// <summary>The same, for the keyword text of an SVG presentation attribute: only a literal <c>none</c> turns kerning off.</summary>
        internal static bool ResolveKerning(string value) => value != Keywords.None;

        private static bool Contains(string[] tokens, string token)
        {
            foreach (var t in tokens)
            {
                if (t == token) return true;
            }
            return false;
        }
    }
}
