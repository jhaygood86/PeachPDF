#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Parses the toggle-keyword grammar of <c>font-variant-numeric</c> - one or more whitespace-
    /// separated tokens from <see cref="Map.FontVariantNumericTokens"/>, each of the 4 axes (figure,
    /// spacing, fraction, ordinal, slashed-zero) appearing at most once. The bare <c>normal</c>
    /// keyword is handled separately, by <c>Converters.FontVariantNumericConverter</c> composing this
    /// with <c>.Or(...)</c> - mirrors <see cref="FontVariantLigaturesValueConverter"/>.
    /// </summary>
    internal sealed class FontVariantNumericValueConverter : IValueConverter
    {
        private static readonly IValueConverter TokenListConverter = Map.FontVariantNumericTokens.ToConverter().Many();

        // Each inner array is one axis's mutually-exclusive keywords - CSS Values' `||` combinator
        // allows each component at most once, so seeing a second keyword from an axis already seen
        // (its only keyword, for the singleton axes) is invalid.
        private static readonly string[][] Axes =
        [
            [Keywords.LiningNums, Keywords.OldstyleNums],
            [Keywords.ProportionalNums, Keywords.TabularNums],
            [Keywords.DiagonalFractions, Keywords.StackedFractions],
            [Keywords.Ordinal],
            [Keywords.SlashedZero]
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
