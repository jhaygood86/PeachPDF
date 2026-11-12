using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Implements CSS calc()'s type-checking rules over a parsed <see cref="CalcNode"/> tree (Layer A
    /// only — Layer B's evaluator trusts that a value it's asked to evaluate already passed this check,
    /// see CalcValueConverter/CalcEvaluator). Also folds pure-Number subtrees to concrete values, which
    /// is what makes a constant divide-by-zero divisor detectable here rather than deferred to layout.
    /// </summary>
    internal static class CalcTypeChecker
    {
        public static CalcCategory? Check(CalcNode node)
        {
            switch (node)
            {
                case NumberCalcNode:
                    return CalcCategory.Number;

                case DimensionCalcNode:
                    return CalcCategory.Length;

                case PercentageCalcNode:
                    return CalcCategory.Percentage;

                case AngleCalcNode:
                    return CalcCategory.Angle;

                case TimeCalcNode:
                    return CalcCategory.Time;

                case ResolutionCalcNode:
                    return CalcCategory.Resolution;

                case UnaryCalcNode unary:
                    return Check(unary.Operand);

                case BinaryCalcNode { Operator: '+' or '-' } binary:
                {
                    var left = Check(binary.Left);
                    var right = Check(binary.Right);
                    return Combine(left, right);
                }

                case BinaryCalcNode { Operator: '*' } binary:
                {
                    var left = Check(binary.Left);
                    var right = Check(binary.Right);
                    if (left is null || right is null) return null;
                    if (left == CalcCategory.Number) return right;
                    if (right == CalcCategory.Number) return left;
                    return null;
                }

                case BinaryCalcNode { Operator: '/' } binary:
                {
                    var left = Check(binary.Left);
                    var right = Check(binary.Right);
                    if (left is null || right != CalcCategory.Number) return null;

                    // Every valid Number-category subtree is built entirely from Number leaves, so it's
                    // always foldable here — this is what lets a constant divide-by-zero be rejected at
                    // validation time rather than silently producing an infinity/NaN at layout time.
                    var divisor = FoldNumber(binary.Right);
                    return divisor is null or 0d ? null : left;
                }

                case CallCalcNode call:
                    return CheckCall(call);

                default:
                    return null;
            }
        }

        private static CalcCategory? CheckCall(CallCalcNode call)
        {
            CalcCategory? combined = null;
            foreach (var argument in call.Arguments)
            {
                var category = Check(argument);
                if (category is null) return null;
                combined = combined is null ? category : Combine(combined, category);
                if (combined is null) return null;
            }

            if (combined is null) return null;

            // Lengths and percentages mix; an angle, time or resolution only stands with its own kind.
            var value = combined.Value;
            if (value != CalcCategory.Number && (value & ~CalcCategory.LengthPercentage) != 0
                && value is not (CalcCategory.Angle or CalcCategory.Time or CalcCategory.Resolution))
                return null;

            switch (call.Name)
            {
                // round(A) and round(A, B) with B omitted are only defined for a Number A.
                case FunctionNames.Round when call.Arguments.Count == 1 && combined != CalcCategory.Number:
                    return null;

                // The Number-only functions take (and give) a plain number.
                case FunctionNames.Pow or FunctionNames.Sqrt or FunctionNames.Log or FunctionNames.Exp:
                    return combined == CalcCategory.Number ? CalcCategory.Number : null;

                case FunctionNames.Sign:
                    return CalcCategory.Number;

                // sin()/cos()/tan() take a number (radians) or an angle.
                case FunctionNames.Sin or FunctionNames.Cos or FunctionNames.Tan:
                    return combined is CalcCategory.Number or CalcCategory.Angle ? CalcCategory.Number : null;

                case FunctionNames.Asin or FunctionNames.Acos or FunctionNames.Atan:
                    return combined == CalcCategory.Number ? CalcCategory.Angle : null;

                // atan2() takes two values of one type (numbers, lengths, percentages...).
                case FunctionNames.Atan2:
                    return CalcCategory.Angle;

                default:
                    return combined;
            }
        }

        /// <summary>Folds a pure-Number subtree to a concrete value; null if it isn't purely numeric.</summary>
        public static double? FoldNumber(CalcNode node) => node switch
        {
            NumberCalcNode number => number.Value,
            UnaryCalcNode unary => FoldUnaryNumber(unary),
            BinaryCalcNode binary => FoldBinaryNumber(binary),
            CallCalcNode call => FoldCallNumber(call),
            // Dimension/Percentage leaves are never purely numeric.
            _ => null,
        };

        private static double? FoldUnaryNumber(UnaryCalcNode unary)
        {
            var value = FoldNumber(unary.Operand);
            return value is null ? null : unary.Negative ? -value.Value : value.Value;
        }

        private static double? FoldBinaryNumber(BinaryCalcNode binary)
        {
            var left = FoldNumber(binary.Left);
            var right = FoldNumber(binary.Right);
            if (left is null || right is null) return null;

            return binary.Operator switch
            {
                '+' => left.Value + right.Value,
                '-' => left.Value - right.Value,
                '*' => left.Value * right.Value,
                '/' => right.Value != 0d ? left.Value / right.Value : null,
                _ => null
            };
        }

        private static double? FoldCallNumber(CallCalcNode call)
        {
            // Only functions that can yield a Number: the same-type ones over Number arguments, the
            // Number-only ones, and sin/cos/tan/sign, which also accept an angle (folded to radians).
            var takesAnyType = call.Name is FunctionNames.Sign or FunctionNames.Sin or FunctionNames.Cos or FunctionNames.Tan;
            if (call.Name is FunctionNames.Asin or FunctionNames.Acos or FunctionNames.Atan or FunctionNames.Atan2) return null;

            var values = new List<double>(call.Arguments.Count);
            foreach (var argument in call.Arguments)
            {
                var value = FoldNumber(argument) ?? (takesAnyType ? CalcSerializer.FoldAngle(argument) : null);
                if (value is null) return null;
                values.Add(value.Value);
            }

            return CalcMath.Apply(call.Name, call.Option, values);
        }

        private static CalcCategory? Combine(CalcCategory? left, CalcCategory? right)
        {
            if (left is null || right is null) return null;
            if (left == CalcCategory.Number && right == CalcCategory.Number) return CalcCategory.Number;
            if (left.Value.HasFlag(CalcCategory.Number) || right.Value.HasFlag(CalcCategory.Number)) return null;
            return left.Value | right.Value;
        }
    }
}
