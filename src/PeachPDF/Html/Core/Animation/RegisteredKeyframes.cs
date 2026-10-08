using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;

namespace PeachPDF.Html.Core.Animation
{
    /// <summary>One keyframe: the declarations that apply at <see cref="Offset"/> (0 to 1) of an animation.</summary>
    /// <param name="Offset">Where the keyframe sits in the animation, 0 for <c>0%</c>/<c>from</c> to 1 for <c>100%</c>/<c>to</c>.</param>
    /// <param name="Declarations">Property name to specified value; no <c>!important</c>, global-keyword or <c>animation-*</c> entries.</param>
    /// <param name="Easing">The keyframe's own <c>animation-timing-function</c>, which eases the interval that starts here; null to use the animation's.</param>
    internal sealed record KeyframeStop(double Offset, Dictionary<string, string> Declarations, string? Easing);

    /// <summary>
    /// The keyframes of one <c>@keyframes</c> rule, in order of offset.
    /// </summary>
    internal sealed class KeyframeSet
    {
        public KeyframeSet(IReadOnlyList<KeyframeStop> stops)
        {
            Stops = stops;
        }

        public IReadOnlyList<KeyframeStop> Stops { get; }

        /// <summary>Every property any keyframe declares, in the order first declared.</summary>
        public IEnumerable<string> Properties =>
            Stops.SelectMany(stop => stop.Declarations.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The document's <c>@keyframes</c> rules by name (CSS Animations 1 §4). Rebuilt each parse pass, like the
    /// other at-rule registries; only built when animations are going to be sampled at all.
    /// </summary>
    internal static class RegisteredKeyframes
    {
        public static Dictionary<string, KeyframeSet> BuildRegistry(CssData cssData)
        {
            var registry = new Dictionary<string, KeyframeSet>(StringComparer.Ordinal);

            // EnumerateRulesRecursive descends into @media/@supports/@layer/@container and keeps source order,
            // so a later @keyframes of the same name replaces an earlier one (§4.1: the last one wins).
            foreach (var rule in cssData.EnumerateRulesRecursive().OfType<KeyframesRule>())
            {
                if (string.IsNullOrEmpty(rule.Name)) continue;
                registry[rule.Name] = Build(rule);
            }

            return registry;
        }

        private static KeyframeSet Build(KeyframesRule rule)
        {
            var byOffset = new SortedDictionary<double, KeyframeStop>();

            foreach (var keyframe in rule.Rules.OfType<KeyframeRule>())
            {
                if (keyframe.Key is not { } selector) continue;

                foreach (var stop in selector.Stops)
                {
                    var offset = stop.Value / 100d;

                    // §4.2: a keyframe selector outside 0% to 100% is not a keyframe.
                    if (!(offset >= 0 && offset <= 1)) continue;

                    // Keyframes at one offset combine; the later declaration of a property wins.
                    if (!byOffset.TryGetValue(offset, out var existing))
                        byOffset[offset] = existing = new KeyframeStop(offset, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null);

                    var easing = existing.Easing;

                    foreach (var property in keyframe.Style)
                    {
                        // §4.2: !important declarations in a keyframe are ignored.
                        if (property.IsImportant) continue;

                        if (property.Name.Equals(PropertyNames.AnimationTimingFunction, StringComparison.OrdinalIgnoreCase))
                        {
                            easing = FirstListEntry(property.Value);
                            continue;
                        }

                        if (!IsAnimatable(property.Name) || CssGlobalKeywords.TryParse(property.Value, out _)) continue;

                        existing.Declarations[property.Name] = property.Value;
                    }

                    if (!ReferenceEquals(easing, existing.Easing))
                        byOffset[offset] = existing with { Easing = easing };
                }
            }

            return new KeyframeSet(byOffset.Values.Where(stop => stop.Declarations.Count > 0 || stop.Easing is not null).ToList());
        }

        /// <summary>
        /// Whether a declaration in a keyframe can animate at all. The <c>animation-*</c> and <c>transition-*</c>
        /// properties are excluded by name (§4.2: they are ignored in a keyframe, bar the timing function handled
        /// separately), and so is a custom property, which only animates once registered with a syntax.
        /// </summary>
        private static bool IsAnimatable(string name) =>
            !name.StartsWith("--", StringComparison.Ordinal)
            && !name.StartsWith("animation", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("transition", StringComparison.OrdinalIgnoreCase);

        /// <summary>The first entry of a comma-separated list, keeping commas inside parentheses together.</summary>
        internal static string FirstListEntry(string list)
        {
            var depth = 0;
            for (var i = 0; i < list.Length; i++)
            {
                if (list[i] == '(') depth++;
                else if (list[i] == ')') depth--;
                else if (list[i] == ',' && depth == 0) return list[..i].Trim();
            }

            return list.Trim();
        }
    }
}
