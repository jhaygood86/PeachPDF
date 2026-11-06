#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>What an <c>animation-timeline</c> entry names (Scroll-driven Animations 1 §2, CSS Animations 2 §3.7).</summary>
    internal enum TimelineKind
    {
        /// <summary><c>auto</c>: the document timeline, time.</summary>
        Auto,

        /// <summary><c>none</c>: no timeline, so the animation does nothing.</summary>
        None,

        /// <summary>A <c>&lt;dashed-ident&gt;</c> naming a <c>scroll-timeline-name</c> or <c>view-timeline-name</c> declared on an element.</summary>
        Named,

        /// <summary><c>scroll()</c>: how far a scroller is scrolled.</summary>
        Scroll,

        /// <summary><c>view()</c>: how much of the element is in a scroller's view.</summary>
        View,
    }

    /// <summary>One entry of <c>animation-timeline</c>.</summary>
    /// <param name="Kind">which kind</param>
    /// <param name="Name">the dashed identifier of a named timeline</param>
    /// <param name="Scroller"><c>nearest</c>, <c>root</c> or <c>self</c> for <c>scroll()</c></param>
    /// <param name="Axis"><c>block</c>, <c>inline</c>, <c>x</c> or <c>y</c>; null when not given (the declaring property's default)</param>
    /// <param name="Inset">the <c>view()</c> inset as written (<c>auto</c>, one or two <c>&lt;length-percentage&gt;</c>); null when not given</param>
    internal readonly record struct TimelineReference(TimelineKind Kind, string Name = null, string Scroller = null, string Axis = null, string Inset = null);

    /// <summary>One end of an <c>animation-range</c>: <c>normal</c>, an offset, or a named range of a view timeline with an optional offset into it.</summary>
    /// <param name="Name">the range name (<c>cover</c>, <c>contain</c>, <c>entry</c>, <c>exit</c>, <c>entry-crossing</c>, <c>exit-crossing</c>); null for <c>normal</c> or a plain offset</param>
    /// <param name="Offset">the <c>&lt;length-percentage&gt;</c> as written, or null</param>
    internal readonly record struct RangeEdge(string Name, string Offset)
    {
        internal bool IsNormal => Name is null && Offset is null;
    }

    /// <summary>
    /// The grammar of the scroll-driven animation properties, shared by the CSS-OM converters that validate a declaration and the render layer that reads it, so the
    /// two cannot disagree (one parser for one grammar).
    /// </summary>
    internal static class TimelineGrammar
    {
        private static readonly string[] RangeNames = ["cover", "contain", "entry", "exit", "entry-crossing", "exit-crossing"];

        internal static bool IsAxis(string text) =>
            text.Equals("block", StringComparison.OrdinalIgnoreCase) || text.Equals("inline", StringComparison.OrdinalIgnoreCase)
            || text.Equals("x", StringComparison.OrdinalIgnoreCase) || text.Equals("y", StringComparison.OrdinalIgnoreCase);

        internal static bool IsDashedIdent(string text) => text.Length > 2 && text.StartsWith("--", StringComparison.Ordinal) && !text.Contains(' ') && !text.Contains('(');

        internal static bool IsRangeName(string text)
        {
            foreach (var name in RangeNames)
            {
                if (text.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>Whether <paramref name="text"/> is a <c>&lt;length-percentage&gt;</c>: a number with a unit or a percentage, a bare 0, or a math function.</summary>
        internal static bool IsLengthPercentage(string text)
        {
            if (text.Length == 0) return false;
            if (text.EndsWith(')')) return text.StartsWith("calc(", StringComparison.OrdinalIgnoreCase) || text.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("max(", StringComparison.OrdinalIgnoreCase) || text.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);

            var i = 0;
            if (text[i] is '+' or '-') i++;
            var digits = 0;
            while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) { i++; digits++; }
            if (digits == 0) return false;
            if (i == text.Length) return text.Trim('+', '-') is "0" or "0.0";

            if (text[i..] == "%") return true;
            for (var j = i; j < text.Length; j++)
            {
                if (!char.IsAsciiLetter(text[j])) return false;
            }

            return true;
        }

        /// <summary>The white-space separated parts of <paramref name="text"/>, keeping a function's arguments together.</summary>
        internal static List<string> Split(string text)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var end = i == text.Length;
                var c = end ? ' ' : text[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (!char.IsWhiteSpace(c) || depth > 0)
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    parts.Add(text[start..i]);
                    start = -1;
                }
            }

            return parts;
        }

        /// <summary><c>auto | none | &lt;dashed-ident&gt; | scroll([ &lt;scroller&gt; || &lt;axis&gt; ]?) | view([ &lt;axis&gt; || &lt;'view-timeline-inset'&gt; ]?)</c> for one comma-separated entry.</summary>
        internal static bool TryParseTimeline(string text, out TimelineReference timeline)
        {
            timeline = default;
            text = text.Trim();

            if (text.Equals("auto", StringComparison.OrdinalIgnoreCase)) { timeline = new TimelineReference(TimelineKind.Auto); return true; }
            if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) { timeline = new TimelineReference(TimelineKind.None); return true; }
            if (IsDashedIdent(text)) { timeline = new TimelineReference(TimelineKind.Named, text); return true; }

            if (TryArguments(text, "scroll", out var scrollArguments))
            {
                string scroller = null, axis = null;
                foreach (var part in Split(scrollArguments))
                {
                    if (part.Equals("root", StringComparison.OrdinalIgnoreCase) || part.Equals("nearest", StringComparison.OrdinalIgnoreCase) || part.Equals("self", StringComparison.OrdinalIgnoreCase))
                    {
                        if (scroller is not null) return false;
                        scroller = part.ToLowerInvariant();
                    }
                    else if (IsAxis(part))
                    {
                        if (axis is not null) return false;
                        axis = part.ToLowerInvariant();
                    }
                    else
                    {
                        return false;
                    }
                }

                timeline = new TimelineReference(TimelineKind.Scroll, Scroller: scroller ?? "nearest", Axis: axis);
                return true;
            }

            if (TryArguments(text, "view", out var viewArguments))
            {
                string axis = null;
                var insetParts = new List<string>();
                foreach (var part in Split(viewArguments))
                {
                    if (IsAxis(part))
                    {
                        if (axis is not null) return false;
                        axis = part.ToLowerInvariant();
                    }
                    else
                    {
                        insetParts.Add(part);
                    }
                }

                string inset = null;
                if (insetParts.Count > 0)
                {
                    inset = string.Join(' ', insetParts);
                    if (!TryParseInset(inset, out _, out _)) return false;
                }

                timeline = new TimelineReference(TimelineKind.View, Axis: axis, Inset: inset);
                return true;
            }

            return false;
        }

        private static bool TryArguments(string text, string function, out string arguments)
        {
            arguments = null;
            if (!text.StartsWith(function + "(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')')) return false;

            arguments = text[(function.Length + 1)..^1].Trim();
            return !arguments.Contains('(') || arguments.Contains("calc(", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>view-timeline-inset</c>: <c>[ auto | &lt;length-percentage&gt; ]{1,2}</c>. The second value, when absent, is the first.</summary>
        internal static bool TryParseInset(string text, out string start, out string end)
        {
            start = end = null;
            var parts = Split(text.Trim());
            if (parts.Count is < 1 or > 2) return false;

            foreach (var part in parts)
            {
                if (!part.Equals("auto", StringComparison.OrdinalIgnoreCase) && !IsLengthPercentage(part)) return false;
            }

            start = parts[0];
            end = parts.Count == 2 ? parts[1] : parts[0];
            return true;
        }

        /// <summary>
        /// One end of <c>animation-range</c>: <c>normal | &lt;length-percentage&gt; | &lt;timeline-range-name&gt; &lt;length-percentage&gt;?</c>. A trailing name alone is that range's
        /// start (or end, for the end value) and an offset of the name is within the named range.
        /// </summary>
        internal static bool TryParseRangeEdge(string text, out RangeEdge edge)
        {
            edge = default;
            var parts = Split(text.Trim());
            if (parts.Count == 0 || parts.Count > 2) return false;

            if (parts.Count == 1)
            {
                if (parts[0].Equals("normal", StringComparison.OrdinalIgnoreCase)) { edge = new RangeEdge(null, null); return true; }
                if (IsRangeName(parts[0])) { edge = new RangeEdge(parts[0].ToLowerInvariant(), null); return true; }
                if (IsLengthPercentage(parts[0])) { edge = new RangeEdge(null, parts[0]); return true; }
                return false;
            }

            if (!IsRangeName(parts[0]) || !IsLengthPercentage(parts[1])) return false;

            edge = new RangeEdge(parts[0].ToLowerInvariant(), parts[1]);
            return true;
        }

        /// <summary><c>none | &lt;dashed-ident&gt;</c> for one entry of a timeline name list.</summary>
        internal static bool IsTimelineName(string text) => text.Equals("none", StringComparison.OrdinalIgnoreCase) || IsDashedIdent(text);

        /// <summary>An entry of <c>timeline-scope</c>: <c>none | all | &lt;dashed-ident&gt;</c>.</summary>
        internal static bool IsTimelineScopeEntry(string text) => IsTimelineName(text) || text.Equals("all", StringComparison.OrdinalIgnoreCase);
    }
}
