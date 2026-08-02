#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Parses the toggle-keyword grammar of <c>font-variant-east-asian</c> - one or more whitespace-
    /// separated tokens from <see cref="Map.FontVariantEastAsianTokens"/>, each of the 3 axes (variant
    /// forms, width, ruby) appearing at most once. The bare <c>normal</c> keyword is handled
    /// separately, by <c>Converters.FontVariantEastAsianConverter</c> composing this with
    /// <c>.Or(...)</c> - mirrors <see cref="FontVariantLigaturesValueConverter"/>.
    /// </summary>
    internal sealed class FontVariantEastAsianValueConverter : IValueConverter
    {
        private static readonly IValueConverter TokenListConverter = Map.FontVariantEastAsianTokens.ToConverter().Many();

        // Each inner array is one axis's mutually-exclusive keywords - CSS Values' `||` combinator
        // allows each component at most once.
        private static readonly string[][] Axes =
        [
            [Keywords.Jis78Forms, Keywords.Jis83Forms, Keywords.Jis90Forms, Keywords.Jis04Forms, Keywords.Simplified, Keywords.Traditional],
            [Keywords.FullWidth, Keywords.ProportionalWidth],
            [Keywords.Ruby]
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
