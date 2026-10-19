using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Animation
{
    /// <summary>
    /// Renders a CSS animation as the single frame a PDF can hold. There is no timeline: each animation a box
    /// names is sampled at <c>PdfGenerateConfig.AnimationProgress</c> of its own run and the values it
    /// produces are written onto the box as ordinary declarations, so layout and paint need to know nothing about
    /// animation, and an animated <c>width</c> moves its neighbours exactly as a declared one would.
    /// </summary>
    internal static partial class AnimationApplier
    {
        /// <summary>
        /// Applies every animation <paramref name="box"/> names. Called by the cascade between its author-normal
        /// and author-<c>!important</c> phases, which is where the animation origin sits (CSS Cascade 4 §6.1):
        /// above every normal declaration, below every important one.
        /// </summary>
        /// <param name="valueParser">The cascade's value parser.</param>
        /// <param name="box">The box being cascaded.</param>
        /// <param name="fraction">Where in its run each animation is sampled, 0 to 1.</param>
        /// <param name="pendingVarProperties">The cascade's deferred <c>var()</c> declarations; those covering an
        /// animated property are resolved and removed here, so the animated value is not overwritten later.</param>
        /// <param name="uaSnapshot">The box's properties as the UA stylesheet left them, which a keyframe's
        /// <c>revert</c> rolls back to; null when no keyframe uses <c>revert</c>.</param>
        /// <param name="colorIsImportant">Whether an <c>!important</c> declaration sets the box's <c>color</c>. It outranks the
        /// animation origin (CSS Cascade 4 §6.1), so the cascade has already put it on the box and an animation of <c>color</c>
        /// is left unapplied: every <c>currentcolor</c> the animation mixes then stands for the colour the box really has.</param>
        public static void Apply(CssValueParser valueParser, CssBox box, double fraction, Dictionary<string, string> pendingVarProperties, IReadOnlyDictionary<string, string?>? uaSnapshot, bool colorIsImportant)
        {
            // Nearly every box names no animation; settle that before allocating anything.
            if (box.HtmlContainer?.Keyframes is not { Count: > 0 } keyframes) return;

            // The cascade resolves var() last, so `animation-name: var(--n)` or `animation: var(--a)` is still
            // deferred here; settle the animation-* declarations before they are read.
            if (pendingVarProperties.Count > 0)
            {
                DomParser.ResolveDeferredVarProperties(valueParser, box, pendingVarProperties,
                    name => name.StartsWith("animation", StringComparison.OrdinalIgnoreCase));
            }

            if (IsNone(box.AnimationName)) return;

            var names = SplitList(box.AnimationName);

            var durations = SplitList(box.AnimationDuration);
            var iterationCounts = SplitList(box.AnimationIterationCount);
            var directions = SplitList(box.AnimationDirection);
            var fillModes = SplitList(box.AnimationFillMode);
            var timingFunctions = SplitList(box.AnimationTimingFunction);

            // currentcolor in one animation's keyframes stands for the `color` the box has at that moment, and that `color` may come
            // from another animation in the list, a later one included. When several animations are in play and some of them animate
            // `color`, it is therefore applied for all of them first, and everything else after; otherwise one pass does it all.
            var colorPasses = !colorIsImportant && names.Count > 1 && AnimatesColor(names, keyframes);

            // A later animation in the list wins over an earlier one for a property they both animate (§3.1), and
            // sees the earlier one's result as the value an implicit keyframe stands for - so applying them in
            // order, straight onto the box, is exactly the composition the spec describes.
            foreach (var (i, pass) in Passes(names.Count, colorPasses))
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keyframes.TryGetValue(Unquote(names[i]), out var set)) continue;
                if (pass == ColorPass.ColorOnly && !set.DeclaredNames.Contains("color")) continue;

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

                // An animated property's author declaration may still be waiting on var() resolution, which the
                // cascade only does at its very end. Settle those now: the implicit 0%/100% keyframe stands for the
                // value the author wrote (§4.2), and a deferred entry left behind would resolve over the animated
                // value afterwards. A deferred shorthand (margin: var(--m)) is settled when any longhand it covers
                // is animated; the longhands the animation does not own keep what that resolution gave them.
                if (pendingVarProperties.Count > 0)
                {
                    DomParser.ResolveDeferredVarProperties(valueParser, box, pendingVarProperties, name => Overlaps(name, set.DeclaredNames));
                }

                var stops = ResolveStops(valueParser, box, set, (name, declared) => ResolveGlobalKeyword(box, name, declared, uaSnapshot));
                var animated = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var stop in stops)
                {
                    foreach (var property in stop.Values.Keys)
                    {
                        if (seen.Add(property)) animated.Add(property);
                    }
                }

                // An important `color` is already on the box and cannot be animated; the passes split `color` from the rest.
                if (colorIsImportant || pass == ColorPass.WithoutColor)
                    animated.RemoveAll(property => property.Equals("color", StringComparison.OrdinalIgnoreCase));
                else if (pass == ColorPass.ColorOnly)
                    animated.RemoveAll(property => !property.Equals("color", StringComparison.OrdinalIgnoreCase));

                // currentcolor in another property's keyframe stands for this animation's `color` at the same moment (CSS Color 4,
                // Color Interpolation), so `color` is sampled and written first.
                // The box's `color` may itself still be waiting on var(): it is what currentcolor resolves to, so settle it for the
                // sampling when a property being animated can name currentcolor (when `color` is animated, the overlap rule above
                // already has). Only for the sampling: the declaration goes back to the pending set, so the cascade's last var() pass
                // still gives the box's own color the !important custom property it may refer to, as it does without an animation.
                if (pendingVarProperties.Count > 0 && ReferencesCurrentColor(box, stops, animated))
                {
                    var colorDeclarations = pendingVarProperties.Where(pair => pair.Key.Equals("color", StringComparison.OrdinalIgnoreCase)).ToList();

                    DomParser.ResolveDeferredVarProperties(valueParser, box, pendingVarProperties, name => name.Equals("color", StringComparison.OrdinalIgnoreCase));

                    foreach (var (name, value) in colorDeclarations) pendingVarProperties[name] = value;
                }

                var colorIndex = animated.FindIndex(property => property.Equals("color", StringComparison.OrdinalIgnoreCase));
                if (colorIndex > 0)
                {
                    animated.RemoveAt(colorIndex);
                    animated.Insert(0, "color");
                }

                foreach (var property in animated)
                {
                    var value = Sample(valueParser, box, stops, property, directed, easing, CssUtils.GetPropertyValue(box, property));
                    if (value is not null) CssUtils.SetPropertyValue(valueParser, box, property, value);
                }
            }
        }

        /// <summary>Which of the box's animated properties a pass of <see cref="Apply"/> samples.</summary>
        private enum ColorPass { Single, ColorOnly, WithoutColor }

        private static readonly ColorPass[] OnePass = [ColorPass.Single];
        private static readonly ColorPass[] TwoPasses = [ColorPass.ColorOnly, ColorPass.WithoutColor];

        /// <summary>Every animation of the list in order, once per pass: a single pass, or the <c>color</c> pass then the rest.</summary>
        private static IEnumerable<(int Index, ColorPass Pass)> Passes(int count, bool colorPasses)
        {
            foreach (var pass in colorPasses ? TwoPasses : OnePass)
            {
                for (var i = 0; i < count; i++) yield return (i, pass);
            }
        }

        private static bool AnimatesColor(List<string> names, IReadOnlyDictionary<string, KeyframeSet> keyframes)
        {
            foreach (var name in names)
            {
                if (keyframes.TryGetValue(Unquote(name), out var set) && set.DeclaredNames.Contains("color")) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a property other than <c>color</c> that these keyframes animate can be <c>currentcolor</c>: a keyframe says so,
        /// or the value the property has without the animation (the implicit 0% or 100% keyframe) does.
        /// </summary>
        private static bool ReferencesCurrentColor(CssBox box, List<ResolvedStop> stops, List<string> animated)
        {
            static bool IsCurrentColor(string? value) => value?.Contains("currentcolor", StringComparison.OrdinalIgnoreCase) == true;

            foreach (var property in animated)
            {
                if (property.Equals("color", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsCurrentColor(CssUtils.GetPropertyValue(box, property))) return true;

                foreach (var stop in stops)
                {
                    if (stop.Values.TryGetValue(property, out var value) && IsCurrentColor(value)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An <c>animation-name</c> may be a string, which names the same animation as the identifier spelled the same way.
        /// The CSS-OM stores it double-quoted with <c>\"</c> and <c>\\</c> escaped (<c>ValueExtensions.ToAnimationName</c>).
        /// </summary>
        private static string Unquote(string name) =>
            name.Length >= 2 && name[0] == '"' && name[^1] == '"'
                ? name[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\")
                : name;

        internal static bool IsNone(string? list) =>
            string.IsNullOrWhiteSpace(list) || list.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether a deferred declaration for <paramref name="name"/> covers a property the animation owns.</summary>
        private static bool Overlaps(string name, HashSet<string> animated)
        {
            if (animated.Contains(name)) return true;

            foreach (var longhand in PropertyFactory.Instance.GetLonghands(name))
            {
                if (animated.Contains(longhand)) return true;
            }

            return false;
        }

        /// <summary>One keyframe with its values settled: <c>var()</c> substituted, global keywords resolved against the box, shorthands split into longhands.</summary>
        private sealed record ResolvedStop(double Offset, string? Easing, Dictionary<string, string> Values);

        private static List<ResolvedStop> ResolveStops(CssValueParser valueParser, CssBox box, KeyframeSet set, Func<string, string, string?> resolveKeyword)
        {
            var stops = new List<ResolvedStop>(set.Stops.Count);

            foreach (var stop in set.Stops)
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (name, declared) in stop.Declarations)
                {
                    var resolved = resolveKeyword(name, declared);
                    if (resolved is null) continue;

                    resolved = ResolveVariables(valueParser, box, resolved);
                    if (resolved is null) continue;

                    foreach (var (longhand, value) in ExpandShorthand(name, resolved))
                        values[longhand] = value;
                }

                stops.Add(new ResolvedStop(stop.Offset, stop.Easing, values));
            }

            return stops;
        }

        /// <summary>
        /// <c>inherit</c>, <c>initial</c> and <c>unset</c> in a keyframe resolve against the element the animation
        /// runs on, as the cascade resolves them in a rule. <c>revert</c> rolls back to the UA level (the animation
        /// origin counts as author origin for it, CSS Cascade 5 §7.3.4), and <c>revert-layer</c> to the layer below:
        /// the animation origin is a layer of its own (§7.3.5), so that is the author-level value the box holds now.
        /// Null when the property has no such value.
        /// </summary>
        private static string? ResolveGlobalKeyword(CssBox box, string name, string value, IReadOnlyDictionary<string, string?>? uaSnapshot)
        {
            if (!CssGlobalKeywords.TryParse(value, out var keyword)) return value;

            return keyword switch
            {
                CssGlobalKeyword.Inherit when box.ParentBox is not null => CssUtils.GetPropertyValue(box.ParentBox, name),
                CssGlobalKeyword.Unset when box.ParentBox is not null && CssDefaults.InheritedProperties.Contains(name) => CssUtils.GetPropertyValue(box.ParentBox, name),
                // The snapshot holds null for a property the UA left unset, which reverts to the initial value.
                CssGlobalKeyword.Revert => uaSnapshot is not null && uaSnapshot.TryGetValue(name, out var uaValue) && uaValue is not null
                    ? uaValue
                    : CssDefaults.GetInitialValue(name),
                CssGlobalKeyword.RevertLayer => CssUtils.GetPropertyValue(box, name),
                _ => CssDefaults.GetInitialValue(name)
            };
        }

        /// <summary>
        /// The CSS-OM expands a shorthand into longhands as it parses, except one holding <c>var()</c>, which is kept
        /// whole until the reference resolves. This is that expansion, for a keyframe value that has just resolved.
        /// A longhand (the common case) comes back as itself.
        /// </summary>
        private static IEnumerable<(string Name, string Value)> ExpandShorthand(string name, string value)
        {
            if (!PropertyFactory.Instance.IsShorthand(name))
            {
                yield return (name, value);
                yield break;
            }

            if (StylesheetParser.Default.ParseDeclaration($"{name}: {value}") is not ShorthandProperty { HasValue: true } shorthand) yield break;

            var longhands = PropertyFactory.Instance.CreateLonghandsFor(name);
            shorthand.Export(longhands);

            foreach (var longhand in longhands)
            {
                // A longhand the shorthand text did not mention resets to its initial value, as in the cascade.
                var longhandValue = longhand.HasValue && longhand.Value != Keywords.Initial
                    ? longhand.Value
                    : CssDefaults.GetInitialValue(longhand.Name);

                if (longhandValue is not null) yield return (longhand.Name, longhandValue);
            }
        }

        /// <summary>The value of one property at <paramref name="progress"/> through the keyframes, or null if it has none.</summary>
        private static string? Sample(CssValueParser valueParser, CssBox box, List<ResolvedStop> stops, string property, double progress, EasingFunction animationEasing, string? underlying)
        {
            var frames = new List<(double Offset, string Value, string? Easing)>();

            foreach (var stop in stops)
            {
                if (stop.Values.TryGetValue(property, out var value))
                    frames.Add((stop.Offset, value, stop.Easing));
            }

            if (frames.Count == 0) return null;

            // §4.2: a property missing from the 0% or 100% keyframe is filled with the value it has without the
            // animation (the "underlying value") - which is what the box holds now.
            if (underlying is not null)
            {
                if (frames[0].Offset > 0) frames.Insert(0, (0, underlying, null));
                if (frames[^1].Offset < 1) frames.Add((1, underlying, null));
            }

            // Before the first keyframe and past the last one, the nearest value holds. Exactly *at* the first
            // keyframe is the start of its interval, which its easing still shapes: step-start shows its first step
            // there, not the 0% value. Exactly at the last is the end of the last interval, which its easing still
            // shapes too: every older easing maps 1 to 1, but linear(0, 1, 0) comes back to its start.
            if (progress < frames[0].Offset || frames.Count == 1) return frames[0].Value;
            if (progress > frames[^1].Offset) return frames[^1].Value;

            var index = 0;
            while (index < frames.Count - 2 && progress >= frames[index + 1].Offset) index++;

            var from = frames[index];
            var to = frames[index + 1];

            // Two keyframes at the same offset leave no interval to shape. The loop above only stops on such a pair at the
            // end of the list, where the later one wins, as it always did.
            if (to.Offset <= from.Offset) return to.Value;

            var local = (progress - from.Offset) / (to.Offset - from.Offset);
            var interval = from.Easing is not null && EasingFunction.TryParse(from.Easing, out var own) ? own : animationEasing;
            var eased = interval.Evaluate(local);

            // Exactly at an end the specified text is kept rather than reformatted. Only exactly: an easing that
            // overshoots (a cubic-bezier with a y outside 0 to 1) is past the end, and mixed beyond it.
            if (eased == 0) return from.Value;
            if (eased == 1) return to.Value;

            return CssValueInterpolator.Interpolate(valueParser, property, from.Value, to.Value, eased, box);
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
        /// Splits a comma-separated list (commas inside parentheses or a quoted string stay put). The animation
        /// properties hold one entry per animation; an empty value is an empty list.
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
                    case '"' or '\'':
                        // A quoted string (an animation-name): skip to its closing quote, honouring backslash escapes.
                        var quote = list[i];
                        for (i++; i < list.Length && list[i] != quote; i++)
                        {
                            if (list[i] == '\\') i++;
                        }
                        break;
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
