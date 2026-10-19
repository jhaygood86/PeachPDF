using System;
using System.Collections.Generic;
using System.Globalization;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// A CSS easing function (CSS Easing 1): maps the linear progress of a keyframe interval, 0 to 1, to the
    /// progress its two values are mixed at. Parsed from the text of an <c>animation-timing-function</c> entry
    /// or a keyframe's own <c>animation-timing-function</c>.
    /// </summary>
    internal readonly struct EasingFunction
    {
        private enum Kind { Linear, CubicBezier, Steps, Points }

        private enum Jump { Start, End, None, Both }

        private readonly Kind _kind;
        private readonly double _x1, _y1, _x2, _y2;
        private readonly int _stepCount;
        private readonly Jump _jump;
        private readonly double[]? _pointInputs, _pointOutputs;

        private EasingFunction(Kind kind, double x1, double y1, double x2, double y2, int stepCount, Jump jump,
            double[]? pointInputs = null, double[]? pointOutputs = null)
        {
            _kind = kind;
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            _stepCount = stepCount;
            _jump = jump;
            _pointInputs = pointInputs;
            _pointOutputs = pointOutputs;
        }

        public static readonly EasingFunction Linear = new(Kind.Linear, 0, 0, 1, 1, 0, Jump.End);

        /// <summary>The initial value of <c>animation-timing-function</c>.</summary>
        public static readonly EasingFunction Ease = new(Kind.CubicBezier, 0.25, 0.1, 0.25, 1, 0, Jump.End);

        private static EasingFunction Bezier(double x1, double y1, double x2, double y2) =>
            new(Kind.CubicBezier, x1, y1, x2, y2, 0, Jump.End);

        /// <summary>
        /// Parses one easing function. Returns false for text that is not one, which the caller treats as the
        /// property's initial value: the CSS-OM has already rejected a declaration whose value is invalid, so this
        /// only fails for text that is not an easing function at all.
        /// </summary>
        public static bool TryParse(string text, out EasingFunction easing)
        {
            text = text.Trim();
            easing = Ease;

            switch (text.ToLowerInvariant())
            {
                case "linear": easing = Linear; return true;
                case "ease": easing = Ease; return true;
                case "ease-in": easing = Bezier(0.42, 0, 1, 1); return true;
                case "ease-out": easing = Bezier(0, 0, 0.58, 1); return true;
                case "ease-in-out": easing = Bezier(0.42, 0, 0.58, 1); return true;
                case "step-start": easing = new EasingFunction(Kind.Steps, 0, 0, 1, 1, 1, Jump.Start); return true;
                case "step-end": easing = new EasingFunction(Kind.Steps, 0, 0, 1, 1, 1, Jump.End); return true;
            }

            var open = text.IndexOf('(');
            if (open < 0 || !text.EndsWith(')')) return false;

            var name = text[..open].Trim().ToLowerInvariant();

            // linear(<stops>) (css-easing-2 §2.1): a piecewise-linear curve through its stops.
            if (name == "linear")
                return TryParseLinearStops(text[(open + 1)..^1], out easing);

            var args = text[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries);

            if (name == "cubic-bezier" && args.Length == 4
                && TryNumber(args[0], out var x1) && TryNumber(args[1], out var y1)
                && TryNumber(args[2], out var x2) && TryNumber(args[3], out var y2)
                && x1 >= 0 && x1 <= 1 && x2 >= 0 && x2 <= 1)   // css-easing-1 §2.1: outside 0 to 1 the curve is not a function of x
            {
                easing = Bezier(x1, y1, x2, y2);
                return true;
            }

            if (name == "steps" && args.Length is 1 or 2 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
            {
                var jump = args.Length == 2 ? args[1].ToLowerInvariant() switch
                {
                    "jump-start" or "start" => Jump.Start,
                    "jump-end" or "end" => Jump.End,
                    "jump-none" => Jump.None,
                    "jump-both" => Jump.Both,
                    _ => (Jump?)null
                } : Jump.End;

                // steps(1, jump-none) is the one combination css-easing-1 §2.2 rejects: it would divide by zero.
                if (jump is { } j && !(j == Jump.None && count < 2))
                {
                    easing = new EasingFunction(Kind.Steps, 0, 0, 1, 1, count, j);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// css-easing-2 §2.1.2: each stop is an output number with up to two input percentages (two make two points of the
        /// same output); the first input defaults to 0 and the last to the greater of 1 and the largest before it, inputs
        /// never decrease, and a run of stops without an input is spread evenly between its neighbours. The grammar
        /// (<see cref="LinearEasingGrammar"/>) is the one the CSS-OM validates with.
        /// </summary>
        private static bool TryParseLinearStops(string arguments, out EasingFunction easing)
        {
            easing = Linear;

            using var pooled = CssValueParser.GetCssTokensPooled(arguments);
            List<Token> tokens = pooled;
            if (LinearEasingGrammar.TryParse(tokens) is not { } stops) return false;

            var inputs = new List<double?>();
            var outputs = new List<double>();
            var largest = double.NegativeInfinity;

            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];

                if (stop.FirstPosition is not { } start)
                {
                    outputs.Add(stop.Output);
                    if (i == 0) { inputs.Add(0); largest = 0; }
                    else if (i == stops.Count - 1) inputs.Add(Math.Max(1, largest));
                    else inputs.Add(null);
                    continue;
                }

                largest = Math.Max(start, largest);
                inputs.Add(largest);
                outputs.Add(stop.Output);

                if (stop.SecondPosition is { } end)
                {
                    largest = Math.Max(end, largest);
                    inputs.Add(largest);
                    outputs.Add(stop.Output);
                }
            }

            // A run of points with no input takes evenly spaced values between the points on either side of it.
            var resolved = new double[inputs.Count];
            for (var i = 0; i < inputs.Count; i++)
            {
                if (inputs[i] is { } known) { resolved[i] = known; continue; }

                var runStart = i - 1;
                var runEnd = i;
                while (inputs[runEnd] is null) runEnd++;

                var from = resolved[runStart];
                var to = inputs[runEnd]!.Value;
                for (var k = i; k < runEnd; k++)
                    resolved[k] = from + (to - from) * (k - runStart) / (runEnd - runStart);
                i = runEnd - 1;
            }

            easing = new EasingFunction(Kind.Points, 0, 0, 1, 1, 0, Jump.End, resolved, outputs.ToArray());
            return true;
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>Applies the function to an input progress. The result may leave 0 to 1 for a bezier whose y values do.</summary>
        public double Evaluate(double progress)
        {
            switch (_kind)
            {
                case Kind.CubicBezier:
                    return EvaluateBezier(progress);
                case Kind.Steps:
                    return EvaluateSteps(progress);
                case Kind.Points:
                    return EvaluatePoints(progress);
                default:
                    return progress;
            }
        }

        private double EvaluateBezier(double x)
        {
            // The curve runs (0,0) -> (1,1). A keyframe interval is only ever evaluated inside 0 to 1, so the
            // end-tangent extension css-easing-1 §2.1 defines for inputs outside it is not needed.
            if (x <= 0) return 0;
            if (x >= 1) return 1;

            // Solve x(t) = x for t by bisection - the curve is monotonic in x because x1/x2 are within [0,1] -
            // then read y(t). 40 halvings are far below the precision a colour or length can show.
            double lo = 0, hi = 1, t = x;
            for (var i = 0; i < 40; i++)
            {
                var sample = Cubic(t, _x1, _x2);
                if (Math.Abs(sample - x) < 1e-9) break;
                if (sample < x) lo = t; else hi = t;
                t = (lo + hi) / 2;
            }

            return Cubic(t, _y1, _y2);
        }

        /// <summary>One coordinate of the bezier with end points 0 and 1 and control values <paramref name="c1"/>/<paramref name="c2"/>.</summary>
        private static double Cubic(double t, double c1, double c2)
        {
            var u = 1 - t;
            return 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t;
        }

        /// <summary>css-easing-2 §2.1.4: read the segment starting at the last point at or before the input, extending the
        /// nearest two points' line outside the range.</summary>
        private double EvaluatePoints(double progress)
        {
            var inputs = _pointInputs!;
            var outputs = _pointOutputs!;

            var a = 0;
            for (var i = 0; i < inputs.Length && inputs[i] <= progress; i++)
                a = i;

            if (a == inputs.Length - 1) a--;

            // At the last point's own input (a is stepped back to its segment above) the curve is that output exactly; the
            // formula below can be a rounding off it. Inside the list the formula already gives a point's output exactly.
            if (progress == inputs[a + 1]) return outputs[a + 1];

            var range = inputs[a + 1] - inputs[a];
            if (range == 0) return outputs[a + 1];

            // Stops are finite, but their span can still overflow (1e308 against -1e308): fall back to the segment's end.
            var value = outputs[a] + (progress - inputs[a]) / range * (outputs[a + 1] - outputs[a]);
            return double.IsFinite(value) ? value : outputs[a + 1];
        }

        private double EvaluateSteps(double progress)
        {
            var currentStep = Math.Floor(progress * _stepCount);
            if (_jump is Jump.Start or Jump.Both) currentStep++;

            if (progress >= 0 && currentStep < 0) currentStep = 0;

            var jumps = _jump switch
            {
                Jump.None => _stepCount - 1,
                Jump.Both => _stepCount + 1,
                _ => _stepCount
            };

            if (progress <= 1 && currentStep > jumps) currentStep = jumps;

            return currentStep / jumps;
        }
    }
}
