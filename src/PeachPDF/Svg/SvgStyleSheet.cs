// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// One parsed CSS rule from an SVG <c>&lt;style&gt;</c> element: a single simple/compound selector
    /// (type name and/or <c>.class</c>es and/or one <c>#id</c> - no combinators, attribute selectors,
    /// or pseudo-classes/elements, a deliberate v1 scope limit; see <see cref="SvgStyleSheet.Parse"/>)
    /// plus the declarations it contributes when it matches.
    /// </summary>
    internal sealed class SvgStyleRule
    {
        /// <summary>Null or <c>"*"</c> means "matches any type" (no type constraint).</summary>
        public string? TypeName { get; init; }
        public string? Id { get; init; }
        public IReadOnlyList<string> Classes { get; init; } = [];
        public IReadOnlyDictionary<string, string> Declarations { get; init; } = new Dictionary<string, string>();

        /// <summary>Position among all rules in the sheet, in document order - the tiebreaker for equal-specificity rules (later wins).</summary>
        public int SourceOrder { get; init; }

        /// <summary>
        /// Standard CSS specificity as an (id, class, type) tuple. <see cref="ValueTuple{T1, T2, T3}"/>
        /// implements lexicographic <see cref="IComparable"/>, so ordering by this directly gives
        /// correct id-beats-class-beats-type precedence without a custom comparer.
        /// </summary>
        public (int IdCount, int ClassCount, int TypeCount) Specificity =>
            (Id is not null ? 1 : 0, Classes.Count, TypeName is not null && TypeName != "*" ? 1 : 0);
    }

    /// <summary>
    /// A minimal CSS engine scoped specifically to SVG <c>&lt;style&gt;</c> content: parses simple
    /// selector rules and matches them against a node's own name/id/class (no ancestor/descendant
    /// matching - see <see cref="Parse"/> for the full list of deliberately unsupported grammar).
    /// Deliberately independent of the full HTML/CSS engine (<c>CssParser</c>/<c>CssData</c>), which is
    /// tightly coupled to a live <c>CssBox</c> tree that only one of the two <c>ISvgSourceNode</c>
    /// implementations (<see cref="CssBoxSvgSourceNode"/>) actually has - this matcher works
    /// identically for both source kinds since it only ever looks at plain strings.
    /// </summary>
    internal sealed class SvgStyleSheet
    {
        public List<SvgStyleRule> Rules { get; } = [];

        /// <summary>
        /// Parses one or more concatenated <c>&lt;style&gt;</c> blocks' CSS text. Supported grammar:
        /// comma-separated simple/compound selectors (type name and/or <c>.class</c>(es) and/or one
        /// <c>#id</c>, e.g. <c>rect.foo#bar</c>) each followed by a <c>{ property: value; ... }</c>
        /// declaration block (parsed via the same grammar as <see cref="SvgValueParsers.ParseStyleDeclarations"/>,
        /// reused verbatim since a declaration block's contents are identical to a <c>style=""</c>
        /// attribute's). NOT supported: descendant/child/sibling combinators, attribute selectors,
        /// pseudo-classes/pseudo-elements, at-rules (e.g. <c>@media</c>) - any selector using this
        /// grammar is silently skipped (not falling over the whole sheet) rather than mis-parsed.
        /// </summary>
        public static SvgStyleSheet Parse(string? cssText)
        {
            var sheet = new SvgStyleSheet();

            if (string.IsNullOrWhiteSpace(cssText))
                return sheet;

            var text = StripComments(cssText);
            var pos = 0;
            var sourceOrder = 0;

            while (pos < text.Length)
            {
                var openBrace = text.IndexOf('{', pos);
                if (openBrace < 0)
                    break;

                var closeBrace = text.IndexOf('}', openBrace);
                if (closeBrace < 0)
                    break;

                var selectorListText = text[pos..openBrace].Trim();
                var declarationText = text[(openBrace + 1)..closeBrace];
                pos = closeBrace + 1;

                if (selectorListText.Length == 0)
                    continue;

                var declarations = SvgValueParsers.ParseStyleDeclarations(declarationText);
                if (declarations.Count == 0)
                    continue;

                foreach (var selectorText in selectorListText.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TryParseSimpleSelector(selectorText.Trim(), out var typeName, out var id, out var classes))
                        continue;

                    sheet.Rules.Add(new SvgStyleRule
                    {
                        TypeName = typeName,
                        Id = id,
                        Classes = classes,
                        Declarations = declarations,
                        SourceOrder = sourceOrder++,
                    });
                }
            }

            return sheet;
        }

        /// <summary>
        /// Returns the merged declarations of every rule matching (<paramref name="tagName"/>,
        /// <paramref name="id"/>, <paramref name="classes"/>), applied lowest-to-highest specificity
        /// (source order breaking ties) so a later/more-specific rule's property values win - matching
        /// normal CSS cascade-within-one-origin behavior.
        /// </summary>
        public Dictionary<string, string> Match(string tagName, string? id, IReadOnlyList<string> classes)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (Rules.Count == 0)
                return result;

            var matching = Rules
                .Where(r => Matches(r, tagName, id, classes))
                .OrderBy(r => r.Specificity)
                .ThenBy(r => r.SourceOrder);

            foreach (var rule in matching)
                foreach (var (property, value) in rule.Declarations)
                    result[property] = value;

            return result;
        }

        private static bool Matches(SvgStyleRule rule, string tagName, string? id, IReadOnlyList<string> classes)
        {
            if (rule.TypeName is not null && rule.TypeName != "*" && !string.Equals(rule.TypeName, tagName, StringComparison.Ordinal))
                return false;

            if (rule.Id is not null && !string.Equals(rule.Id, id, StringComparison.Ordinal))
                return false;

            foreach (var cls in rule.Classes)
                if (!classes.Contains(cls, StringComparer.Ordinal))
                    return false;

            return true;
        }

        private static string StripComments(string css)
        {
            var result = css;

            while (true)
            {
                var start = result.IndexOf("/*", StringComparison.Ordinal);
                if (start < 0)
                    return result;

                var end = result.IndexOf("*/", start + 2, StringComparison.Ordinal);
                result = end < 0 ? result[..start] : result[..start] + result[(end + 2)..];
            }
        }

        private static bool TryParseSimpleSelector(string selector, out string? typeName, out string? id, out List<string> classes)
        {
            typeName = null;
            id = null;
            classes = [];

            if (selector.Length == 0 || selector.IndexOfAny([' ', '\t', '\n', '\r', '>', '+', '~', '[', ']', ':', '(', ')']) >= 0)
                return false;

            var pos = 0;

            if (selector[0] == '*')
            {
                typeName = "*";
                pos = 1;
            }
            else if (char.IsLetter(selector[0]) || selector[0] == '_')
            {
                var start = pos;
                while (pos < selector.Length && (char.IsLetterOrDigit(selector[pos]) || selector[pos] is '-' or '_'))
                    pos++;
                typeName = selector[start..pos];
            }

            while (pos < selector.Length)
            {
                if (selector[pos] == '.')
                {
                    pos++;
                    var start = pos;
                    while (pos < selector.Length && (char.IsLetterOrDigit(selector[pos]) || selector[pos] is '-' or '_'))
                        pos++;
                    if (pos == start)
                        return false;
                    classes.Add(selector[start..pos]);
                }
                else if (selector[pos] == '#')
                {
                    pos++;
                    var start = pos;
                    while (pos < selector.Length && (char.IsLetterOrDigit(selector[pos]) || selector[pos] is '-' or '_'))
                        pos++;
                    if (pos == start)
                        return false;
                    id = selector[start..pos];
                }
                else
                {
                    // Unrecognized character (e.g. a stray combinator not already caught above) -
                    // reject the whole selector rather than guessing at a partial match.
                    return false;
                }
            }

            return typeName is not null || id is not null || classes.Count > 0;
        }
    }
}
