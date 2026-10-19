using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg;

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
            if (box.HtmlContainer is not { AnimationProgress: { } fraction, Keyframes: { Count: > 0 } keyframes } container) return null;
            if (IsNone(box.AnimationName)) return null;

            var names = SplitList(box.AnimationName);
            var durations = SplitList(box.AnimationDuration);
            var iterationCounts = SplitList(box.AnimationIterationCount);
            var directions = SplitList(box.AnimationDirection);
            var fillModes = SplitList(box.AnimationFillMode);
            var timingFunctions = SplitList(box.AnimationTimingFunction);

            List<ElementAnimation>? animations = null;

            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keyframes.TryGetValue(Unquote(names[i]), out var set)) continue;

                var fill = Cycle(fillModes, i);
                var progress = AnimationTimeline.DirectedProgress(
                    fraction,
                    ParseSeconds(Cycle(durations, i)),
                    ParseIterationCount(Cycle(iterationCounts, i)),
                    ParseDirection(Cycle(directions, i)),
                    fill.Equals("forwards", StringComparison.OrdinalIgnoreCase) || fill.Equals("both", StringComparison.OrdinalIgnoreCase));

                if (progress is not { } directed) continue;

                var easing = EasingFunction.TryParse(Cycle(timingFunctions, i), out var parsed) ? parsed : EasingFunction.Ease;
                (animations ??= []).Add(new ElementAnimation(set, directed, easing));
            }

            return animations is null ? null : new ElementPlan(animations, new CssValueParser(container.RenderContext));
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
        internal static string? SampleElement(CssBox box, ElementPlan plan, string property,
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
                    stops.Add(new ResolvedStop(stop.Offset, stop.Easing, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [property] = resolved }));
                }

                if (stops.Count == 0) continue;

                if (Sample(valueParser, box, stops, property, animation.Progress, animation.Easing, result) is { } sampled)
                {
                    result = sampled;
                    applied = true;
                }
            }

            // An animation none of whose steps gave a value (an unresolvable var(), say) leaves the declaration as it is, not its px-normalised copy.
            return applied ? result : null;
        }

        // The lengths of an SVG that take a bare number for user units (the stroke, the text and the geometry attributes). The interpolator mixes a number and a length of another unit as a
        // calc(), which is not what `stroke-width: 2` against `10px` means (user units are CSS pixels), so both sides are put in px first.
        private static readonly HashSet<string> UserUnitProperties = new(StringComparer.OrdinalIgnoreCase) {
            "stroke-width", "stroke-dashoffset", "stroke-dasharray", "font-size", "letter-spacing", "word-spacing",
            "x", "y", "cx", "cy", "r", "rx", "ry", "width", "height"
        };

        private static readonly Regex BareNumber = new(@"(?<![\w.%#-])[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?(?![\w.%])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static string? InUserUnits(string property, string? value) =>
            value is not null && UserUnitProperties.Contains(property) && value.IndexOf('(') < 0 ? BareNumber.Replace(value, "$0px") : value;

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
