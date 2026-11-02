using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PeachBrackets.MathML
{
    /// <summary>
    /// Says a MathML formula in English, for someone who hears it rather than sees it: <c>x² + 1</c> is "x squared plus 1". It reads the
    /// structure the author wrote (fractions, roots, scripts, sums, tables, fences) and not how it is drawn, and it is deliberately plain - a
    /// description to be read out, not a full speech-rule engine. An author who has a better one says it with <c>alttext</c>, an
    /// <c>aria-label</c> or a <c>text/plain</c> annotation, and those win.
    /// </summary>
    internal static partial class MathSpeech
    {
        /// <summary>Says a formula.</summary>
        /// <param name="math">the <c>math</c> element, or any element of a formula</param>
        /// <returns>the words, white space collapsed</returns>
        public static string Speak(IMathSourceNode math) => Collapse(Say(math));

        private static string Collapse(string text) => WhiteSpace().Replace(text, " ").Trim();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpace();

        private static string Join(IEnumerable<string> parts) => string.Join(' ', parts.Where(p => p.Length > 0));

        private static List<IMathSourceNode> Kids(IMathSourceNode node) => [.. node.Children];

        private static string SayAll(IEnumerable<IMathSourceNode> nodes) => Join(nodes.Select(Say));

        // A token or a row of one: said without needing words around it to make clear where it ends.
        private static bool IsSimple(IMathSourceNode node)
        {
            var name = node.Name.ToLowerInvariant();
            if (name is "mi" or "mn" or "mtext" or "ms")
                return true;

            var kids = Kids(node);
            return name is "mrow" or "mstyle" && kids.Count == 1 && IsSimple(kids[0]);
        }

        private static string Say(IMathSourceNode node)
        {
            var kids = Kids(node);
            switch (node.Name.ToLowerInvariant())
            {
                case "mi":
                    return Identifier(node.GetTextContent().Trim());
                case "mn" or "mtext":
                    return node.GetTextContent().Trim();
                case "ms":
                    return node.GetTextContent().Trim();
                case "mo":
                    return Operator(node.GetTextContent().Trim());
                case "mspace" or "mphantom" or "none" or "mprescripts" or "annotation" or "annotation-xml":
                    return string.Empty;

                case "semantics":
                    {
                        // The author's own plain-text reading, if there is one, is better than ours.
                        var plain = kids.FirstOrDefault(k => k.Name.Equals("annotation", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(k.GetAttribute("encoding"), "text/plain", StringComparison.OrdinalIgnoreCase));
                        if (plain is not null && plain.GetTextContent().Trim() is { Length: > 0 } text)
                            return text;

                        return kids.Count > 0 ? Say(kids[0]) : string.Empty;
                    }

                case "maction":
                    return kids.Count > 0 ? Say(kids[0]) : string.Empty;

                case "mfenced":
                    return Fenced(node, kids);

                case "mfrac" when kids.Count == 2:
                    return IsSimple(kids[0]) && IsSimple(kids[1])
                        ? $"{Say(kids[0])} over {Say(kids[1])}"
                        : $"the fraction with numerator {Say(kids[0])} and denominator {Say(kids[1])}";

                case "msqrt":
                    return kids.Count == 1 && IsSimple(kids[0])
                        ? $"the square root of {Say(kids[0])}"
                        : $"the square root of {SayAll(kids)}, end root";

                case "mroot" when kids.Count == 2:
                    {
                        var index = Say(kids[1]);
                        var kind = index switch { "2" => "square", "3" => "cube", _ => $"{index}th" };
                        return IsSimple(kids[0]) ? $"the {kind} root of {Say(kids[0])}" : $"the {kind} root of {Say(kids[0])}, end root";
                    }

                case "msup" when kids.Count == 2:
                    return Superscript(Say(kids[0]), kids[1]);

                case "msub" when kids.Count == 2:
                    return $"{Say(kids[0])} sub {Say(kids[1])}";

                case "msubsup" when kids.Count == 3:
                    return Superscript($"{Say(kids[0])} sub {Say(kids[1])}", kids[2]);

                case "munder" when kids.Count == 2:
                    return IsBigOperator(kids[0]) ? $"{Say(kids[0])} from {Say(kids[1])}" : $"{Say(kids[0])} under {Say(kids[1])}";

                case "mover" when kids.Count == 2:
                    return IsBigOperator(kids[0]) ? $"{Say(kids[0])} to {Say(kids[1])}" : $"{Say(kids[0])} with {Say(kids[1])} over it";

                case "munderover" when kids.Count == 3:
                    return IsBigOperator(kids[0])
                        ? $"{Say(kids[0])} from {Say(kids[1])} to {Say(kids[2])}"
                        : $"{Say(kids[0])} with {Say(kids[1])} under and {Say(kids[2])} over";

                case "mmultiscripts" when kids.Count > 0:
                    return $"{Say(kids[0])} with scripts {SayAll(kids.Skip(1))}";

                case "mtable":
                    return Table(kids);

                case "mtr" or "mlabeledtr":
                    return Join(kids.Select(Say));

                case "mtd":
                    return SayAll(kids);

                default:
                    // mrow, mstyle, mpadded, menclose, merror, math and anything unknown: its content in order.
                    return kids.Count > 0 ? SayAll(kids) : node.GetTextContent().Trim();
            }
        }

        private static string Superscript(string baseText, IMathSourceNode exponent)
        {
            var text = Say(exponent);
            return text switch
            {
                "2" => $"{baseText} squared",
                "3" => $"{baseText} cubed",
                _ => IsSimple(exponent) ? $"{baseText} to the {text} power" : $"{baseText} to the power {text}, end power",
            };
        }

        private static bool IsBigOperator(IMathSourceNode node) =>
            node.Name.Equals("mo", StringComparison.OrdinalIgnoreCase) && node.GetTextContent().Trim() is "∑" or "∏" or "∫" or "∬" or "∭" or "∮" or "⋃" or "⋂" or "lim";

        private static string Fenced(IMathSourceNode node, List<IMathSourceNode> kids)
        {
            var open = node.GetAttribute("open") ?? "(";
            var close = node.GetAttribute("close") ?? ")";
            var separator = (node.GetAttribute("separators") ?? ",").Trim();
            var say = kids.Select(Say).Where(s => s.Length > 0);
            return Join([Operator(open), string.Join(separator.Length > 0 ? $" {Operator(separator[0].ToString())} " : " ", say), Operator(close)]);
        }

        private static string Table(List<IMathSourceNode> rows)
        {
            var parts = new List<string>();
            var counted = rows.Where(r => r.Name.Equals("mtr", StringComparison.OrdinalIgnoreCase) || r.Name.Equals("mlabeledtr", StringComparison.OrdinalIgnoreCase)).ToList();
            var columns = counted.Count == 0 ? 0 : counted.Max(r => Kids(r).Count(c => c.Name.Equals("mtd", StringComparison.OrdinalIgnoreCase)));
            parts.Add($"table with {counted.Count} {(counted.Count == 1 ? "row" : "rows")} and {columns} {(columns == 1 ? "column" : "columns")}");
            for (var i = 0; i < counted.Count; i++)
                parts.Add($"row {i + 1}: " + string.Join(", ", Kids(counted[i]).Select(Say).Where(s => s.Length > 0)));

            return string.Join("; ", parts);
        }

        private static string Identifier(string text) => text switch
        {
            "α" => "alpha", "β" => "beta", "γ" => "gamma", "δ" => "delta", "ε" => "epsilon", "ζ" => "zeta", "η" => "eta", "θ" => "theta",
            "λ" => "lambda", "μ" => "mu", "ν" => "nu", "ξ" => "xi", "π" => "pi", "ρ" => "rho", "σ" => "sigma", "τ" => "tau", "φ" => "phi",
            "χ" => "chi", "ψ" => "psi", "ω" => "omega", "Γ" => "capital gamma", "Δ" => "capital delta", "Θ" => "capital theta",
            "Λ" => "capital lambda", "Σ" => "capital sigma", "Φ" => "capital phi", "Ψ" => "capital psi", "Ω" => "capital omega",
            "∞" => "infinity", "ℝ" => "the real numbers", "ℕ" => "the natural numbers", "ℤ" => "the integers", "ℚ" => "the rational numbers",
            "ℂ" => "the complex numbers", "ℯ" or "ⅇ" => "e", "ⅈ" => "i",
            _ => text,
        };

        private static string Operator(string text) => text switch
        {
            "+" => "plus", "-" or "−" or "–" => "minus", "*" or "×" or "·" or "⋅" or "∗" => "times", "/" or "÷" => "divided by",
            "=" => "equals", "≠" => "is not equal to", "<" => "is less than", ">" => "is greater than",
            "≤" or "⩽" => "is less than or equal to", "≥" or "⩾" => "is greater than or equal to", "≈" => "is approximately equal to",
            "≡" => "is identical to", "∝" => "is proportional to", "±" => "plus or minus", "∓" => "minus or plus",
            "∑" => "sum", "∏" => "product", "∫" => "integral", "∬" => "double integral", "∭" => "triple integral", "∮" => "contour integral",
            "∂" => "partial", "∇" => "nabla", "√" => "square root", "∞" => "infinity", "∆" => "delta",
            "(" => "left parenthesis", ")" => "right parenthesis", "[" => "left bracket", "]" => "right bracket", "{" => "left brace", "}" => "right brace",
            "|" => "vertical bar", "‖" => "double vertical bar", "⟨" => "left angle bracket", "⟩" => "right angle bracket",
            "," => "comma", ";" => "semicolon", ":" => "colon", "!" => "factorial", "%" => "percent", "°" => "degrees", "′" => "prime", "″" => "double prime",
            "∈" => "is an element of", "∉" => "is not an element of", "⊂" => "is a subset of", "⊆" => "is a subset of or equal to", "∪" => "union", "∩" => "intersection",
            "∀" => "for all", "∃" => "there exists", "∅" => "the empty set", "⇒" or "→" => "implies", "⇔" or "↔" => "if and only if",
            "…" or "⋯" or "⋮" or "⋱" => "dot dot dot", "⁡" or "⁢" or "⁣" or "⁤" => string.Empty,
            _ => text,
        };
    }
}
