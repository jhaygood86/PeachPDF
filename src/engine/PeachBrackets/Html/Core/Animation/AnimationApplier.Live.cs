using System;
using System.Collections.Generic;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Updates;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;

namespace PeachBrackets.Html.Animation
{
    // The live side of the sampler: the same keyframe sampling the still frame uses, but driven by a running clock (AnimationClock) one
    // moment at a time, writing only the properties that cannot change layout.
    internal static partial class AnimationApplier
    {
        /// <summary>One animation a box names, resolved once and then sampled every frame.</summary>
        internal sealed class LiveTrack
        {
            public required string Name { get; init; }
            public required int Index { get; init; }
            public required List<ResolvedStop> Stops { get; init; }

            /// <summary>The properties this animation animates that a live frame can write without a layout pass.</summary>
            public required List<string> Properties { get; init; }

            public required double DelaySeconds { get; init; }
            public required double DurationSeconds { get; init; }
            public required double IterationCount { get; init; }
            public required AnimationDirectionKind Direction { get; init; }
            public required bool FillsBackwards { get; init; }
            public required bool FillsForwards { get; init; }
            public required bool Paused { get; init; }
            public required EasingFunction Easing { get; init; }

            /// <summary>The animation's <c>animation-composition</c>: <c>replace</c>, <c>add</c> or <c>accumulate</c>.</summary>
            public string Composition { get; init; } = "replace";

            /// <summary>Whether this animation animates <paramref name="property"/>.</summary>
            public bool HasProperty(string property)
            {
                for (var i = 0; i < Properties.Count; i++)
                {
                    if (string.Equals(Properties[i], property, StringComparison.OrdinalIgnoreCase)) return true;
                }

                return false;
            }

            /// <summary>The keyframes of each property, with the implicit ends filled in, kept while the value the box has without the animation is the same.</summary>
            internal Dictionary<string, (string? Underlying, List<(double Offset, string Value, string? Easing)> Frames)> FrameCache { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Time the animation has run, not counting time it was paused.</summary>
            public double Elapsed { get; set; }

            /// <summary>Whether the animation is past its end: nothing it does changes any more.</summary>
            public bool Finished { get; set; }

            /// <summary>Whether <c>animationstart</c> has been sent.</summary>
            public bool Started { get; set; }

            /// <summary>The iteration the last <c>animationiteration</c> (or the start) was for.</summary>
            public int Iteration { get; set; }

            /// <summary>Whether <c>animationend</c> has been sent.</summary>
            public bool Ended { get; set; }

            /// <summary>Held by <see cref="DocumentAnimation.Pause"/> (the animation's own <c>animation-play-state</c> is <see cref="Paused"/>).</summary>
            public bool ControlPaused { get; set; }

            /// <summary>How fast time passes for the animation (<see cref="DocumentAnimation.PlaybackRate"/>).</summary>
            public double PlaybackRate { get; set; } = 1;

            /// <summary>Cancelled through <see cref="DocumentAnimation.Cancel"/>: it has no effect until the element names it again.</summary>
            public bool Cancelled { get; set; }

            /// <summary>Taken away by a restyle: the element no longer names it.</summary>
            public bool Removed { get; set; }

            /// <summary>The object the host holds for this animation, once it has asked for one.</summary>
            public DocumentAnimation? Handle { get; set; }

            /// <summary>The scroll or view timeline the animation runs on, or null for the document's time.</summary>
            public ResolvedTimeline? Timeline { get; init; }

            /// <summary>Whether the animation is on a timeline that does not exist (<c>animation-timeline: none</c>, a name nothing declares, a scroller that does not scroll): it does nothing.</summary>
            public bool TimelineInactive { get; init; }

            /// <summary>Whether progress comes from a scroll position and not from time.</summary>
            public bool IsScrollDriven => Timeline is not null || TimelineInactive;

            /// <summary>The start of the range of the timeline the animation plays over (<c>animation-range-start</c>).</summary>
            public RangeEdge RangeStart { get; init; }

            /// <summary>The end of that range (<c>animation-range-end</c>).</summary>
            public RangeEdge RangeEnd { get; init; }

            /// <summary>Where the animation is now, 0 to 1 through the range, as the clock last worked it out from the scroll position; null before it or after it with no fill.</summary>
            public double? ScrollProgress { get; set; }

            /// <summary>How long the animation's active phase lasts, in seconds; infinite for one that never ends.</summary>
            public double ActiveSeconds => double.IsInfinity(IterationCount) ? double.PositiveInfinity : DurationSeconds * IterationCount;
        }

