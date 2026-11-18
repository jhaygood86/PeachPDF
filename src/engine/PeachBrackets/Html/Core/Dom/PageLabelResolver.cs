using PeachBrackets.Annotations;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// Turns the <c>-peachpdf-pdf-page-label</c> each page's <c>@page</c> rules give into the ranges of a PDF page-label tree
    /// (ISO 32000-1 12.4.2). The label is a <c>content</c> list: <c>[ "prefix" ]? counter(page, &lt;style&gt;)</c> becomes a range with that numbering
    /// style and prefix, so a run of pages is one range; any other content (other text around the counter, a counter style PDF has no name for,
    /// letters past Z, whose repeat rule differs from CSS's) becomes the page's whole label as text, a range of its own.
    /// </summary>
    internal static class PageLabelResolver
    {
        /// <summary>One page's label, before neighboring pages are folded into ranges: the style and prefix, and the number the style writes.</summary>
        private readonly record struct Label(PageLabelStyle Style, string Prefix, int Number);

        /// <summary>
        /// The label ranges of a document, or none when no page asks for a label (the pages then number as a viewer does by itself).
        /// </summary>
        /// <param name="pageStyles">each page's merged <c>@page</c> declaration, by page, or null for a page no rule matched</param>
        /// <param name="counters">the <c>page</c> counter's value on each page</param>
        /// <param name="totalPages">the page total <c>counter(pages)</c> reports</param>
        /// <param name="pageYs">where each page begins, for <c>string()</c> in a label</param>
        /// <param name="container">the document, for counter styles and named strings</param>
        public static IReadOnlyList<PageLabelRange> Resolve(
            IReadOnlyList<StyleDeclaration?> pageStyles, IReadOnlyList<int> counters, int totalPages, IReadOnlyList<double> pageYs, HtmlContainerInt container)
        {
            var declared = pageStyles.Select(s => s?.PdfPageLabel).ToList();
            if (declared.All(d => string.IsNullOrWhiteSpace(d) || d.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase)))
                return [];

            var ranges = new List<PageLabelRange>();
            Label? previous = null;
            for (var i = 0; i < pageStyles.Count; i++)
            {
                var label = Evaluate(declared[i], counters[i], totalPages, pageYs[i], container);
                var continues = previous is { } p && p.Style == label.Style && p.Prefix == label.Prefix
                    && (label.Style == PageLabelStyle.None || label.Number == p.Number + 1);
                if (!continues)
                    ranges.Add(new PageLabelRange { PageIndex = i, Style = label.Style, Prefix = label.Prefix, Start = label.Number });
                previous = label;
            }

            return ranges;
        }

        private static Label Evaluate(string? declaration, int number, int totalPages, double pageY, HtmlContainerInt container)
        {
            var text = declaration?.Trim() ?? "";
            if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return new Label(PageLabelStyle.Decimal, "", number);

            // A bare counter style is shorthand for counter(page, <style>).
            if (!text.Contains('(') && !text.Contains('"') && !text.Contains('\'') && !text.Contains(' '))
                text = "counter(page, " + text + ")";

            if (TryNumbered(text, number, out var numbered))
                return numbered;

            // Anything else is the page's whole label as written: text only, no numbering style.
            var literal = MarginBoxRenderer.ResolveContent(text, number, totalPages, pageY, container, container.NamedStrings) ?? "";
            return new Label(PageLabelStyle.None, literal, 1);
        }

        /// <summary><c>[ &lt;string&gt; ]* counter(page [, style])</c> with a style PDF can number in, at a value that style writes the way CSS does.</summary>
        private static bool TryNumbered(string text, int number, out Label label)
        {
            label = default;
            using var pooled = CssValueParser.GetCssTokensPooled(text);
            List<Token> tokens = pooled;

            var prefix = new System.Text.StringBuilder();
            PageLabelStyle? style = null;
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case { Type: TokenType.Whitespace }:
                        break;
                    case { Type: TokenType.String } s when style is null:
                        prefix.Append(s.Data);
                        break;
                    case { Type: TokenType.Function, Data: "counter" } counter when style is null:
                    {
                        var args = counter.ArgumentTokens.Where(t => t.Type is not (TokenType.Whitespace or TokenType.Comma)).ToArray();
                        if (args.Length is < 1 or > 2 || !args[0].Data.Equals("page", StringComparison.OrdinalIgnoreCase))
                            return false;
                        var name = args.Length == 2 ? args[1].Data.ToString().ToLowerInvariant() : "decimal";
                        style = name switch
                        {
                            "decimal" => PageLabelStyle.Decimal,
                            "lower-roman" => PageLabelStyle.RomanLower,
                            "upper-roman" => PageLabelStyle.RomanUpper,
                            // PDF writes letters past Z as a repeat (AA, BB), CSS as a count (AA, AB), so only the first 26 agree.
                            "lower-alpha" or "lower-latin" when number is >= 1 and <= 26 => PageLabelStyle.LettersLower,
                            "upper-alpha" or "upper-latin" when number is >= 1 and <= 26 => PageLabelStyle.LettersUpper,
                            _ => null,
                        };
                        if (style is null || (style is PageLabelStyle.RomanLower or PageLabelStyle.RomanUpper && number is < 1 or > 3999)
                            || (style is PageLabelStyle.Decimal && number < 1))
                            return false;
                        break;
                    }
                    default:
                        return false;
                }
            }

            if (style is null) return false;
            label = new Label(style.Value, prefix.ToString(), number);
            return true;
        }
    }
}
