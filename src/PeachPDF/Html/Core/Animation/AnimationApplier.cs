using System;
using System.Collections.Generic;
using System.Globalization;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Animation
{
    /// <summary>
    /// Renders a CSS animation as the single frame a PDF can hold. There is no timeline: each animation a box
    /// names is sampled at <see cref="PdfGenerateConfig.AnimationProgress"/> of its own run and the values it
    /// produces are written onto the box as ordinary declarations, so layout and paint need to know nothing about
    /// animation, and an animated <c>width</c> moves its neighbours exactly as a declared one would.
    /// </summary>
    internal static class AnimationApplier
    {
        /// <summary>
        /// Applies every animation <paramref name="box"/> names. Called by the cascade between its author-normal
        /// and author-<c>!important</c> phases, which is where the animation origin sits (CSS Cascade 4 §6.1):
        /// above every normal declaration, below every important one.
        /// </summary>
        /// <param name="valueParser">The cascade's value parser.</param>
        /// <param name="box">The box being cascaded.</param>
        /// <param name="fraction">Where in its run each animation is sampled, 0 to 1.</param>
        /// <param name="pendingVarProperties">The cascade's deferred <c>var()</c> declarations; an animated
        /// property's earlier entry is dropped so its resolution cannot overwrite the animated value.</param>
        public static void Apply(CssValueParser valueParser, CssBox box, double fraction, Dictionary<string, string> pendingVarProperties)
        {
            var names = SplitList(box.AnimationName);
            if (names.Count == 0) return;

            if (box.HtmlContainer?.Keyframes is not { Count: > 0 } keyframes) return;

            var durations = SplitList(box.AnimationDuration);
            var iterationCounts = SplitList(box.AnimationIterationCount);
            var directions = SplitList(box.AnimationDirection);
            var fillModes = SplitList(box.AnimationFillMode);
            var timingFunctions = SplitList(box.AnimationTimingFunction);

            // A later animation in the list wins over an earlier one for a property they both animate (§3.1), and
            // sees the earlier one's result as the value an implicit keyframe stands for - so applying them in
            // order, straight onto the box, is exactly the composition the spec describes.
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keyframes.TryGetValue(names[i], out var set)) continue;

                // The lists are repeated or truncated to the length of animation-name (§3.1).
                var fill = Cycle(fillModes, i);
                var progress = AnimationTimeline.DirectedProgress(
                    fraction,
                    ParseSeconds(Cycle(durations, i)),
                    ParseIterationCount(Cycle(iterationCounts, i)),
                    ParseDirection(Cycle(directions, i)),
                    fill.Equals("forwards", StringComparison.OrdinalIgnoreCase) || fill.Equals("both", StringComparison.OrdinalIgnoreCase));

                if (progress is not { } directed) continue;

                var easing = EasingFunction.TryParse(Cycle(timingFunctions, i), out var parsed) ? parsed : EasingFunction.Ease;

                foreach (var property in set.Properties)
                {
                    var value = Sample(valueParser, box, set, property, directed, easing);
                    if (value is null) continue;

                    pendingVarProperties.Remove(property);
                    CssUtils.SetPropertyValue(valueParser, box, property, value);
                }
            }
        }

        /// <summary>The value of one property at <paramref name="progress"/> through the keyframes, or null if it has none.</summary>
        private static string? Sample(CssValueParser valueParser, CssBox box, KeyframeSet set, string property, double progress, EasingFunction animationEasing)
        {
            var frames = new List<(double Offset, string Value, string? Easing)>();

            foreach (var stop in set.Stops)
            {
                if (!stop.Declarations.TryGetValue(property, out var declared)) continue;

                var value = ResolveVariables(valueParser, box, declared);
                if (value is null) continue;

                frames.Add((stop.Offset, value, stop.Easing));
            }

            if (frames.Count == 0) return null;

            // §4.2: a property missing from the 0% or 100% keyframe is filled with the value it has without the
            // animation (the "underlying value") - which is what the box holds now.
            var underlying = CssUtils.GetPropertyValue(box, property);
            if (underlying is not null)
            {
                if (frames[0].Offset > 0) frames.Insert(0, (0, underlying, null));
                if (frames[^1].Offset < 1) frames.Add((1, underlying, null));
            }

            if (progress <= frames[0].Offset) return frames[0].Value;
            if (progress >= frames[^1].Offset) return frames[^1].Value;

            var index = 0;
            while (index < frames.Count - 2 && progress >= frames[index + 1].Offset) index++;

            var from = frames[index];
            var to = frames[index + 1];

            var local = (progress - from.Offset) / (to.Offset - from.Offset);
            var interval = from.Easing is not null && EasingFunction.TryParse(from.Easing, out var own) ? own : animationEasing;
            var eased = interval.Evaluate(local);

            // Exactly at an end the specified text is kept rather than reformatted.
            if (eased <= 0) return from.Value;
            if (eased >= 1) return to.Value;

            return CssValueInterpolator.Interpolate(valueParser, property, from.Value, to.Value, eased);
        }

        /// <summary>Substitutes <c>var()</c> in a keyframe value against the box's custom properties; null when it cannot be resolved.</summary>
        private static string? ResolveVariables(CssValueParser valueParser, CssBox box, string value)
        {
            if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0) return value;

            var registered = box.HtmlContainer?.RegisteredProperties;
            var context = registered is { Count: > 0 } ? new CssVarResolver.VarContext(registered, valueParser) : null;
            var result = CssVarResolver.Substitute(box, value, [], [], [], context);

            return result.Success ? result.Value : null;
        }

        /// <summary>
        /// Splits a comma-separated list (commas inside parentheses stay put). The animation properties hold one
        /// entry per animation; an empty value is an empty list.
        /// </summary>
        internal static List<string> SplitList(string? list)
        {
            var entries = new List<string>();
            if (string.IsNullOrWhiteSpace(list)) return entries;

            var depth = 0;
            var start = 0;
            for (var i = 0; i < list.Length; i++)
            {
                switch (list[i])
                {
                    case '(': depth++; break;
                    case ')': depth--; break;
                    case ',' when depth == 0:
                        entries.Add(list[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }

            entries.Add(list[start..].Trim());
            return entries;
        }

        private static string Cycle(List<string> list, int index) => list.Count == 0 ? string.Empty : list[index % list.Count];

        private static double ParseSeconds(string text)
        {
            text = text.Trim();

            if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
                return milliseconds / 1000;

            if (text.EndsWith('s') || text.EndsWith('S'))
                return double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;

            return 0;
        }

        private static double ParseIterationCount(string text)
        {
            text = text.Trim();
            if (text.Equals("infinite", StringComparison.OrdinalIgnoreCase)) return double.PositiveInfinity;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var count) ? count : 1;
        }

        private static AnimationDirectionKind ParseDirection(string text) => text.Trim().ToLowerInvariant() switch
        {
            "reverse" => AnimationDirectionKind.Reverse,
            "alternate" => AnimationDirectionKind.Alternate,
            "alternate-reverse" => AnimationDirectionKind.AlternateReverse,
            _ => AnimationDirectionKind.Normal
        };
    }
}
