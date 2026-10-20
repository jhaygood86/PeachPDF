using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.Layout
{
    /// <summary>Parses the page-range text of a file layer (<c>"1-3,5,7-z"</c>).</summary>
    internal static class PageRange
    {
        /// <summary>
        /// The one-based pages <paramref name="range"/> names, in the order written, with pages outside <c>1..pageCount</c> dropped.
        /// </summary>
        /// <exception cref="FormatException">the text is not a page range</exception>
        public static List<int> Parse(string range, int pageCount)
        {
            var pages = new List<int>();
            foreach (var part in range.Split(','))
            {
                var text = part.Trim();
                if (text.Length == 0)
                    continue;

                var dash = text.IndexOf('-');
                if (dash < 0)
                {
                    Add(pages, Number(text, pageCount, range), pageCount);
                    continue;
                }

                var from = Number(text[..dash].Trim(), pageCount, range);
                var to = Number(text[(dash + 1)..].Trim(), pageCount, range);
                var step = from <= to ? 1 : -1;
                for (var page = from; page != to + step; page += step)
                    Add(pages, page, pageCount);
            }

            return pages;
        }

        private static void Add(List<int> pages, int page, int pageCount)
        {
            if (page >= 1 && page <= pageCount)
                pages.Add(page);
        }

        private static int Number(string text, int pageCount, string whole)
        {
            if (text is "z" or "Z")
                return pageCount;

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return value;

            throw new FormatException($"'{whole}' is not a page range: expected page numbers, spans like 2-5, and z for the last page.");
        }
    }
}
