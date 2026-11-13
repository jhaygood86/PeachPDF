using System;
using System.Text.RegularExpressions;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// <c>calc-size(&lt;basis&gt;, &lt;calc-sum&gt;)</c> (CSS Values 5 §10.8.1), and the bare <c>min-content</c>/<c>max-content</c>/<c>fit-content</c> sizes (which are the same size): a size computed from another size, where the keyword
    /// <c>size</c> inside the expression stands for the basis. Layout turns it into an ordinary <c>calc()</c> by
    /// measuring the basis (see <c>CssBox.CalcSizing</c>), so everything downstream sees a plain length.
    /// </summary>
    internal static partial class CalcSizeValue
    {
        /// <summary>Splits a <c>calc-size()</c> value into its basis and its expression; false when the text is not one.</summary>
        public static bool TryParse(string? value, out string basis, out string expression)
        {
            basis = expression = string.Empty;
            if (value is null) return false;

            var text = value.AsSpan().Trim();

            // `max-content` and the other intrinsic keywords are the size itself: calc-size(<keyword>, size).
            if (text.Equals("min-content", StringComparison.OrdinalIgnoreCase) || text.Equals("max-content", StringComparison.OrdinalIgnoreCase)
                || text.Equals("fit-content", StringComparison.OrdinalIgnoreCase))
            {
                basis = text.ToString();
                expression = "size";
                return true;
            }

            const string open = "calc-size(";
            if (text.Length <= open.Length + 1 || !text.StartsWith(open, StringComparison.OrdinalIgnoreCase) || text[^1] != ')') return false;

            var inner = text[open.Length..^1].ToString();
            var depth = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                switch (inner[i])
                {
                    case '(': depth++; break;
                    case ')': depth--; break;
                    case ',' when depth == 0:
                        basis = inner[..i].Trim();
                        expression = inner[(i + 1)..].Trim();
                        return basis.Length > 0 && expression.Length > 0 && !expression.Contains(',', StringComparison.Ordinal);
                }
            }

            return false;
        }

        public static bool IsCalcSize(string? value) => TryParse(value, out _, out _);

        /// <summary>Whether the basis is an intrinsic-size keyword rather than a length, which has to be measured.</summary>
        public static bool IsKeywordBasis(string basis) =>
            basis.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || basis.Equals("min-content", StringComparison.OrdinalIgnoreCase)
            || basis.Equals("max-content", StringComparison.OrdinalIgnoreCase)
            || basis.Equals("fit-content", StringComparison.OrdinalIgnoreCase);

        /// <summary>The expression as a <c>calc()</c>, with <c>size</c> replaced by <paramref name="sizeText"/>.</summary>
        public static string Substitute(string expression, string sizeText) =>
            "calc(" + SizeKeyword().Replace(expression, "(" + sizeText + ")") + ")";

        [GeneratedRegex(@"(?<![\w-])size(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SizeKeyword();
    }
}
