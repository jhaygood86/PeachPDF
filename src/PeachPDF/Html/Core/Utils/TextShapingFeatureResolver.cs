using System;
using System.Collections.Generic;
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
        internal static LigatureFeatures ResolveLigatures(string value)
        {
            if (value == Keywords.None)
                return LigatureFeatures.Required;

            var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resolved = LigatureFeatures.Required;
            if (!Contains(tokens, Keywords.NoCommonLigatures)) resolved |= LigatureFeatures.Common;
            if (!Contains(tokens, Keywords.NoContextual)) resolved |= LigatureFeatures.Contextual;
            if (Contains(tokens, Keywords.DiscretionaryLigatures)) resolved |= LigatureFeatures.Discretionary;
            if (Contains(tokens, Keywords.HistoricalLigatures)) resolved |= LigatureFeatures.Historical;
            return resolved;
        }

        /// <summary>The caps feature <paramref name="value"/> requests, ungated - the caller must still
        /// check its own resolved font's <c>SupportsFontVariantCaps</c> capability before actually
        /// requesting this from the shaping layer (see this type's own doc comment, and
        /// <c>DerivedStyle.ActualFontVariantCaps</c> for the HTML-side gating).</summary>
        internal static FontVariantCapsFeature ResolveCapsRequested(FontVariantCapsMode value) => value switch
        {
            FontVariantCapsMode.SmallCaps => FontVariantCapsFeature.SmallCaps,
            FontVariantCapsMode.AllSmallCaps => FontVariantCapsFeature.AllSmallCaps,
            FontVariantCapsMode.PetiteCaps => FontVariantCapsFeature.PetiteCaps,
            FontVariantCapsMode.AllPetiteCaps => FontVariantCapsFeature.AllPetiteCaps,
            FontVariantCapsMode.Unicase => FontVariantCapsFeature.Unicase,
            FontVariantCapsMode.TitlingCaps => FontVariantCapsFeature.TitlingCaps,
            _ => FontVariantCapsFeature.None,
        };

        /// <summary>The same, for the keyword text of an SVG presentation attribute; an unrecognized keyword requests nothing.</summary>
        internal static FontVariantCapsFeature ResolveCapsRequested(string value) =>
            Map.FontVariantCapsModes.TryGetValue(value, out var mode) ? ResolveCapsRequested(mode) : FontVariantCapsFeature.None;

        /// <summary>The position feature <paramref name="value"/> requests, ungated - the caller must
        /// still check its own resolved font's <c>SupportsFontVariantPosition</c> capability to decide
        /// between real substitution and a synthesized sub/superscript (see
        /// <c>DerivedStyle.ActualFontVariantPosition</c> for the HTML-side gating).</summary>
        internal static FontVariantPositionFeature ResolvePositionRequested(FontVariantPositionMode value) => value switch
        {
            FontVariantPositionMode.Sub => FontVariantPositionFeature.Sub,
            FontVariantPositionMode.Super => FontVariantPositionFeature.Super,
            _ => FontVariantPositionFeature.None,
        };

        /// <summary>The same, for the keyword text of an SVG presentation attribute; an unrecognized keyword requests nothing.</summary>
        internal static FontVariantPositionFeature ResolvePositionRequested(string value) =>
            Map.FontVariantPositionModes.TryGetValue(value, out var mode) ? ResolvePositionRequested(mode) : FontVariantPositionFeature.None;

        internal static NumericFeatures ResolveNumeric(string value)
        {
            var resolved = NumericFeatures.None;
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                resolved |= token switch
                {
                    Keywords.LiningNums => NumericFeatures.LiningNums,
                    Keywords.OldstyleNums => NumericFeatures.OldstyleNums,
                    Keywords.ProportionalNums => NumericFeatures.ProportionalNums,
                    Keywords.TabularNums => NumericFeatures.TabularNums,
                    Keywords.DiagonalFractions => NumericFeatures.DiagonalFractions,
                    Keywords.StackedFractions => NumericFeatures.StackedFractions,
                    Keywords.Ordinal => NumericFeatures.Ordinal,
                    Keywords.SlashedZero => NumericFeatures.SlashedZero,
                    _ => NumericFeatures.None,
                };
            }
            return resolved;
        }

        internal static EastAsianFeatures ResolveEastAsian(string value)
        {
            var resolved = EastAsianFeatures.None;
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                resolved |= token switch
                {
                    Keywords.Jis78Forms => EastAsianFeatures.Jis78,
                    Keywords.Jis83Forms => EastAsianFeatures.Jis83,
                    Keywords.Jis90Forms => EastAsianFeatures.Jis90,
                    Keywords.Jis04Forms => EastAsianFeatures.Jis04,
                    Keywords.Simplified => EastAsianFeatures.Simplified,
                    Keywords.Traditional => EastAsianFeatures.Traditional,
                    Keywords.FullWidth => EastAsianFeatures.FullWidth,
                    Keywords.ProportionalWidth => EastAsianFeatures.ProportionalWidth,
                    Keywords.Ruby => EastAsianFeatures.Ruby,
                    _ => EastAsianFeatures.None,
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
