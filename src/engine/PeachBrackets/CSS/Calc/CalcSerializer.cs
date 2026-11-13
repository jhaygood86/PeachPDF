using System.Collections.Generic;
using System.Globalization;

namespace PeachBrackets.Css
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
                // A Number built from a length (sign(10px - 4px)) has no pure-number fold; keep its text.
                return CalcTypeChecker.FoldNumber(node) is { } value
                    ? ((float)value).ToString(null, CultureInfo.InvariantCulture)
                    : SerializeExpression(node);
            }

            if (category == CalcCategory.Angle && FoldAngle(node) is { } radians)
            {
                // Unlike Length/Percentage, Angle never needs layout context to resolve - deg/grad/rad/turn
                // all convert via fixed constants - so a valid Angle expression folds fully here (an
                // atan2() of em-relative lengths is the one exception, kept as text below).
                var degrees = radians * (180.0 / System.Math.PI);
                return new Angle((float)degrees, Angle.Unit.Deg).ToString(null, CultureInfo.InvariantCulture);
            }

            if (TryFoldPixels(node, out var points))
            {
                // TryFoldPixels folds through Length.ToPixels, whose result is layout POINTS —
                // convert back to CSS px for the canonical "<N>px" text (calc(12px + 4px) → "16px"),
                // so re-parsing the folded text applies the spec px ratio exactly once, not twice.
                var folded = new Length((float)(points / Length.PointsPerPx), Length.Unit.Px).ToString(null, CultureInfo.InvariantCulture);
                // A negative result stays a calc(): as a bare length it would be a negative literal, which a property with a [0,inf] range drops as
                // invalid, where a calc() is valid and clamps to the range when it is computed (css-values-4 section 10.2).
                return points < 0 ? "calc(" + folded + ")" : folded;
            }

            return SerializeExpression(node);
        }

        /// <summary>Folds an Angle-category subtree to radians; always succeeds for a validated tree.</summary>
        internal static double? FoldAngle(CalcNode node) => node switch
        {
            AngleCalcNode angle => new Angle((float)angle.Value, angle.Unit).ToRadian(),
            UnaryCalcNode unary => FoldUnaryAngle(unary),
            BinaryCalcNode { Operator: '+' or '-' } binary => FoldAdditiveAngle(binary),
            BinaryCalcNode { Operator: '*' } binary => FoldMultiplicativeAngle(binary),
            BinaryCalcNode { Operator: '/' } binary => FoldDivisionAngle(binary),
            CallCalcNode call => FoldCallAngle(call),
            _ => null,
        };

        private static double? FoldUnaryAngle(UnaryCalcNode unary)
        {
            var value = FoldAngle(unary.Operand);
            return value is null ? null : unary.Negative ? -value.Value : value.Value;
        }

        private static double? FoldAdditiveAngle(BinaryCalcNode binary)
        {
            var left = FoldAngle(binary.Left);
            var right = FoldAngle(binary.Right);
            if (left is null || right is null) return null;
            return binary.Operator == '+' ? left.Value + right.Value : left.Value - right.Value;
        }

        private static double? FoldMultiplicativeAngle(BinaryCalcNode binary)
        {
            var leftAngle = FoldAngle(binary.Left);
            if (leftAngle is not null)
            {
                var scalar = CalcTypeChecker.FoldNumber(binary.Right);
                return scalar is null ? null : leftAngle.Value * scalar.Value;
            }

            var rightAngle = FoldAngle(binary.Right);
            if (rightAngle is not null)
            {
                var scalar = CalcTypeChecker.FoldNumber(binary.Left);
                return scalar is null ? null : rightAngle.Value * scalar.Value;
            }

            return null;
        }

        private static double? FoldDivisionAngle(BinaryCalcNode binary)
        {
            var left = FoldAngle(binary.Left);
            if (left is null) return null;
            var divisor = CalcTypeChecker.FoldNumber(binary.Right);
            return divisor is null or 0d ? null : left.Value / divisor.Value;
        }

        private static double? FoldCallAngle(CallCalcNode call)
        {
            // The functions that yield a Number never produce an angle.
            if (!CalcMath.IsSameTypeFunction(call.Name) && call.Name is not (FunctionNames.Asin or FunctionNames.Acos or FunctionNames.Atan or FunctionNames.Atan2))
                return null;

            var values = new List<double>(call.Arguments.Count);
            foreach (var argument in call.Arguments)
            {
                // The inverse trigonometric functions take numbers (atan2: numbers or lengths) and give an angle.
                var value = call.Name is FunctionNames.Asin or FunctionNames.Acos or FunctionNames.Atan or FunctionNames.Atan2
                    ? CalcTypeChecker.FoldNumber(argument) ?? (TryFoldPixels(argument, out var px) ? px : null)
                    : FoldAngle(argument);
                if (value is null) return null;
                values.Add(value.Value);
            }

            return CalcMath.Apply(call.Name, call.Option, values);
        }

        private static string SerializeExpression(CalcNode node)
        {
            if (node is CallCalcNode call)
            {
                var args = new List<string>(call.Arguments.Count);
                if (call.Option is not null) args.Add(call.Option);
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

                // Time/Resolution leaves are only produced by @property `<time>`/`<resolution>` validation,
                // which type-checks rather than serializes, so these arms are defensive completeness. A
                // Time/Resolution-category Serialize() (were one ever created) folds through neither the Number
                // nor Angle branch nor TryFoldPixels, so it reaches SerializeExpression and re-serializes to
                // normalized calc() text using these leaf renderings.
                case AngleCalcNode angle:
                    return new Angle((float)angle.Value, angle.Unit).ToString(null, CultureInfo.InvariantCulture);

                case TimeCalcNode time:
                    return new Time((float)time.Value, time.Unit).ToString(null, CultureInfo.InvariantCulture);

                case ResolutionCalcNode resolution:
                    return new Resolution((float)resolution.Value, resolution.Unit).ToString(null, CultureInfo.InvariantCulture);

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

                // Container-relative and viewport units (like em/rem/ex/ch) need layout context this
                // Layer-A-only fold has none of - a `50cqw`/`50vw` leaf folded here with no container/page
                // size in scope would permanently bake in 0 as the canonical CssText, losing the unit
                // before layout ever gets a chance to resolve it against a real container/page (see
                // CalcParser.IsSupportedLengthUnit, which accepts these units into calc() specifically so
                // layout's own CalcEvaluator can resolve them).
                case DimensionCalcNode dimension when dimension.Unit is not (Length.Unit.Em or Length.Unit.Rem or
                    Length.Unit.Ex or Length.Unit.Ch or
                    Length.Unit.Cap or Length.Unit.Ic or Length.Unit.Lh or
                    Length.Unit.Rex or Length.Unit.Rch or Length.Unit.Rcap or Length.Unit.Ric or Length.Unit.Rlh or
                    Length.Unit.Cqw or Length.Unit.Cqh or Length.Unit.Cqi or Length.Unit.Cqb or
                    Length.Unit.Cqmin or Length.Unit.Cqmax or
                    Length.Unit.Vw or Length.Unit.Vh or Length.Unit.Vi or Length.Unit.Vb or
                    Length.Unit.Vmin or Length.Unit.Vmax or
                    Length.Unit.Svw or Length.Unit.Svh or Length.Unit.Svi or Length.Unit.Svb or
                    Length.Unit.Svmin or Length.Unit.Svmax or
                    Length.Unit.Lvw or Length.Unit.Lvh or Length.Unit.Lvi or Length.Unit.Lvb or
                    Length.Unit.Lvmin or Length.Unit.Lvmax or
                    Length.Unit.Dvw or Length.Unit.Dvh or Length.Unit.Dvi or Length.Unit.Dvb or
                    Length.Unit.Dvmin or Length.Unit.Dvmax):
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
                    // A Number-valued call (sin(30deg), pow(2, 3)) folds on its own.
                    if (CalcTypeChecker.FoldNumber(call) is { } folded)
                    {
                        pixels = folded;
                        return true;
                    }

                    // The inverse trig functions give an angle, never a length.
                    if (call.Name is FunctionNames.Asin or FunctionNames.Acos or FunctionNames.Atan or FunctionNames.Atan2)
                    {
                        pixels = 0;
                        return false;
                    }

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

                    if (CalcMath.IsSameTypeFunction(call.Name) && CalcMath.Apply(call.Name, call.Option, values) is { } result)
                    {
                        pixels = result;
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
    }
}
