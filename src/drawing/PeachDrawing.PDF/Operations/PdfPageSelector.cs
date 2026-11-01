using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// Reads the text a caller uses to pick pages of a document: <c>"1,3,5-10"</c>, <c>"r3-r1"</c> (the last three), <c>"z-1"</c> (all, reversed),
    /// <c>"1-20:even"</c>, <c>"1-10,x3-4"</c> (all but 3 and 4).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Pages count from 1. <c>z</c> is the last page, <c>r1</c> too, <c>r2</c> the one before it.</description></item>
    /// <item><description>A list is read left to right and the pages come out in the order written, a page named twice appearing twice.</description></item>
    /// <item><description>A range <c>a-b</c> runs up when <c>a &lt;= b</c> and down otherwise; both ends are included.</description></item>
    /// <item><description><c>:odd</c> or <c>:even</c> after a range keeps the pages at odd or even positions <em>within that range</em>, counting its first page as 1.</description></item>
    /// <item><description>An <c>x</c> before a page or range removes those pages from everything selected so far.</description></item>
    /// </list>
    /// </remarks>
    public static class PdfPageSelector
    {
        /// <summary>The one-based page numbers <paramref name="selector"/> names for a document of <paramref name="pageCount"/> pages, in order.</summary>
        /// <param name="selector">the text to read</param>
        /// <param name="pageCount">the number of pages of the document</param>
        /// <returns>the selected pages, each from 1 to <paramref name="pageCount"/></returns>
        /// <exception cref="FormatException">the text is not a page selector, or names a page outside the document</exception>
        public static IReadOnlyList<int> Select(string selector, int pageCount)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (pageCount < 0)
                throw new ArgumentOutOfRangeException(nameof(pageCount));

            var pages = new List<int>();
            foreach (var raw in selector.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                    throw Bad(selector, "it has an empty entry");

                var exclude = part[0] is 'x' or 'X';
                if (exclude)
                    part = part[1..].Trim();

                var filter = 0; // 0 = every page, 1 = odd positions, 2 = even positions
                var colon = part.IndexOf(':');
                if (colon >= 0)
                {
                    var suffix = part[(colon + 1)..].Trim();
                    filter = suffix.ToLowerInvariant() switch
                    {
                        "odd" => 1,
                        "even" => 2,
                        _ => throw Bad(selector, $"':{suffix}' is not ':odd' or ':even'"),
                    };
                    part = part[..colon].Trim();
                }

                var dash = part.IndexOf('-');
                int from, to;
                if (dash < 0)
                {
                    from = to = Number(part, pageCount, selector);
                }
                else
                {
                    from = Number(part[..dash].Trim(), pageCount, selector);
                    to = Number(part[(dash + 1)..].Trim(), pageCount, selector);
                }

                var step = from <= to ? 1 : -1;
                var position = 0;
                var range = new List<int>();
                for (var page = from; page != to + step; page += step)
                {
                    position++;
                    if (filter == 1 && position % 2 == 0 || filter == 2 && position % 2 == 1)
                        continue;
                    range.Add(page);
                }

                if (exclude)
                    pages.RemoveAll(range.Contains);
                else
                    pages.AddRange(range);
            }

            return pages;
        }

        private static int Number(string text, int pageCount, string whole)
        {
            int page;
            if (text.Length == 1 && text[0] is 'z' or 'Z')
            {
                page = pageCount;
            }
            else if (text.Length > 1 && text[0] is 'r' or 'R')
            {
                if (!int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fromEnd) || fromEnd < 1)
                    throw Bad(whole, $"'{text}' is not a page");
                page = pageCount - fromEnd + 1;
            }
            else if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page))
            {
                throw Bad(whole, $"'{text}' is not a page");
            }

            if (page < 1 || page > pageCount)
                throw Bad(whole, $"page {text} is outside the document, which has {pageCount} page{(pageCount == 1 ? "" : "s")}");

            return page;
        }

        private static FormatException Bad(string selector, string reason) =>
            new($"'{selector}' is not a page selector: {reason}. Use page numbers, ranges like 3-7, z or r2 for pages from the end, :odd/:even, and x to exclude.");
    }
}
