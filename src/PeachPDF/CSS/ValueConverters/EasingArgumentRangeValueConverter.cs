#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Applies the argument ranges css-easing-1 §2 puts on <c>steps()</c> and <c>cubic-bezier()</c>, which the plain
    /// number grammars cannot express: the step count is at least 1 (at least 2 with <c>jump-none</c>, which would
    /// otherwise divide by zero), and a bezier's x control values lie in 0 to 1 (outside it the curve is not a function of
    /// x). A declaration that breaks one is invalid, so an earlier valid easing still applies and the renderer never meets
    /// a curve it has to guess at.
    /// </summary>
    internal sealed class EasingArgumentRangeValueConverter : IValueConverter
    {
        private readonly IValueConverter _inner;

        public EasingArgumentRangeValueConverter(IValueConverter inner)
        {
            _inner = inner;
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var result = _inner.Convert(value);
            if (result == null) return null;

            var function = value.OnlyOrDefault();
            if (function is not { Type: TokenType.Function } f) return result;

            var name = f.Data.ToString();
            var args = Arguments(f.ArgumentTokens);

            if (name.Equals(FunctionNames.Steps, StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count == 0 || !TryNumber(args[0], out var count) || count < 1) return null;
                if (args.Count > 1 && args[1].Equals(Keywords.JumpNone, StringComparison.OrdinalIgnoreCase) && count < 2) return null;
            }
            else if (name.Equals(FunctionNames.CubicBezier, StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count != 4) return null;
                if (!TryNumber(args[0], out var x1) || x1 < 0 || x1 > 1) return null;
                if (!TryNumber(args[2], out var x2) || x2 < 0 || x2 > 1) return null;
            }

            return result;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return _inner.Construct(properties);
        }

        private static bool TryNumber(string text, out double number) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        /// <summary>The comma-separated arguments of a function, each as the text of its tokens without whitespace.</summary>
        private static List<string> Arguments(IEnumerable<Token> tokens)
        {
            var arguments = new List<string>();
            var current = new StringBuilder();

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Comma)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }
                else if (token.Type != TokenType.Whitespace)
                {
                    current.Append(token.Data.ToString());
                }
            }

            arguments.Add(current.ToString());
            return arguments;
        }
    }
}
