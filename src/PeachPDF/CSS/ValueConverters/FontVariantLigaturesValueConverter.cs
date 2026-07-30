#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Parses the toggle-keyword half of <c>font-variant-ligatures</c>'s grammar - one or more
    /// whitespace-separated tokens from <see cref="Map.FontVariantLigatureTokens"/>, each of the
    /// four axes (common/discretionary/historical ligatures, contextual) appearing at most once.
    /// The bare <c>normal</c>/<c>none</c> keywords are handled separately, by
    /// <c>Converters.FontVariantLigaturesConverter</c> composing this with <c>.Or(...)</c>.
    /// </summary>
    internal sealed class FontVariantLigaturesValueConverter : IValueConverter
    {
        private static readonly IValueConverter TokenListConverter = Map.FontVariantLigatureTokens.ToConverter().Many();

        // Each inner array is one axis's two mutually-exclusive keywords - CSS Values' `||` combinator
        // allows each component at most once, so seeing either keyword from an axis a second time (the
        // same one repeated, or its opposite) is invalid.
        private static readonly string[][] Axes =
        [
            [Keywords.CommonLigatures, Keywords.NoCommonLigatures],
            [Keywords.DiscretionaryLigatures, Keywords.NoDiscretionaryLigatures],
            [Keywords.HistoricalLigatures, Keywords.NoHistoricalLigatures],
            [Keywords.Contextual, Keywords.NoContextual]
        ];

        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var result = TokenListConverter.Convert(value);
            return result != null && IsValid(result.CssText) ? result : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            var result = TokenListConverter.Construct(properties);
            return result != null && IsValid(result.CssText) ? result : null;
        }

        private static bool IsValid(string cssText)
        {
            var tokens = cssText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var seenAxes = new HashSet<int>();

            foreach (var token in tokens)
            {
                for (var axis = 0; axis < Axes.Length; axis++)
                {
                    if (Axes[axis].Contains(token, StringComparer.OrdinalIgnoreCase) && !seenAxes.Add(axis))
                        return false;
                }
            }

            return true;
        }
    }
}
