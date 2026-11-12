using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The numeric semantics of every calc-family function (CSS Values 4 §10): the one place the
    /// folder, the type-checker and the layout-time evaluator share, so each function is defined once.
    /// Arguments arrive already reduced to doubles (lengths in layout units, angles in radians); a
    /// <c>null</c> result is an invalid computation (divide by zero, a NaN or infinite result).
    /// </summary>
    internal static class CalcMath
    {
        private static readonly (string Name, int Min, int Max)[] Arity =
        [
            (FunctionNames.Calc, 1, 1),
            (FunctionNames.Min, 1, int.MaxValue),
            (FunctionNames.Max, 1, int.MaxValue),
            (FunctionNames.Clamp, 3, 3),
            (FunctionNames.Round, 1, 2),
            (FunctionNames.Mod, 2, 2),
            (FunctionNames.Rem, 2, 2),
            (FunctionNames.Sin, 1, 1),
            (FunctionNames.Cos, 1, 1),
            (FunctionNames.Tan, 1, 1),
            (FunctionNames.Asin, 1, 1),
            (FunctionNames.Acos, 1, 1),
            (FunctionNames.Atan, 1, 1),
            (FunctionNames.Atan2, 2, 2),
            (FunctionNames.Pow, 2, 2),
            (FunctionNames.Sqrt, 1, 1),
            (FunctionNames.Hypot, 1, int.MaxValue),
            (FunctionNames.Log, 1, 2),
            (FunctionNames.Exp, 1, 1),
            (FunctionNames.Abs, 1, 1),
            (FunctionNames.Sign, 1, 1),
        ];

        /// <summary>The canonical lowercase name of a calc-family function, or <c>null</c>.</summary>
        public static string? Canonical(ReadOnlySpan<char> name)
        {
            foreach (var entry in Arity)
                if (name.Isi(entry.Name)) return entry.Name;
            return null;
        }

        public static bool AcceptsArgumentCount(string name, int count)
        {
            foreach (var entry in Arity)
                if (entry.Name == name) return count >= entry.Min && count <= entry.Max;
            return false;
        }

        /// <summary>Whether the keyword is a <c>round()</c> rounding strategy.</summary>
        public static bool IsRoundingStrategy(string keyword) =>
            keyword is "nearest" or "up" or "down" or "to-zero";

        /// <summary>
        /// Functions whose result has the same type as their arguments (so a length stays a length and an
        /// angle stays an angle); the rest produce a Number (or, for the inverse trig functions, an Angle).
        /// </summary>
        public static bool IsSameTypeFunction(string name) =>
            name is FunctionNames.Min or FunctionNames.Max or FunctionNames.Clamp or FunctionNames.Round or
                FunctionNames.Mod or FunctionNames.Rem or FunctionNames.Hypot or FunctionNames.Abs;

        public static double? Apply(string name, string? option, IReadOnlyList<double> v)
        {
            double? result = name switch
            {
                FunctionNames.Min => Reduce(v, Math.Min),
                FunctionNames.Max => Reduce(v, Math.Max),
                FunctionNames.Clamp when v.Count == 3 => v[0] > v[2] ? v[2] : Math.Clamp(v[1], v[0], v[2]),
                FunctionNames.Round => Round(option, v[0], v.Count > 1 ? v[1] : 1d),
                FunctionNames.Mod when v[1] != 0d => v[0] - (v[1] * Math.Floor(v[0] / v[1])),
                FunctionNames.Rem when v[1] != 0d => v[0] % v[1],
                FunctionNames.Sin => Math.Sin(v[0]),
                FunctionNames.Cos => Math.Cos(v[0]),
                FunctionNames.Tan => Math.Tan(v[0]),
                FunctionNames.Asin => Math.Asin(v[0]),
                FunctionNames.Acos => Math.Acos(v[0]),
                FunctionNames.Atan => Math.Atan(v[0]),
                FunctionNames.Atan2 => Math.Atan2(v[0], v[1]),
                FunctionNames.Pow => Math.Pow(v[0], v[1]),
                FunctionNames.Sqrt => Math.Sqrt(v[0]),
                FunctionNames.Hypot => Hypot(v),
                FunctionNames.Log => v.Count > 1 ? Math.Log(v[0]) / Math.Log(v[1]) : Math.Log(v[0]),
                FunctionNames.Exp => Math.Exp(v[0]),
                FunctionNames.Abs => Math.Abs(v[0]),
                FunctionNames.Sign => v[0] == 0d ? 0d : Math.Sign(v[0]),
                _ => null,
            };

            return result is { } r && double.IsFinite(r) ? r : null;
        }

        private static double Reduce(IReadOnlyList<double> v, Func<double, double, double> pick)
        {
            var result = v[0];
            for (var i = 1; i < v.Count; i++) result = pick(result, v[i]);
            return result;
        }

        private static double? Round(string? strategy, double a, double b)
        {
            b = Math.Abs(b);
            if (b == 0d) return null;
            var q = a / b;
            var rounded = strategy switch
            {
                "up" => Math.Ceiling(q),
                "down" => Math.Floor(q),
                "to-zero" => Math.Truncate(q),
                // A tie rounds toward +infinity (CSS Values 4 §10.3).
                _ => Math.Floor(q + 0.5),
            };
            return rounded * b;
        }

        private static double Hypot(IReadOnlyList<double> v)
        {
            double sum = 0;
            foreach (var x in v) sum += x * x;
            return Math.Sqrt(sum);
        }
    }
}
