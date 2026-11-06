using System;
using System.Collections.Generic;
using System.Globalization;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Svg.Smil
{
    /// <summary>Which animation element: <c>&lt;animate&gt;</c> (and the deprecated <c>&lt;animateColor&gt;</c>), <c>&lt;set&gt;</c>, <c>&lt;animateTransform&gt;</c> or <c>&lt;animateMotion&gt;</c>.</summary>
    internal enum SmilKind { Animate, Set, Transform, Motion }

    /// <summary>How the values of an animation are spread over its simple duration (SMIL Animation §3.2.3, <c>calcMode</c>).</summary>
    internal enum SmilCalcMode { Linear, Discrete, Paced, Spline }

    /// <summary>One entry of a <c>begin</c> or <c>end</c> list: an offset from the document's start, or a time relative to another animation's begin or end.</summary>
    /// <param name="Seconds">the offset (for a sync-base, the offset from its begin or end)</param>
    /// <param name="SyncId">the id of the animation element a sync-base names, or null for an offset</param>
    /// <param name="SyncEnd">whether a sync-base is the other animation's end (<c>id.end</c>) and not its begin</param>
    internal readonly record struct SmilTime(double Seconds, string? SyncId = null, bool SyncEnd = false);

    /// <summary>One animation element of an inline <c>&lt;svg&gt;</c>, read once: what it animates, when it plays and what it gives.</summary>
    internal sealed class SmilAnimation
    {
        public required SmilKind Kind { get; init; }

        /// <summary>The animation element's own box.</summary>
        public required ISmilNode Element { get; init; }

        /// <summary>The element it animates: its parent, or the one its <c>href</c> names.</summary>
        public required ISmilNode Target { get; init; }

        /// <summary>The document order of the element, which breaks a tie in the order animations apply in.</summary>
        public int Order { get; init; }

        /// <summary>The <c>attributeName</c>; for an <c>animateTransform</c> always <c>transform</c>; null for <c>animateMotion</c>.</summary>
        public string? Attribute { get; init; }

        public string? Id { get; init; }

        // Timing
        public List<SmilTime> Begin { get; init; } = [];
        public List<SmilTime> End { get; init; } = [];
        public bool HasEnd { get; init; }

        /// <summary>The simple duration; null for <c>indefinite</c> or no <c>dur</c>.</summary>
        public double? Duration { get; init; }

        public double RepeatCount { get; init; } = double.NaN;
        public double RepeatDuration { get; init; } = double.NaN;
        public double Min { get; init; } = double.NaN;
        public double Max { get; init; } = double.NaN;
        public bool Freeze { get; init; }
        public string Restart { get; init; } = "always";

        // Values
        public string? From { get; init; }
        public string? To { get; init; }
        public string? By { get; init; }
        public List<string> Values { get; init; } = [];
        public List<double>? KeyTimes { get; init; }
        public List<string>? KeySplines { get; init; }
        public SmilCalcMode CalcMode { get; init; }
        public bool AdditiveSum { get; init; }
        public bool AccumulateSum { get; init; }

        // animateTransform
        public string? TransformType { get; init; }

        // animateMotion
        public string? Path { get; init; }
        public string? MPathHref { get; init; }
        public List<double>? KeyPoints { get; init; }
        public string? Rotate { get; init; }

        /// <summary>The intervals the animation is active in, worked out once all the animations are known (a sync-base needs another's).</summary>
        public List<(double Begin, double End, bool Truncated)>? Intervals { get; set; }

        /// <summary>The length of one active duration (SMIL Animation §3.6.5): the simple duration repeated, cut by <c>repeatDur</c>, <c>min</c> and <c>max</c>. Infinite when it never ends.</summary>
        public double ActiveDuration()
        {
            var simple = Duration ?? double.PositiveInfinity;
            var active = simple;

            var hasCount = !double.IsNaN(RepeatCount);
            var hasRepeatDuration = !double.IsNaN(RepeatDuration);
            if (hasCount && hasRepeatDuration) active = Math.Min(simple * RepeatCount, RepeatDuration);
            else if (hasCount) active = simple * RepeatCount;
            else if (hasRepeatDuration) active = RepeatDuration;

            if (!double.IsNaN(Max) && active > Max) active = Max;
            if (!double.IsNaN(Min) && active < Min) active = Min;
            return active;
        }

        /// <summary>Parses a SMIL clock value: <c>5s</c>, <c>100ms</c>, <c>1.5min</c>, <c>2h</c>, <c>02:30</c>, <c>01:02:30.5</c> or a bare number of seconds.</summary>
        internal static bool TryParseClock(string text, out double seconds)
        {
            seconds = 0;
            text = text.Trim();
            if (text.Length == 0) return false;

            var sign = 1.0;
            if (text[0] is '+' or '-')
            {
                sign = text[0] == '-' ? -1 : 1;
                text = text[1..].TrimStart();
            }

            if (text.Contains(':'))
            {
                var parts = text.Split(':');
                if (parts.Length is < 2 or > 3) return false;

                double total = 0;
                foreach (var part in parts)
                {
                    if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return false;
                    total = (total * 60) + value;
                }

                seconds = sign * total;
                return true;
            }

            var scale = 1.0;
            var number = text;
            if (text.EndsWith("ms", StringComparison.Ordinal)) { scale = 0.001; number = text[..^2]; }
            else if (text.EndsWith("min", StringComparison.Ordinal)) { scale = 60; number = text[..^3]; }
            else if (text.EndsWith('s')) { number = text[..^1]; }
            else if (text.EndsWith('h')) { scale = 3600; number = text[..^1]; }

            if (!double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) return false;

            seconds = sign * parsed * scale;
            return true;
        }

        /// <summary>The entries of a <c>begin</c> or <c>end</c> list. An event, an access key or <c>indefinite</c> never happens here, so it is left out.</summary>
        internal static List<SmilTime> ParseTimes(string? text)
        {
            var times = new List<SmilTime>();
            if (text is null) return times;

            foreach (var raw in text.Split(';'))
            {
                var entry = raw.Trim();
                if (entry.Length == 0 || entry.Equals("indefinite", StringComparison.Ordinal)) continue;

                if (TryParseClock(entry, out var offset))
                {
                    times.Add(new SmilTime(offset));
                    continue;
                }

                // id.begin, id.end, with an optional + or - offset
                var dot = entry.IndexOf('.');
                if (dot <= 0) continue;

                var id = entry[..dot];
                var rest = entry[(dot + 1)..];
                bool syncEnd;
                if (rest.StartsWith("begin", StringComparison.Ordinal)) { syncEnd = false; rest = rest[5..]; }
                else if (rest.StartsWith("end", StringComparison.Ordinal)) { syncEnd = true; rest = rest[3..]; }
                else continue;

                rest = rest.Trim();
                var syncOffset = 0.0;
                if (rest.Length > 0 && !TryParseClock(rest, out syncOffset)) continue;

                times.Add(new SmilTime(syncOffset, id, syncEnd));
            }

            return times;
        }
    }
}
