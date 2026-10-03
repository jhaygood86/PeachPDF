#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.CounterStyles
{
    /// <summary>
    /// The document's <c>@counter-style</c> rules by name, plus the lookup that turns a counter-style
    /// reference (a name or a <c>symbols()</c> value) into a <see cref="CounterStyle"/>.
    /// </summary>
    internal sealed class CounterStyleRegistry
    {
        // The names §3 reserves: an @counter-style naming one is invalid and ignored.
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "none", "decimal", "disc", "square", "circle", "disclosure-open", "disclosure-closed",
            "inherit", "initial", "unset", "revert", "revert-layer", "default",
        };

        private readonly Dictionary<string, CounterStyle> _styles;

        private CounterStyleRegistry(Dictionary<string, CounterStyle> styles) => _styles = styles;

        public bool TryGet(string name, out CounterStyle style) => _styles.TryGetValue(name, out style!);

        /// <summary>
        /// Harvests every valid <c>@counter-style</c> rule. Names are case-sensitive; a later rule of the
        /// same name replaces an earlier one (cascade order). Returns null when the document declares none,
        /// so the common case costs nothing downstream.
        /// </summary>
        public static CounterStyleRegistry? BuildRegistry(CssData cssData)
        {
            Dictionary<string, CounterStyle>? styles = null;

            foreach (var rule in cssData.EnumerateRulesRecursive().OfType<CounterStyleRule>())
            {
                if (string.IsNullOrEmpty(rule.Name) || ReservedNames.Contains(rule.Name)) continue;

                var style = CounterStyle.FromRule(rule);
                if (style is null) continue;

                (styles ??= new Dictionary<string, CounterStyle>(StringComparer.Ordinal))[rule.Name] = style;
            }

            return styles is null ? null : new CounterStyleRegistry(styles);
        }

        /// <summary>
        /// Resolves a counter-style reference to an author-defined style, or null when it names (or falls
        /// back to) a predefined style - which the built-in formatter handles - or is an invalid
        /// <c>symbols()</c>. A <c>@counter-style</c> by a predefined name (other than the reserved few)
        /// overrides it.
        /// </summary>
        public static CounterStyle? Find(string style, CounterStyleRegistry? registry)
        {
            if (style.Length > 8 && style.StartsWith("symbols(", StringComparison.OrdinalIgnoreCase))
            {
                using var pooled = CssValueParser.GetCssTokensPooled(style);
                List<Token> tokens = pooled;
                return tokens.Count == 1 ? CounterStyle.FromSymbolsFunction(tokens[0]) : null;
            }

            return registry is not null && registry.TryGet(style, out var found) ? found : null;
        }
    }
}
