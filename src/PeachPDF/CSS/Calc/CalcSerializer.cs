using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Produces the canonical CSS text (<see cref="IPropertyValue.CssText"/>) for a validated calc-family
    /// AST (Layer A only). A fully-numeric expression folds to a plain number; an expression built
    /// entirely from absolute length units folds to a single pixel length; anything needing layout
    /// context (mixed em/rem/%) is re-serialized back to normalized calc()/min()/max()/clamp() text.
    /// </summary>
    internal static class CalcSerializer
    {
        public static string Serialize(CalcNode node, CalcCategory category)
        {
            if (category == CalcCategory.Number)
            {
                var value = CalcTypeChecker.FoldNumber(node) ?? 0d;
                return ((float)value).ToString(null, CultureInfo.InvariantCulture);
            }

            if (TryFoldPixels(node, out var pixels))
            {
                return new Length((float)pixels, Length.Unit.Px).ToString(null, CultureInfo.InvariantCulture);
            }

            return SerializeExpression(node);
        }

        private static string SerializeExpression(CalcNode node)
        {
            if (node is CallCalcNode call)
            {
                var args = new List<string>(call.Arguments.Count);
                foreach (var argument in call.Arguments) args.Add(SerializeOperand(argument, false, 0));
                return $"{call.Name}({string.Join(", ", args)})";
            }

            return $"calc({SerializeOperand(node, false, 0)})";
        }

        /// <summary>
        /// Serializes a node as an operand of a parent with the given precedence (0 = none, 1 = sum-level
        /// +/-, 2 = product-level */÷), adding parentheses whenever omitting them would change the
        /// expression's meaning on re-parse (lower-precedence child under a higher-precedence parent, or
        /// a same-precedence child on the right — since +/- and */÷ are left-associative and not, in
        /// general, safe to flatten on the right).
        /// </summary>
        private static string SerializeOperand(CalcNode node, bool isRightOperand, int parentPrecedence)
        {
            switch (node)
            {
                case NumberCalcNode number:
                    return ((float)number.Value).ToString(null, CultureInfo.InvariantCulture);

                case DimensionCalcNode dimension:
                    return new Length((float)dimension.Value, dimension.Unit).ToString(null, CultureInfo.InvariantCulture);

                case PercentageCalcNode percentage:
                    return new Length((float)percentage.Value, Length.Unit.Percent).ToString(null, CultureInfo.InvariantCulture);

                case UnaryCalcNode unary:
                {
                    var operand = SerializeOperand(unary.Operand, false, 3);
                    return unary.Negative ? $"-{operand}" : operand;
                }

                case CallCalcNode call:
                    return SerializeExpression(call);

                case BinaryCalcNode binary:
                {
                    var precedence = binary.Operator is '+' or '-' ? 1 : 2;
                    var text =
                        $"{SerializeOperand(binary.Left, false, precedence)} {binary.Operator} {SerializeOperand(binary.Right, true, precedence)}";
                    var needsParens = precedence < parentPrecedence || (precedence == parentPrecedence && isRightOperand);
                    return needsParens ? $"({text})" : text;
                }

                default:
                    return string.Empty;
            }
        }

        private static bool TryFoldPixels(CalcNode node, out double pixels)
        {
            switch (node)
            {
                case NumberCalcNode number:
                    pixels = number.Value;
                    return true;

                case DimensionCalcNode dimension when dimension.Unit is not (Length.Unit.Em or Length.Unit.Rem or Length.Unit.Ex):
                    pixels = new Length((float)dimension.Value, dimension.Unit).ToPixels(0, 0, 0);
                    return true;

                case UnaryCalcNode unary when TryFoldPixels(unary.Operand, out var operand):
                    pixels = unary.Negative ? -operand : operand;
                    return true;

                case BinaryCalcNode binary when TryFoldPixels(binary.Left, out var left) && TryFoldPixels(binary.Right, out var right):
                    switch (binary.Operator)
                    {
                        case '+':
                            pixels = left + right;
                            return true;
                        case '-':
                            pixels = left - right;
                            return true;
                        case '*':
                            pixels = left * right;
                            return true;
                        case '/' when right != 0d:
                            pixels = left / right;
                            return true;
                        default:
                            pixels = 0;
                            return false;
                    }

                case CallCalcNode call:
                {
                    var values = new List<double>(call.Arguments.Count);
                    foreach (var argument in call.Arguments)
                    {
                        if (!TryFoldPixels(argument, out var value))
                        {
                            pixels = 0;
                            return false;
                        }

                        values.Add(value);
                    }

                    if (call.Name.Isi(FunctionNames.Min))
                    {
                        pixels = Min(values);
                        return true;
                    }

                    if (call.Name.Isi(FunctionNames.Max))
                    {
                        pixels = Max(values);
                        return true;
                    }

                    if (call.Name.Isi(FunctionNames.Clamp) && values.Count == 3)
                    {
                        pixels = values[0] > values[2] ? values[2] : System.Math.Clamp(values[1], values[0], values[2]);
                        return true;
                    }

                    pixels = 0;
                    return false;
                }

                default:
                    pixels = 0;
                    return false;
            }
        }

        private static double Min(List<double> values)
        {
            var result = values[0];
            for (var i = 1; i < values.Count; i++) if (values[i] < result) result = values[i];
            return result;
        }

        private static double Max(List<double> values)
        {
            var result = values[0];
            for (var i = 1; i < values.Count; i++) if (values[i] > result) result = values[i];
            return result;
        }
    }
}
