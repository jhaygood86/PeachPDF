using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// A custom property registered with an <c>@property</c> at-rule (CSS Properties &amp; Values API Level 1).
    /// Captures the three descriptors (<c>syntax</c>, <c>initial-value</c>, <c>inherits</c>) and validates a
    /// candidate value against the registered <c>syntax</c> at computed-value time.
    /// </summary>
    internal sealed class RegisteredProperty
    {
        /// <summary>The registered custom property name (a dashed-ident, e.g. <c>--my-color</c>).</summary>
        public string Name { get; }

        /// <summary>The normalized <c>syntax</c> string (surrounding quotes stripped), e.g. <c>&lt;color&gt;</c> or <c>*</c>.</summary>
        public string Syntax { get; }

        /// <summary>The raw <c>initial-value</c>, or <c>null</c> when the rule declares none (only valid for <c>*</c>).</summary>
        public string? InitialValue { get; }

        /// <summary>Whether the property inherits (the <c>inherits</c> descriptor; defaults to <c>false</c> per spec).</summary>
        public bool Inherits { get; }

        private RegisteredProperty(string name, string syntax, string? initialValue, bool inherits)
        {
            Name = name;
            Syntax = syntax;
            InitialValue = initialValue;
            Inherits = inherits;
        }

        /// <summary>
        /// Builds a <see cref="RegisteredProperty"/> from a parsed <c>@property</c> rule, or returns null if the
        /// rule is invalid and must be ignored (CSS Properties &amp; Values API §3): a missing/empty <c>syntax</c>,
        /// or a non-universal syntax with a missing or syntax-mismatched <c>initial-value</c>.
        /// </summary>
        public static RegisteredProperty? FromRule(IPropertyRule rule, CssValueParser valueParser)
        {
            if (rule.Name is null || !rule.Name.StartsWith("--", StringComparison.Ordinal)) return null;

            var syntax = StripQuotes(rule.Syntax);
            if (string.IsNullOrWhiteSpace(syntax)) return null;

            // `inherits` is a required descriptor (CSS Properties & Values API §3): a rule missing it, or
            // carrying anything but true/false, is invalid and ignored — matching real UAs.
            var inheritsRaw = rule.Inherits?.Trim();
            bool inherits;
            if (string.Equals(inheritsRaw, "true", StringComparison.OrdinalIgnoreCase)) inherits = true;
            else if (string.Equals(inheritsRaw, "false", StringComparison.OrdinalIgnoreCase)) inherits = false;
            else return null;

            var rawInitial = rule.InitialValue;
            var hasInitial = !string.IsNullOrWhiteSpace(rawInitial);

            // The initial-value must be computationally independent (§3): a var() reference is not allowed
            // (its value depends on other custom properties), so an initial-value containing var() invalidates
            // the whole rule. This also closes an infinite-recursion path — the resolver's initial-value
            // fallback would otherwise re-enter var() resolution for a self-referential initial-value.
            if (hasInitial && rawInitial!.Contains("var(", StringComparison.OrdinalIgnoreCase)) return null;

            if (syntax == "*")
                // The universal syntax accepts any value and may omit initial-value.
                return new RegisteredProperty(rule.Name, syntax, hasInitial ? rawInitial!.Trim() : null, inherits);

            // A typed syntax REQUIRES an initial-value that matches it, else the whole rule is invalid.
            if (!hasInitial) return null;
            var initial = rawInitial!.Trim();
            if (!SyntaxMatches(syntax, initial, valueParser)) return null;

            return new RegisteredProperty(rule.Name, syntax, initial, inherits);
        }

        /// <summary>
        /// Whether <paramref name="value"/> is accepted by this property's registered <c>syntax</c>.
        /// The universal syntax accepts anything; a typed syntax accepts a value matching any of its
        /// <c>|</c>-separated components.
        /// </summary>
        public bool Accepts(string value, CssValueParser valueParser) => SyntaxMatches(Syntax, value, valueParser);

        private static bool SyntaxMatches(string syntax, string value, CssValueParser valueParser)
        {
            if (syntax == "*") return true;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // syntax is a set of components separated by top-level '|' (alternation) — match any one.
            foreach (var rawComponent in syntax.Split('|'))
            {
                var component = rawComponent.Trim();
                if (component.Length == 0) continue;
                if (ComponentMatches(component, value.Trim(), valueParser)) return true;
            }

            return false;
        }

        private static bool ComponentMatches(string component, string value, CssValueParser valueParser)
        {
            // List multipliers: '<type>+' (space-separated) / '<type>#' (comma-separated). Validate each item
            // against the single-type base. (A base with no multiplier validates the whole value as one item.)
            if (component.EndsWith("+", StringComparison.Ordinal))
                return SplitList(value, ' ').All(item => BaseTypeMatches(component[..^1].Trim(), item, valueParser));
            if (component.EndsWith("#", StringComparison.Ordinal))
                return SplitList(value, ',').All(item => BaseTypeMatches(component[..^1].Trim(), item, valueParser));

            return BaseTypeMatches(component, value, valueParser);
        }

        /// <summary>
        /// Splits a value on a top-level separator only — a separator inside a function's parentheses or a
        /// quoted string does not split (so <c>rgb(1, 2, 3), rgb(4, 5, 6)</c> splits into two colors, not
        /// pieces of each <c>rgb()</c>).
        /// </summary>
        private static string[] SplitList(string value, char separator)
        {
            var items = new List<string>();
            var depth = 0;
            var start = 0;
            char quote = '\0';
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c is '"' or '\'') quote = c;
                else if (c == '(') depth++;
                else if (c == ')') { if (depth > 0) depth--; }
                else if (c == separator && depth == 0)
                {
                    items.Add(value[start..i].Trim());
                    start = i + 1;
                }
            }
            items.Add(value[start..].Trim());
            return items.Where(item => item.Length > 0).ToArray();
        }

        private static bool BaseTypeMatches(string type, string value, CssValueParser valueParser)
        {
            if (value.Length == 0) return false;

            // A calc()-family expression is valid wherever a numeric data type is (length/number/percentage/…).
            // Note: a calc() with font/viewport-relative units (e.g. calc(1em + 2px)) is accepted here even as
            // an initial-value, where the spec requires computational independence (§3) — see issue #212.
            static bool NumericOk(string v, Func<string, bool> check) => CssValueParser.IsCalcFunction(v) || check(v);

            switch (type)
            {
                case "<length>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToLength() != null);
                case "<percentage>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToPercent() != null);
                case "<length-percentage>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToDistance() != null);
                case "<number>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToSingle() != null);
                case "<integer>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToInteger() != null);
                case "<angle>":
                    return NumericOk(value, v => CssValueParser.GetCssTokens(v).ToAngle() != null);
                case "<color>":
                    return valueParser.IsColorValid(value);
                case "<custom-ident>":
                    return IsIdent(value);
                case "<string>":
                    return value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0];
                default:
                    // A bare ident literal in the syntax (e.g. `auto`) is a <custom-ident> and matches that
                    // keyword case-sensitively (CSS Syntax 3 — idents are case-sensitive). Any data type we
                    // don't model (<image>, <url>, <time>, <resolution>, <transform-*>) degrades to "accept"
                    // rather than wrongly reject (documented accepted gap, issue #212).
                    if (type.StartsWith("<", StringComparison.Ordinal)) return true;
                    return string.Equals(type, value, StringComparison.Ordinal);
            }
        }

        private static bool IsIdent(string value)
        {
            if (value.Length == 0) return false;
            // A CSS identifier may not start with a digit, nor with a hyphen immediately followed by a digit
            // (CSS Syntax 3 §4.3.11), so e.g. "10px" is a dimension, not a <custom-ident>.
            if (char.IsDigit(value[0])) return false;
            if (value[0] == '-' && value.Length > 1 && char.IsDigit(value[1])) return false;

            foreach (var c in value)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
            return true;
        }

        private static string StripQuotes(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Trim();
            if (s.Length >= 2 && s[0] is '"' or '\'' && s[^1] == s[0])
                s = s[1..^1].Trim();
            return s;
        }
    }
}
