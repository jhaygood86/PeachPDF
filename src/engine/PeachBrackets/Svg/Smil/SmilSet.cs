using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Svg.Smil
{
    /// <summary>
    /// Every SMIL animation element of one inline <c>&lt;svg&gt;</c>: read from its boxes once, with their begin and end times resolved against each other, and
    /// sampled at any time to give the value an animated attribute has then (SMIL Animation 3, as SVG 1.1 and SVG 2 use it).
    /// </summary>
    internal sealed class SmilSet
    {
        private readonly Dictionary<CssBox, List<SmilAnimation>> _byTarget = [];
        private readonly Dictionary<string, CssBox> _elementsById = new(StringComparer.Ordinal);

        private SmilSet(List<SmilAnimation> animations)
        {
            Animations = animations;
            foreach (var animation in animations)
            {
                if (!_byTarget.TryGetValue(animation.Target, out var list)) _byTarget[animation.Target] = list = [];
                list.Add(animation);
            }
        }

        public List<SmilAnimation> Animations { get; }

        /// <summary>Whether any animation targets <paramref name="box"/>.</summary>
        public bool Targets(CssBox box) => _byTarget.ContainsKey(box);

        #region Reading

        private static readonly string[] AnimationTags = ["animate", "animateColor", "set", "animateTransform", "animateMotion"];

        /// <summary>The animations of the <c>&lt;svg&gt;</c> <paramref name="root"/>, or null when it has none.</summary>
        internal static SmilSet? Read(CssBox root)
        {
            var found = new List<CssBox>();
            var ids = new Dictionary<string, CssBox>(StringComparer.Ordinal);
            Walk(root);
            if (found.Count == 0) return null;

            var animations = new List<SmilAnimation>();
            foreach (var element in found)
            {
                if (Create(element, ids, animations.Count) is { } animation) animations.Add(animation);
            }

            if (animations.Count == 0) return null;

            var set = new SmilSet(animations);
            foreach (var (id, box) in ids) set._elementsById[id] = box;
            set.ResolveTimes();
            return set;

            void Walk(CssBox box)
            {
                foreach (var child in box.Boxes)
                {
                    if (child.HtmlTag is not { } tag) continue;

                    if (child.GetAttribute("id", null) is { Length: > 0 } id) ids.TryAdd(id, child);
                    if (AnimationTags.Any(name => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) found.Add(child);

                    Walk(child);
                }
            }
        }

        private static SmilAnimation? Create(CssBox element, Dictionary<string, CssBox> ids, int order)
        {
            var name = element.HtmlTag!.Name;
            var kind = name.Equals("set", StringComparison.OrdinalIgnoreCase) ? SmilKind.Set
                : name.Equals("animateTransform", StringComparison.OrdinalIgnoreCase) ? SmilKind.Transform
                : name.Equals("animateMotion", StringComparison.OrdinalIgnoreCase) ? SmilKind.Motion
                : SmilKind.Animate;

            // The target is the parent element, or the one the href names.
            var target = element.ParentBox;
            var href = element.GetAttribute("href", null) ?? element.GetAttribute("xlink:href", null);
            if (href is { Length: > 1 } && href[0] == '#' && ids.TryGetValue(href[1..], out var referenced)) target = referenced;
            if (target?.HtmlTag is null) return null;

            string? attribute = kind switch
            {
                SmilKind.Transform => "transform",
                SmilKind.Motion => null,
                _ => element.GetAttribute("attributeName", null)?.Trim()
            };

            if (kind != SmilKind.Motion && string.IsNullOrEmpty(attribute)) return null;

            // dur: a clock value above zero, or indefinite (also what "media" and a missing dur mean here). A zero or malformed one is an error and the animation is ignored.
            double? duration = null;
            if (element.GetAttribute("dur", null) is { } dur && !dur.Trim().Equals("indefinite", StringComparison.Ordinal) && !dur.Trim().Equals("media", StringComparison.Ordinal))
            {
                if (!SmilAnimation.TryParseClock(dur, out var seconds) || seconds <= 0) return null;
                duration = seconds;
            }

            var beginText = element.GetAttribute("begin", null);
            var begin = beginText is null ? [new SmilTime(0)] : SmilAnimation.ParseTimes(beginText);
            var endText = element.GetAttribute("end", null);

            var repeatCount = double.NaN;
            if (element.GetAttribute("repeatCount", null) is { } count)
            {
                if (count.Trim().Equals("indefinite", StringComparison.Ordinal)) repeatCount = double.PositiveInfinity;
                else if (double.TryParse(count, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedCount) && parsedCount > 0) repeatCount = parsedCount;
            }

            var repeatDuration = double.NaN;
            if (element.GetAttribute("repeatDur", null) is { } repeatDur)
            {
                if (repeatDur.Trim().Equals("indefinite", StringComparison.Ordinal)) repeatDuration = double.PositiveInfinity;
                else if (SmilAnimation.TryParseClock(repeatDur, out var parsedRepeat) && parsedRepeat > 0) repeatDuration = parsedRepeat;
            }

            var min = double.NaN;
            if (element.GetAttribute("min", null) is { } minText && SmilAnimation.TryParseClock(minText, out var parsedMin) && parsedMin >= 0) min = parsedMin;
            var max = double.NaN;
            if (element.GetAttribute("max", null) is { } maxText)
            {
                if (maxText.Trim().Equals("indefinite", StringComparison.Ordinal)) max = double.NaN;
                else if (SmilAnimation.TryParseClock(maxText, out var parsedMax) && parsedMax > 0) max = parsedMax;
            }

            var calc = (element.GetAttribute("calcMode", null) ?? (kind == SmilKind.Motion ? "paced" : "linear")).Trim().ToLowerInvariant() switch
            {
                "discrete" => SmilCalcMode.Discrete,
                "paced" => SmilCalcMode.Paced,
                "spline" => SmilCalcMode.Spline,
                _ => SmilCalcMode.Linear
            };

            string? mpath = null;
            if (kind == SmilKind.Motion)
            {
                foreach (var child in element.Boxes)
                {
                    if (child.HtmlTag?.Name.Equals("mpath", StringComparison.OrdinalIgnoreCase) == true)
                        mpath = child.GetAttribute("href", null) ?? child.GetAttribute("xlink:href", null);
                }
            }

            return new SmilAnimation
            {
                Kind = kind,
                Element = element,
                Target = target,
                Order = order,
                Attribute = attribute,
                Id = element.GetAttribute("id", null),
                Begin = begin,
                End = endText is null ? [] : SmilAnimation.ParseTimes(endText),
                HasEnd = endText is not null,
                Duration = duration,
                RepeatCount = repeatCount,
                RepeatDuration = repeatDuration,
                Min = min,
                Max = max,
                Freeze = element.GetAttribute("fill", null)?.Trim().Equals("freeze", StringComparison.Ordinal) == true,
                Restart = element.GetAttribute("restart", null)?.Trim() ?? "always",
                From = element.GetAttribute("from", null),
                To = element.GetAttribute("to", null),
                By = element.GetAttribute("by", null),
                Values = SmilValues.SplitList(element.GetAttribute("values", null)),
                KeyTimes = SmilValues.ParseNumbers(element.GetAttribute("keyTimes", null)),
                KeySplines = element.GetAttribute("keySplines", null) is { } splines ? SmilValues.SplitList(splines) : null,
                CalcMode = calc,
                AdditiveSum = element.GetAttribute("additive", null)?.Trim().Equals("sum", StringComparison.Ordinal) == true,
                AccumulateSum = element.GetAttribute("accumulate", null)?.Trim().Equals("sum", StringComparison.Ordinal) == true,
                TransformType = element.GetAttribute("type", null)?.Trim(),
                Path = element.GetAttribute("path", null),
                MPathHref = mpath,
                KeyPoints = SmilValues.ParseNumbers(element.GetAttribute("keyPoints", null)),
                Rotate = element.GetAttribute("rotate", null)?.Trim()
            };
        }

        #endregion

        #region Timing

        private const int MaxIntervals = 1000;

        private void ResolveTimes()
        {
            var byId = new Dictionary<string, SmilAnimation>(StringComparer.Ordinal);
            foreach (var animation in Animations)
            {
                if (animation.Id is { Length: > 0 } id) byId.TryAdd(id, animation);
            }

            foreach (var animation in Animations) ResolveIntervals(animation, byId, []);
        }

        private static void ResolveIntervals(SmilAnimation animation, Dictionary<string, SmilAnimation> byId, HashSet<SmilAnimation> resolving)
        {
            if (animation.Intervals is not null) return;

            // A begin that waits on itself (directly or through others) never resolves.
            if (!resolving.Add(animation))
            {
                animation.Intervals = [];
                return;
            }

            var begins = InstanceTimes(animation.Begin, animation, byId, resolving);
            var ends = InstanceTimes(animation.End, animation, byId, resolving);
            begins.Sort();
            ends.Sort();

            var activeDuration = animation.ActiveDuration();
            var intervals = new List<(double Begin, double End, bool Truncated)>();

            for (var i = 0; i < begins.Count && intervals.Count < MaxIntervals; i++)
            {
                var begin = begins[i];
                if (intervals.Count > 0)
                {
                    if (animation.Restart == "never") break;
                    if (animation.Restart == "whenNotActive" && begin < intervals[^1].End) continue;
                }

                var end = begin + activeDuration;

                // An end time after the begin cuts the interval short.
                if (animation.HasEnd && ends.Count > 0)
                {
                    foreach (var candidate in ends)
                    {
                        if (candidate > begin)
                        {
                            end = Math.Min(end, candidate);
                            break;
                        }
                    }
                }

                var truncated = false;
                if (animation.Restart == "always")
                {
                    for (var j = i + 1; j < begins.Count; j++)
                    {
                        if (begins[j] > begin)
                        {
                            if (begins[j] < end)
                            {
                                end = begins[j];
                                truncated = true;
                            }

                            break;
                        }
                    }
                }

                intervals.Add((begin, end, truncated));
            }

            animation.Intervals = intervals;
            resolving.Remove(animation);
        }

        private static List<double> InstanceTimes(List<SmilTime> times, SmilAnimation self, Dictionary<string, SmilAnimation> byId, HashSet<SmilAnimation> resolving)
        {
            var result = new List<double>();
            foreach (var time in times)
            {
                if (time.SyncId is null)
                {
                    result.Add(time.Seconds);
                    continue;
                }

                if (!byId.TryGetValue(time.SyncId, out var other) || ReferenceEquals(other, self)) continue;

                ResolveIntervals(other, byId, resolving);
                if (other.Intervals is null) continue;

                foreach (var (begin, end, _) in other.Intervals)
                {
                    var at = (time.SyncEnd ? end : begin) + time.Seconds;
                    if (!double.IsInfinity(at)) result.Add(at);
                }
            }

            return result;
        }

        /// <summary>Whether anything can still change after <paramref name="time"/>: an animation is running or yet to begin (one that has ended and froze holds one value).</summary>
        public bool ChangesAfter(double time)
        {
            foreach (var animation in Animations)
            {
                if (animation.Intervals is null) continue;
                foreach (var (_, end, _) in animation.Intervals)
                {
                    if (end > time) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How long a still frame spans: the end of the longest animation's first interval, or, for one that never ends, its first simple duration.
        /// <c>AnimationProgress</c> is a fraction of this.
        /// </summary>
        public double StillFrameLength
        {
            get
            {
                double length = 0;
                foreach (var animation in Animations)
                {
                    if (animation.Intervals is not { Count: > 0 } intervals) continue;

                    var (begin, end, _) = intervals[0];
                    var span = double.IsInfinity(end) ? animation.Duration ?? 0 : end;
                    length = Math.Max(length, Math.Max(0, begin) + Math.Max(0, double.IsInfinity(end) ? span : span - Math.Max(0, begin)));
                }

                return length;
            }
        }

        // Where an animation is at <time>: inside an interval, or held at the end of the last one by fill="freeze"; false when it has no effect.
        private static bool TryPhase(SmilAnimation animation, double time, out double progress, out int iteration)
        {
            progress = 0;
            iteration = 0;

            if (animation.Intervals is not { Count: > 0 } intervals) return false;

            var found = -1;
            for (var i = 0; i < intervals.Count; i++)
            {
                if (intervals[i].Begin <= time) found = i;
            }

            if (found < 0) return false;

            var (begin, end, truncated) = intervals[found];
            var frozen = false;
            double local;
            if (time < end)
            {
                local = time - begin;
            }
            else
            {
                if (!animation.Freeze || truncated) return false;
                frozen = true;
                local = end - begin;
            }

            if (animation.Duration is not { } duration)
            {
                // An indefinite simple duration never gets past its start.
                return true;
            }

            var whole = (int)Math.Floor(local / duration);
            var remainder = local - (whole * duration);

            // A freeze at a whole number of iterations holds the end of the last one, not the start of the next.
            if (frozen && remainder < 1e-9 && whole > 0)
            {
                whole--;
                remainder = duration;
            }

            iteration = whole;
            progress = Math.Clamp(remainder / duration, 0, 1);
            return true;
        }

        #endregion

        #region Sampling

        /// <summary>
        /// The value animations give <paramref name="attribute"/> of <paramref name="box"/> at <paramref name="time"/>, or null when none does. Each animation
        /// that has an effect applies in the order it began, over the value the ones before gave (SMIL Animation §3.7.1, the animation sandwich).
        /// </summary>
        /// <param name="box">The element the attribute is on.</param>
        /// <param name="attribute">The attribute (or presentation property) name.</param>
        /// <param name="time">The time, in seconds from the start of the document.</param>
        /// <param name="underlying">The value without any animation, which an animation with no <c>from</c> starts at and an additive one adds to.</param>
        /// <param name="parser">The parser that settles colours and keywords while values are mixed; null mixes numbers only.</param>
        public string? Sample(CssBox box, string attribute, double time, Func<string?> underlying, CssValueParser? parser)
        {
            if (!_byTarget.TryGetValue(box, out var candidates)) return null;

            if (attribute.Equals("transform", StringComparison.Ordinal)) return SampleTransform(box, candidates, time, underlying);

            List<(SmilAnimation Animation, double Progress, int Iteration)>? active = null;
            foreach (var animation in candidates)
            {
                if (animation.Kind is SmilKind.Motion or SmilKind.Transform) continue;
                if (!string.Equals(animation.Attribute, attribute, StringComparison.Ordinal)) continue;
                if (!TryPhase(animation, time, out var progress, out var iteration)) continue;

                (active ??= []).Add((animation, progress, iteration));
            }

            if (active is null) return null;

            var result = underlying();
            string? produced = null;

            foreach (var (animation, progress, iteration) in OrderOfBegin(active, time))
            {
                var value = Value(animation, progress, iteration, result, attribute, box, parser);
                if (value is null) continue;

                // Additive animation adds to what is below it; set is additive only when it says so.
                if (animation.AdditiveSum && result is not null && SmilValues.Add(result, value) is { } sum) value = sum;

                result = value;
                produced = value;
            }

            return produced;
        }

        private static IEnumerable<(SmilAnimation Animation, double Progress, int Iteration)> OrderOfBegin(
            List<(SmilAnimation Animation, double Progress, int Iteration)> active, double time) =>
            active.OrderBy(entry => BeginOf(entry.Animation, time)).ThenBy(entry => entry.Animation.Order);

        private static double BeginOf(SmilAnimation animation, double time)
        {
            double begin = double.NegativeInfinity;
            if (animation.Intervals is null) return begin;

            foreach (var interval in animation.Intervals)
            {
                if (interval.Begin <= time) begin = interval.Begin;
            }

            return begin;
        }

        private static string? Value(SmilAnimation animation, double progress, int iteration, string? underlying, string attribute, CssBox box, CssValueParser? parser)
        {
            if (animation.Kind == SmilKind.Set) return animation.To;

            // The values the animation moves through.
            var values = animation.Values;
            if (values.Count == 0)
            {
                if (animation.From is not null && animation.To is not null) values = [animation.From, animation.To];
                else if (animation.From is not null && animation.By is not null)
                    values = [animation.From, SmilValues.Add(animation.From, animation.By) ?? animation.By];
                else if (animation.To is not null && underlying is not null) values = [underlying, animation.To];
                else if (animation.By is not null && underlying is not null)
                    values = [underlying, SmilValues.Add(underlying, animation.By) ?? underlying];
                else return null;
            }

            var value = Segment(animation, values, progress, attribute, box, parser);

            // accumulate="sum" starts each repeat where the last one ended.
            if (animation.AccumulateSum && iteration > 0 && values.Count > 0 && SmilValues.Scale(values[^1], iteration) is { } carried
                && SmilValues.Add(value, carried) is { } accumulated)
            {
                value = accumulated;
            }

            return value;
        }

        // The value at progress p of the way through a list of them: the segment p falls in, shaped by calcMode.
        private static string Segment(SmilAnimation animation, List<string> values, double progress, string attribute, CssBox? box, CssValueParser? parser)
        {
            if (values.Count == 1) return values[0];

            var count = values.Count;
            var times = animation.KeyTimes is { } keyTimes && keyTimes.Count == count ? keyTimes : null;

            if (animation.CalcMode == SmilCalcMode.Discrete)
            {
                var index = count - 1;
                for (var i = 0; i < count; i++)
                {
                    var start = times is null ? (double)i / count : times[i];
                    var next = i + 1 < count ? (times is null ? (double)(i + 1) / count : times[i + 1]) : 1.0001;
                    if (progress >= start && progress < next)
                    {
                        index = i;
                        break;
                    }
                }

                return values[index];
            }

            // paced: the time between values follows the distance between them.
            double[]? pacedTimes = null;
            if (animation.CalcMode == SmilCalcMode.Paced && times is null)
            {
                var distances = new double[count - 1];
                double total = 0;
                for (var i = 0; i < count - 1; i++)
                {
                    distances[i] = SmilValues.Distance(values[i], values[i + 1]);
                    total += distances[i];
                }

                if (total > 0)
                {
                    pacedTimes = new double[count];
                    double along = 0;
                    for (var i = 0; i < count; i++)
                    {
                        pacedTimes[i] = along / total;
                        if (i < count - 1) along += distances[i];
                    }
                }
            }

            double Time(int i) => times is not null ? times[i] : pacedTimes is not null ? pacedTimes[i] : (double)i / (count - 1);

            var segment = count - 2;
            for (var i = 0; i < count - 1; i++)
            {
                if (progress < Time(i + 1) || i == count - 2)
                {
                    segment = i;
                    break;
                }
            }

            var from = Time(segment);
            var to = Time(segment + 1);
            var local = to > from ? Math.Clamp((progress - from) / (to - from), 0, 1) : 1;

            if (animation.CalcMode == SmilCalcMode.Spline && animation.KeySplines is { } splines && segment < splines.Count)
                local = SmilValues.Spline(splines[segment], local);

            return SmilValues.Mix(parser, attribute, values[segment], values[segment + 1], local, box);
        }

        private string? SampleTransform(CssBox box, List<SmilAnimation> candidates, double time, Func<string?> underlying)
        {
            string? result = null;
            string? current = null;
            var produced = false;

            string Current()
            {
                current ??= underlying() ?? string.Empty;
                return result ?? current;
            }

            var active = new List<(SmilAnimation Animation, double Progress, int Iteration)>();
            foreach (var animation in candidates)
            {
                if (animation.Kind != SmilKind.Transform) continue;
                if (TryPhase(animation, time, out var progress, out var iteration)) active.Add((animation, progress, iteration));
            }

            foreach (var (animation, progress, iteration) in OrderOfBegin(active, time))
            {
                var type = animation.TransformType ?? "translate";
                var value = Value(animation, progress, iteration, null, "transform", box, null);
                if (value is null) continue;

                var text = $"{type}({value})";
                result = animation.AdditiveSum ? $"{Current()} {text}".Trim() : text;
                produced = true;
            }

            // A motion path is a supplemental transform after the element's own (and any animateTransform's).
            foreach (var animation in candidates)
            {
                if (animation.Kind != SmilKind.Motion) continue;
                if (!TryPhase(animation, time, out var progress, out _)) continue;
                if (MotionTransform(animation, progress) is not { } motion) continue;

                result = $"{Current()} {motion}".Trim();
                produced = true;
            }

            return produced ? result : null;
        }

        private string? MotionTransform(SmilAnimation animation, double progress)
        {
            var path = animation.Path;
            if (path is null && animation.MPathHref is { Length: > 1 } href && href[0] == '#' && _elementsById.TryGetValue(href[1..], out var referenced))
                path = referenced.GetAttribute("d", null);

            if (path is null)
            {
                // from/to/by/values give a straight line through points.
                var points = animation.Values.Count > 0
                    ? animation.Values
                    : animation.From is not null && animation.To is not null ? [animation.From, animation.To]
                    : animation.To is not null ? ["0,0", animation.To]
                    : animation.By is not null ? ["0,0", animation.By] : null;

                if (points is null || points.Count == 0) return null;
                path = "M " + string.Join(" L ", points);
            }

            var segments = SvgPathDataParser.Parse(path);
            if (segments.Count == 0) return null;

            var geometry = new SvgTextPathGeometry(segments);
            if (geometry.IsEmpty) return null;

            var fraction = progress;
            if (animation.KeyPoints is { } keyPoints && animation.KeyTimes is { } keyTimes && keyPoints.Count == keyTimes.Count && keyPoints.Count > 1)
            {
                fraction = keyPoints[^1];
                for (var i = 0; i < keyTimes.Count - 1; i++)
                {
                    if (progress <= keyTimes[i + 1] || i == keyTimes.Count - 2)
                    {
                        var span = keyTimes[i + 1] - keyTimes[i];
                        var local = span > 0 ? Math.Clamp((progress - keyTimes[i]) / span, 0, 1) : 1;
                        fraction = keyPoints[i] + ((keyPoints[i + 1] - keyPoints[i]) * local);
                        break;
                    }
                }
            }

            var (x, y, tangent) = geometry.PointAtLength(Math.Clamp(fraction, 0, 1) * geometry.TotalLength);

            var angle = 0.0;
            if (animation.Rotate is { } rotate)
            {
                if (rotate.Equals("auto", StringComparison.Ordinal)) angle = tangent;
                else if (rotate.Equals("auto-reverse", StringComparison.Ordinal)) angle = tangent + 180;
                else if (double.TryParse(rotate, NumberStyles.Float, CultureInfo.InvariantCulture, out var fixedAngle)) angle = fixedAngle;
            }

            var text = string.Create(CultureInfo.InvariantCulture, $"translate({x:R} {y:R})");
            return angle == 0 ? text : string.Create(CultureInfo.InvariantCulture, $"{text} rotate({angle:R})");
        }

        #endregion
    }
}
