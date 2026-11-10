using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// The animations of one element of an inline <c>&lt;svg&gt;</c>, sampled one property at a time. An HTML box takes the sampled values as
    /// declarations on itself (<see cref="Apply"/>), so layout and paint read them like any others; the elements of an SVG are not boxes anything
    /// reads, and <see cref="SvgTreeBuilder"/> asks for each property as it resolves it (<c>ResolveStyledAttr</c>), so here the animation origin
    /// is a lookup: what value does this animation give this property, given the value it has without it.
    /// </summary>
    internal static partial class AnimationApplier
    {
        /// <summary>One animation an element names, with the point of its run the snapshot samples.</summary>
        internal sealed record ElementAnimation(KeyframeSet Set, double Progress, EasingFunction Easing);

        /// <summary>
        /// What an SVG element's box names, worked out once: the animations (in the order of <c>animation-name</c>, so a later one wins over an
        /// earlier one for a property both animate, CSS Animations 1 §3.1) and the value parser that settles <c>var()</c> in a keyframe.
        /// </summary>
        internal sealed class ElementPlan
        {
            public ElementPlan(List<ElementAnimation> animations, CssValueParser valueParser)
            {
                Animations = animations;
                ValueParser = valueParser;
            }

            public List<ElementAnimation> Animations { get; }

            public CssValueParser ValueParser { get; }
        }

        /// <summary>
        /// The animations <paramref name="box"/> names that exist in the document's <c>@keyframes</c> and are in play at the sampled point,
        /// or null when there are none (nearly every element), which is settled before anything is allocated.
        /// </summary>
        internal static ElementPlan? PlanElement(CssBox box)
        {
            if (box.HtmlContainer is not { Keyframes: { Count: > 0 } keyframes } container) return null;

            // A snapshot samples every animation at one fraction of its own run; a running clock gives the time, and each animation's delay, iterations and fill apply.
            if (container.AnimationProgress is null && container.SvgAnimationSeconds is null) return null;
            if (IsNone(box.AnimationName)) return null;

            return BuildPlan(BoxStyle(box), keyframes, container.AnimationProgress, container.SvgAnimationSeconds, new CssValueParser(container.RenderContext));
        }

        /// <summary>
        /// The animations of an element of a standalone SVG (its style is the declarations its own style sheets give it, <paramref name="style"/> reading one longhand by name) that
        /// are in play <paramref name="seconds"/> after the image began to show; null when there are none.
        /// </summary>
        internal static ElementPlan? PlanStandaloneElement(Func<string, string?> style, IReadOnlyDictionary<string, KeyframeSet> keyframes, double seconds, CssValueParser parser) =>
            keyframes.Count == 0 || IsNone(style("animation-name")) ? null : BuildPlan(style, keyframes, null, seconds, parser);

        // The longhands of the animation properties of a box, by name.
        private static Func<string, string?> BoxStyle(CssBox box) => name => name switch
        {
            "animation-name" => box.AnimationName,
            "animation-duration" => box.AnimationDuration,
            "animation-iteration-count" => box.AnimationIterationCount,
            "animation-direction" => box.AnimationDirection,
            "animation-fill-mode" => box.AnimationFillMode,
            "animation-timing-function" => box.AnimationTimingFunction,
            _ => CssUtils.GetPropertyValue(box, name)
        };

        private static ElementPlan? BuildPlan(Func<string, string?> style, IReadOnlyDictionary<string, KeyframeSet> keyframes, double? fractionOrNull, double? seconds, CssValueParser parser)
        {
            var fraction = fractionOrNull ?? 0;

            var names = SplitList(style("animation-name"));
            var durations = SplitList(style("animation-duration"));
            var delays = fractionOrNull is null ? SplitList(style("animation-delay")) : null;

            // The elements of an SVG are not restyled while it plays, so a paused animation stays where it began: its time never advances (a still frame has no play state at all).
            var playStates = fractionOrNull is null ? SplitList(style("animation-play-state")) : null;
            var iterationCounts = SplitList(style("animation-iteration-count"));
            var directions = SplitList(style("animation-direction"));
            var fillModes = SplitList(style("animation-fill-mode"));
            var timingFunctions = SplitList(style("animation-timing-function"));

            List<ElementAnimation>? animations = null;

            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keyframes.TryGetValue(Unquote(names[i]), out var set)) continue;

                var fill = Cycle(fillModes, i);
                var fillsForwards = fill.Equals("forwards", StringComparison.OrdinalIgnoreCase) || fill.Equals("both", StringComparison.OrdinalIgnoreCase);
                var progress = fractionOrNull is not null
                    ? AnimationTimeline.DirectedProgress(
                        fraction,
                        ParseSeconds(Cycle(durations, i)),
                        ParseIterationCount(Cycle(iterationCounts, i)),
                        ParseDirection(Cycle(directions, i)),
                        fillsForwards)
                    : AnimationTimeline.LiveProgress(
                        Cycle(playStates!, i).Equals("paused", StringComparison.OrdinalIgnoreCase) ? 0 : seconds!.Value,
                        ParseSeconds(Cycle(delays!, i)),
                        ParseSeconds(Cycle(durations, i)),
                        ParseIterationCount(Cycle(iterationCounts, i)),
                        ParseDirection(Cycle(directions, i)),
                        fill.Equals("backwards", StringComparison.OrdinalIgnoreCase) || fill.Equals("both", StringComparison.OrdinalIgnoreCase),
                        fillsForwards,
                        out _);

                if (progress is not { } directed) continue;

                var easing = EasingFunction.TryParse(Cycle(timingFunctions, i), out var parsed) ? parsed : EasingFunction.Ease;
                (animations ??= []).Add(new ElementAnimation(set, directed, easing));
            }

            return animations is null ? null : new ElementPlan(animations, parser);
        }

        /// <summary>
        /// When the last CSS animation <paramref name="box"/> names ends, in seconds (infinite when one repeats for ever), or null when it names none that exists.
        /// What decides whether a playing <c>&lt;svg&gt;</c> still has frames to draw.
        /// </summary>
        internal static double? ElementAnimationEnd(CssBox box) =>
            box.HtmlContainer is not { Keyframes: { Count: > 0 } keyframes } || IsNone(box.AnimationName) ? null : AnimationEnd(BoxStyle(box), keyframes);

        /// <summary>The same for an element of a standalone SVG.</summary>
        internal static double? StandaloneAnimationEnd(Func<string, string?> style, IReadOnlyDictionary<string, KeyframeSet> keyframes) =>
            keyframes.Count == 0 || IsNone(style("animation-name")) ? null : AnimationEnd(style, keyframes);

        private static double? AnimationEnd(Func<string, string?> style, IReadOnlyDictionary<string, KeyframeSet> keyframes)
        {
            var names = SplitList(style("animation-name"));
            var durations = SplitList(style("animation-duration"));
            var delays = SplitList(style("animation-delay"));
            var iterationCounts = SplitList(style("animation-iteration-count"));
            var playStates = SplitList(style("animation-play-state"));

            double? end = null;
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase) || !keyframes.ContainsKey(Unquote(names[i]))) continue;

                // A paused animation never gets anywhere (see BuildPlan), so it ends nothing.
                if (Cycle(playStates, i).Equals("paused", StringComparison.OrdinalIgnoreCase))
                {
                    end ??= 0;
                    continue;
                }

                var duration = ParseSeconds(Cycle(durations, i));
                var count = ParseIterationCount(Cycle(iterationCounts, i));
                var stop = ParseSeconds(Cycle(delays, i)) + (double.IsPositiveInfinity(count) ? double.PositiveInfinity : duration * count);
                end = Math.Max(end ?? 0, stop);
            }

            return end;
        }

        /// <summary>
        /// The value the element's animations give <paramref name="property"/>, or null when none of them animates it.
        /// A keyframe that leaves the property out of its 0% or 100% stands for the value it has without the animation (CSS Animations 1 §4.2),
        /// which for a later animation is what the earlier ones gave it.
        /// </summary>
        /// <param name="box">The element's box: <c>currentcolor</c> in a keyframe is its <c>color</c>, and its custom properties feed <c>var()</c>.</param>
        /// <param name="plan">The element's animations.</param>
        /// <param name="property">The property, as the SVG builder names it.</param>
        /// <param name="underlying">The value the property has without any animation (its declaration, else inherited or initial); called at most once.</param>
        /// <param name="parent">The parent element's value of the property, which <c>inherit</c> in a keyframe takes.</param>
        /// <param name="initial">The property's initial value, which <c>initial</c> in a keyframe takes.</param>
        /// <param name="inherited">Whether the property is inherited, which makes <c>unset</c> mean <c>inherit</c> (and otherwise <c>initial</c>).</param>
        internal static string? SampleElement(CssBox? box, ElementPlan plan, string property,
            Func<string?> underlying, Func<string?> parent, Func<string?> initial, bool inherited)
        {
            var valueParser = plan.ValueParser;
            string? result = null;
            var settled = false;
            var applied = false;
            string? underlyingValue = null;

            foreach (var animation in plan.Animations)
            {
                if (!animation.Set.DeclaredNames.Contains(property)) continue;

                if (!settled)
                {
                    underlyingValue = InUserUnits(property, underlying());
                    result = underlyingValue;
                    settled = true;
                }

                var stops = new List<ResolvedStop>(animation.Set.Stops.Count);

                foreach (var stop in animation.Set.Stops)
                {
                    if (!stop.Declarations.TryGetValue(property, out var declared)) continue;

                    var resolved = ResolveElementKeyword(declared, underlyingValue, parent, initial, inherited);
                    if (resolved is null) continue;

                    resolved = ResolveVariables(valueParser, box, resolved);
                    if (resolved is null) continue;

                    resolved = InUserUnits(property, resolved)!;

                    // A literal the property cannot take (a negative number where it takes none, a bare string for d) is an invalid declaration, and a browser
                    // drops it from the keyframe as it does at parse time. A function (calc(), var()) is valid here and is settled when it is used.
                    if (stop.IsLiteral(property) && IsInvalidLiteral(property, resolved)) continue;

                    stops.Add(new ResolvedStop(stop.Offset, stop.Easing, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [property] = resolved }));
                }

                if (stops.Count == 0) continue;

                if (Sample(valueParser, box, stops, property, animation.Progress, animation.Easing, result) is { } sampled)
                {
                    // A calc() or min() that came out below zero was kept as it is while mixing, and clamps to the property's range where it is used (CSS Values 4, range checking of calc()).
                    result = NonNegativeElementProperties.Contains(property) && IsNegativeNumber(sampled.Trim()) ? "0px" : sampled;
                    applied = true;
                }
            }

            // An animation none of whose steps gave a value (an unresolvable var(), say) leaves the declaration as it is, not its px-normalised copy.
            return applied ? result : null;
        }

        // The lengths of an SVG that take a bare number for user units (the stroke, the text and the geometry attributes). The interpolator mixes a number and a length of another unit as a
        // calc(), which is not what `stroke-width: 2` against `10px` means (user units are CSS pixels), so both sides are put in px first.
        private static readonly HashSet<string> UserUnitProperties = [with(StringComparer.OrdinalIgnoreCase),
            "stroke-width", "stroke-dashoffset", "stroke-dasharray", "font-size", "letter-spacing", "word-spacing",
            "x", "y", "cx", "cy", "r", "rx", "ry", "width", "height"
        ];

        // The properties that take no negative number: a keyframe that gives one is invalid (a calc() that comes out negative is valid, and clamps).
        private static readonly HashSet<string> NonNegativeElementProperties = [with(StringComparer.OrdinalIgnoreCase),
            "r", "rx", "ry", "width", "height", "stroke-width", "font-size"
        ];

        private static bool IsInvalidLiteral(string property, string value)
        {
            var trimmed = value.Trim();

            if (property.Equals("d", StringComparison.OrdinalIgnoreCase)) return !IsPathOrNone(trimmed);

            if (property.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase))
                return trimmed.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Any(IsNegativeNumber);

            return NonNegativeElementProperties.Contains(property) && IsNegativeNumber(trimmed);
        }

        // d takes none or path(<string>) in CSS: the bare data of the attribute, and path() with a fill rule, are not values of it.
        private static bool IsPathOrNone(string value)
        {
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            if (!value.StartsWith("path(", StringComparison.OrdinalIgnoreCase)) return false;

            var rest = value.AsSpan(5).TrimStart();
            return rest.Length > 0 && (rest[0] == '"' || rest[0] == (char)39);
        }

        // A number that is below zero: -0px is a zero, which is as valid as 0px.
        private static bool IsNegativeNumber(string text) => CssValueInterpolator.TryReadNumberWithUnit(text, out var number, out _) && number < 0;

        private static readonly Regex BareNumber = new(@"(?<![\w.%#-])[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?(?![\w.%])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string? InUserUnits(string property, string? value)
        {
            if (value is null || !UserUnitProperties.Contains(property)) return value;

            return value.IndexOf('(') < 0 ? BareNumber.Replace(value, "$0px") : value;
        }

        /// <summary>The CSS-wide keywords in a keyframe, resolved against the element as <see cref="ResolveGlobalKeyword"/> does for an HTML box.</summary>
        private static string? ResolveElementKeyword(string value, string? underlying, Func<string?> parent, Func<string?> initial, bool inherited)
        {
            if (!CssGlobalKeywords.TryParse(value, out var keyword)) return value;

            return keyword switch
            {
                CssGlobalKeyword.Inherit => parent(),
                // revert rolls the author origin back (the animation origin counts as author origin, CSS Cascade 5 section 7.3.4), and an SVG
                // element has no user-agent declaration, so what is left is what unset gives. revert-layer rolls back one layer: the animation
                // origin is a layer of its own (section 7.3.5), so what is left under it is the value without the animation.
                CssGlobalKeyword.Unset or CssGlobalKeyword.Revert => inherited ? parent() : initial(),
                CssGlobalKeyword.RevertLayer => underlying,
                _ => initial()
            };
        }
    }
}