        /// <summary>
        /// The animations <paramref name="box"/> names, resolved for live sampling. Only properties that are paint-only are kept (a property that
        /// can move or resize something needs a layout pass a live frame does not run). Empty when the box names none.
        /// </summary>
        internal static List<LiveTrack> BuildLiveTracks(CssValueParser valueParser, CssBox box)
        {
            var tracks = new List<LiveTrack>();
            if (box.HtmlContainer?.Keyframes is not { Count: > 0 } keyframes) return tracks;
            if (IsNone(box.AnimationName)) return tracks;

            var names = SplitList(box.AnimationName);
            var durations = SplitList(box.AnimationDuration);
            var delays = SplitList(CssUtils.GetPropertyValue(box, "animation-delay"));
            var iterationCounts = SplitList(box.AnimationIterationCount);
            var directions = SplitList(box.AnimationDirection);
            var fillModes = SplitList(box.AnimationFillMode);
            var timingFunctions = SplitList(box.AnimationTimingFunction);
            var playStates = SplitList(CssUtils.GetPropertyValue(box, "animation-play-state"));
            var compositions = SplitList(box.AnimationComposition);
            var timelines = SplitList(box.AnimationTimeline);
            var rangeStarts = SplitList(box.AnimationRangeStart);
            var rangeEnds = SplitList(box.AnimationRangeEnd);

            for (var i = 0; i < names.Count; i++)
            {
                if (names[i].Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                if (!keyframes.TryGetValue(Unquote(names[i]), out var set)) continue;

                var stops = ResolveStops(valueParser, box, set, (name, declared) => ResolveGlobalKeyword(box, name, declared, null));
                var properties = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stop in stops)
                {
                    foreach (var property in stop.Values.Keys)
                    {
                        if ((InteractionRestyler.IsLiveAnimatable(property) || IsLiveCustomProperty(box, property)) && seen.Add(property)) properties.Add(property);
                    }
                }

                if (properties.Count == 0) continue;

                // A scroll or view timeline replaces time; one that does not exist leaves the animation with no effect.
                var timelineEntry = TimelineGrammar.TryParseTimeline(Cycle(timelines, i), out var timelineReference) ? timelineReference : new TimelineReference(TimelineKind.Auto);
                var (timeline, inactive) = ResolveTimeline(box, timelineEntry);

                var fill = Cycle(fillModes, i);
                var fills = fill.Trim().ToLowerInvariant();
                tracks.Add(new LiveTrack
                {
                    Name = names[i],
                    Index = i,
                    Stops = stops,
                    Properties = properties,
                    DelaySeconds = ParseSeconds(Cycle(delays, i)),
                    DurationSeconds = ParseSeconds(Cycle(durations, i)),
                    IterationCount = ParseIterationCount(Cycle(iterationCounts, i)),
                    Direction = ParseDirection(Cycle(directions, i)),
                    FillsBackwards = fills is "backwards" or "both",
                    FillsForwards = fills is "forwards" or "both",
                    Paused = Cycle(playStates, i).Trim().Equals("paused", StringComparison.OrdinalIgnoreCase),
                    Easing = EasingFunction.TryParse(Cycle(timingFunctions, i), out var parsed) ? parsed : EasingFunction.Ease,
                    Composition = Cycle(compositions, i).Trim().ToLowerInvariant() is { Length: > 0 } composition ? composition : "replace",
                    Timeline = timeline,
                    TimelineInactive = inactive,
                    RangeStart = TimelineGrammar.TryParseRangeEdge(Cycle(rangeStarts, i), out var rangeStart) ? rangeStart : default,
                    RangeEnd = TimelineGrammar.TryParseRangeEdge(Cycle(rangeEnds, i), out var rangeEnd) ? rangeEnd : default,
                });
            }

            return tracks;
        }

        /// <summary>
        /// Whether <paramref name="property"/> is a registered custom property (<c>@property</c>) whose syntax mixes by interpolating. Only those play live: an unregistered
        /// one, or one of another syntax, is a step, and a step is not worth the cascade of everything that might read it.
        /// </summary>
        internal static bool IsLiveCustomProperty(CssBox box, string property) =>
            PropertyFactory.IsCustomPropertyName(property)
            && box.HtmlContainer?.RegisteredProperties is { } registry && registry.TryGetValue(property, out var registered) && registered.CanInterpolate;

        /// <summary>
        /// The value of <paramref name="property"/> for the track as it stands now, or null when the track does not animate it at this moment
        /// (so the value the box has without it stands).
        /// </summary>
        internal static string? SampleLive(CssValueParser valueParser, CssBox box, LiveTrack track, string property, string? underlying)
        {
            // On a scroll or view timeline the clock has already worked progress out from the scroll position, and nothing is ever past its end: it can be scrolled back.
            var finished = false;
            var progress = track.IsScrollDriven
                ? track.ScrollProgress
                : AnimationTimeline.LiveProgress(track.Elapsed, track.DelaySeconds, track.DurationSeconds, track.IterationCount,
                    track.Direction, track.FillsBackwards, track.FillsForwards, out finished);
            track.Finished = finished;

            if (progress is not { } directed) return null;

            if (!track.FrameCache.TryGetValue(property, out var cached) || cached.Underlying != underlying)
            {
                var built = BuildFrames(track.Stops, property, underlying, new CompositionContext(valueParser, box, track.Composition));
                if (built is null) return null;
                track.FrameCache[property] = cached = (underlying, built);
            }

            if (PropertyFactory.IsCustomPropertyName(property))
            {
                RegisteredProperty? registered = null;
                box.HtmlContainer?.RegisteredProperties?.TryGetValue(property, out registered);
                return SampleFrames(valueParser, box, cached.Frames, property, directed, track.Easing,
                    (from, to, eased) => MixCustomProperty(valueParser, registered, property, from, to, eased));
            }

            return SampleFrames(valueParser, box, cached.Frames, property, directed, track.Easing);
        }

        /// <summary>The value of <paramref name="property"/> a transition from <paramref name="from"/> to <paramref name="to"/> has after <paramref name="eased"/> of its way.</summary>
        internal static string MixTransition(CssValueParser valueParser, CssBox box, string property, string from, string to, double eased) =>
            CssValueInterpolator.Interpolate(valueParser, property, from, to, eased, box);

        /// <summary>Parses a <c>transition-*</c> or <c>animation-*</c> list entry as seconds.</summary>
        internal static double LiveSeconds(string text) => ParseSeconds(text);

        internal static string LiveCycle(List<string> list, int index) => Cycle(list, index);
    }
}
