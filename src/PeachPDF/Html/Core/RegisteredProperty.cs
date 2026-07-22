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

            // The syntax string itself must be well-formed (CSS Properties & Values API §3): every component
            // is a supported <data-type> or an ident literal. A syntax naming an unsupported/unknown type
            // (e.g. "<bogus>") is not a valid syntax string, so the whole @property rule is invalid and ignored.
            if (!IsValidSyntaxString(syntax)) return null;

            // A typed syntax REQUIRES an initial-value that matches it, else the whole rule is invalid.
            // The initial-value is additionally held to the §3 computational-independence rule
            // (forInitialValue: true), which rejects a calc() built from font/viewport-relative or
            // percentage terms — a distinction that does NOT apply to values matched later at
            // computed-value time (Accepts), where any resolvable calc() is fine.
            if (!hasInitial) return null;
            var initial = rawInitial!.Trim();
            if (!SyntaxMatches(syntax, initial, valueParser, forInitialValue: true)) return null;

            return new RegisteredProperty(rule.Name, syntax, initial, inherits);
        }

        /// <summary>
        /// Harvests every valid <c>@property</c> rule from <paramref name="cssData"/>'s stylesheets into a
        /// name-keyed registry (case-sensitive, per CSS custom-property naming). Later duplicate registrations
        /// of the same name win (cascade order); invalid rules (<see cref="FromRule"/> returns null) are dropped
        /// per spec. Shared by the HTML cascade (<c>DomParser.GenerateCssTree</c>) and the standalone-SVG loader
        /// so both build the registry the same way.
        /// </summary>
        public static Dictionary<string, RegisteredProperty> BuildRegistry(CssData cssData, CssValueParser valueParser)
        {
            var registry = new Dictionary<string, RegisteredProperty>(StringComparer.Ordinal);
            foreach (var propertyRule in cssData.Stylesheets.SelectMany(s => s.Rules.OfType<PropertyRule>()))
            {
                var registered = FromRule(propertyRule, valueParser);
                if (registered is not null)
                    registry[registered.Name] = registered;
            }
            return registry;
        }

        /// <summary>
        /// Whether <paramref name="value"/> is accepted by this property's registered <c>syntax</c>.
        /// The universal syntax accepts anything; a typed syntax accepts a value matching any of its
        /// <c>|</c>-separated components.
        /// </summary>
        public bool Accepts(string value, CssValueParser valueParser) =>
            SyntaxMatches(Syntax, value, valueParser, forInitialValue: false);

        private static bool SyntaxMatches(string syntax, string value, CssValueParser valueParser, bool forInitialValue)
        {
            if (syntax == "*") return true;
            if (string.IsNullOrWhiteSpace(value)) return false;

            // syntax is a set of components separated by top-level '|' (alternation) — match any one.
            foreach (var rawComponent in syntax.Split('|'))
            {
                var component = rawComponent.Trim();
                if (component.Length == 0) continue;
                if (ComponentMatches(component, value.Trim(), valueParser, forInitialValue)) return true;
            }

            return false;
        }

        private static bool ComponentMatches(string component, string value, CssValueParser valueParser, bool forInitialValue)
        {
            // List multipliers: '<type>+' (space-separated) / '<type>#' (comma-separated). Validate each item
            // against the single-type base. (A base with no multiplier validates the whole value as one item.)
            if (component.EndsWith("+", StringComparison.Ordinal))
                return SplitList(value, ' ').All(item => BaseTypeMatches(component[..^1].Trim(), item, valueParser, forInitialValue));
            if (component.EndsWith("#", StringComparison.Ordinal))
                return SplitList(value, ',').All(item => BaseTypeMatches(component[..^1].Trim(), item, valueParser, forInitialValue));

            return BaseTypeMatches(component, value, valueParser, forInitialValue);
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

        private static bool BaseTypeMatches(string type, string value, CssValueParser valueParser, bool forInitialValue)
        {
            if (value.Length == 0) return false;

            // A calc()-family expression is valid for a numeric data type when its resolved type-checking
            // category is within `allowed` (the same subset test CalcValueConverter uses), so e.g. calc(50%)
            // matches <length-percentage>/<percentage> but not <length>. When validating an initial-value
            // (forInitialValue) it must additionally be computationally independent (CSS Properties & Values
            // API §3): a calc() with a font/viewport-relative length (e.g. calc(1em + 2px)) is rejected there,
            // though it is fine at computed-value time. Percentages ARE computationally independent (they
            // compute to themselves), so it's the category test, not independence, that gates them per type.
            bool NumericOk(string v, CalcCategory allowed, Func<string, bool> check)
            {
                if (!CssValueParser.IsCalcFunction(v)) return check(v);
                return CalcMatchesCategory(v, allowed) && (!forInitialValue || CalcIsComputationallyIndependent(v));
            }

            switch (type)
            {
                case "<length>":
                    return NumericOk(value, CalcCategory.Length, v => CssValueParser.GetCssTokens(v).ToLength() != null);
                case "<percentage>":
                    return NumericOk(value, CalcCategory.Percentage, v => CssValueParser.GetCssTokens(v).ToPercent() != null);
                case "<length-percentage>":
                    return NumericOk(value, CalcCategory.LengthPercentage, v => CssValueParser.GetCssTokens(v).ToDistance() != null);
                case "<number>":
                    return NumericOk(value, CalcCategory.Number, v => CssValueParser.GetCssTokens(v).ToSingle() != null);
                case "<integer>":
                    return NumericOk(value, CalcCategory.Number, v => CssValueParser.GetCssTokens(v).ToInteger() != null);
                case "<angle>":
                    return NumericOk(value, CalcCategory.Angle, v => CssValueParser.GetCssTokens(v).ToAngle() != null);
                case "<ratio>":
                    // <ratio> = <number [0,∞]> [ / <number [0,∞]> ]? (CSS Values 4 §11) — notably NOT `auto`,
                    // so `@property { syntax: "<ratio>"; initial-value: auto }` is invalid and the rule drops.
                    // A whole-value calc() here resolves to a Number (a single-number ratio).
                    return NumericOk(value, CalcCategory.Number, v => AspectRatioGrammar.TryParseRatio(CssValueParser.GetCssTokens(v), out _));
                case "<color>":
                    return valueParser.IsColorValid(value);
                case "<url>":
                    // A single url() token (CSS Values 4 §4.5).
                    return CssValueParser.GetCssTokens(value).ToUri() != null;
                case "<image>":
                    // Validate through the shared Layer-A <image> grammar (Converters.ImageSourceConverter):
                    // url(), gradients, and the syntactically-valid-but-not-rendered image functions
                    // image-set()/cross-fade()/element() (CSS Images 4 §2) — the same grammar background-image
                    // and friends use, so registration and property parsing agree. Tokenized in value context
                    // so hex gradient stops (#f00 / #00f) resolve as Color tokens for the gradient converters.
                    return Converters.ImageSourceConverter.Convert(CssValueParser.GetCssTokens(value, inValueContext: true)) is not null;
                case "<time>":
                    // A literal <time> dimension (s/ms) or a <time>-category calc() (CalcParser now models
                    // time units); s/ms are absolute, so a time calc() is always computationally independent.
                    return NumericOk(value, CalcCategory.Time, v => CssValueParser.GetCssTokens(v).ToTime() != null);
                case "<resolution>":
                    // A literal <resolution> dimension (dpi/dpcm/dppx) or a <resolution>-category calc().
                    return NumericOk(value, CalcCategory.Resolution, v => CssValueParser.GetCssTokens(v).ToResolution() != null);
                case "<transform-function>":
                    // Exactly one transform function, validated (name + argument arity/types) through the
                    // shared Layer-A transform grammar. FunctionValueConverter's OnlyOrDefault rejects a list.
                    return Converters.TransformConverter.Convert(CssValueParser.GetCssTokens(value)) is not null;
                case "<transform-list>":
                    // One or more space-separated transform functions (CSS Transforms 1). GetCssTokens drops
                    // whitespace, but ToItems() (used by Many) starts a new item at each function token, so a
                    // multi-function list still splits correctly.
                    return Converters.TransformConverter.Many().Convert(CssValueParser.GetCssTokens(value)) is not null;
                case "<custom-ident>":
                    return IsIdent(value);
                case "<string>":
                    return value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0];
                default:
                    // A bare ident literal in the syntax (e.g. `auto`) is matched case-sensitively (CSS Syntax
                    // 3 — idents are case-sensitive). Every supported <data-type> has an explicit case above,
                    // and FromRule's IsValidSyntaxString rejects any unknown <foo> before a property is ever
                    // registered, so the only values reaching here are ident literals.
                    return string.Equals(type, value, StringComparison.Ordinal);
            }
        }

        /// <summary>The supported <c>syntax</c> component data types (CSS Properties &amp; Values API §3), plus
        /// <c>&lt;ratio&gt;</c> (a PeachPDF extension). A <c>syntax</c> naming any other <c>&lt;foo&gt;</c> is invalid.</summary>
        private static readonly HashSet<string> KnownDataTypes = new(StringComparer.Ordinal)
        {
            "<length>", "<number>", "<integer>", "<percentage>", "<length-percentage>", "<color>", "<angle>",
            "<ratio>", "<image>", "<url>", "<time>", "<resolution>", "<transform-function>", "<transform-list>",
            "<custom-ident>", "<string>"
        };

        /// <summary>
        /// Whether <paramref name="syntax"/> is a well-formed syntax string: every top-level <c>|</c> component
        /// (after stripping a trailing <c>+</c>/<c>#</c> multiplier) is either a supported <c>&lt;data-type&gt;</c>
        /// or an ident literal. The universal <c>*</c> is handled by the caller before this runs.
        /// </summary>
        private static bool IsValidSyntaxString(string syntax)
        {
            foreach (var rawComponent in syntax.Split('|'))
            {
                var component = rawComponent.Trim();
                var hasMultiplier = component.EndsWith("+", StringComparison.Ordinal) || component.EndsWith("#", StringComparison.Ordinal);
                if (hasMultiplier) component = component[..^1].Trim();

                if (component.Length == 0) return false;

                if (component.StartsWith("<", StringComparison.Ordinal))
                {
                    if (!KnownDataTypes.Contains(component)) return false;
                }
                // A `+`/`#` multiplier applies only to a <data-type>, never an ident literal (§3), so `auto+` is invalid.
                else if (hasMultiplier || !IsIdent(component))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a calc()-family <paramref name="value"/> parses and type-checks to a category within
        /// <paramref name="allowed"/> — the same subset test <see cref="CalcValueConverter"/> applies, so a
        /// calc()'s resolved kind (length / percentage / number / angle / time / resolution) must match the
        /// declared syntax type (e.g. <c>calc(50%)</c> matches <c>&lt;length-percentage&gt;</c> but not <c>&lt;length&gt;</c>).
        /// </summary>
        private static bool CalcMatchesCategory(string value, CalcCategory allowed)
        {
            var tokens = CssValueParser.GetCssTokens(value);
            if (tokens is not [FunctionToken fn] || !CalcParser.IsCalcFamily(fn.Data)) return false;
            var node = CalcParser.Parse(fn);
            if (node is null) return false;
            var category = CalcTypeChecker.Check(node);
            return category is not null && (category.Value & ~allowed) == 0;
        }

        /// <summary>
        /// Whether a calc()-family <paramref name="value"/> is computationally independent (CSS Properties &amp;
        /// Values API §3): it may not depend on element context. Numbers, angles, times, resolutions, and
        /// percentages are all independent (they compute to themselves without layout context); only
        /// font-relative (em/ex/ch/rem) and viewport-relative lengths are not. A non-calc value is trivially
        /// independent (its own type check handles it).
        /// </summary>
        private static bool CalcIsComputationallyIndependent(string value)
        {
            var tokens = CssValueParser.GetCssTokens(value);
            if (tokens is not [FunctionToken fn] || !CalcParser.IsCalcFamily(fn.Data)) return true;
            var node = CalcParser.Parse(fn);
            return node is not null && IsComputationallyIndependent(node);
        }

        private static bool IsComputationallyIndependent(CalcNode node) => node switch
        {
            // A percentage computes to itself (it's preserved to used-value time), so it's independent; the
            // <length> vs <length-percentage> gating is handled by the category test in CalcMatchesCategory.
            NumberCalcNode or AngleCalcNode or TimeCalcNode or ResolutionCalcNode or PercentageCalcNode => true,
            // Viewport/ch units never reach the tree (CalcParser doesn't admit them); em/ex/rem do, and are relative.
            DimensionCalcNode dimension => new Length(0f, dimension.Unit).IsAbsolute,
            UnaryCalcNode unary => IsComputationallyIndependent(unary.Operand),
            BinaryCalcNode binary => IsComputationallyIndependent(binary.Left) && IsComputationallyIndependent(binary.Right),
            CallCalcNode call => call.Arguments.All(IsComputationallyIndependent),
            _ => false,
        };

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
