#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// <c>contain-intrinsic-size</c> (css-sizing-4 §5.1): one or two <c>auto? [ none | &lt;length&gt; ]</c> groups, the first for the width and the second for
    /// the height; with one group both take it. A group is one or two tokens, so the value is split at each <c>auto</c> that follows a complete group.
    /// </summary>
    internal sealed class ContainIntrinsicSizeShorthandConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToList();
            var groups = new List<List<Token>>();

            foreach (var token in tokens)
            {
                var isAuto = token.Type == TokenType.Ident && token.Data.Isi(Keywords.Auto);

                // `auto` opens a group; any other token completes the group it is in, or is a group of its own.
                if (isAuto || groups.Count == 0 || IsComplete(groups[^1]))
                {
                    groups.Add([token]);
                }
                else
                {
                    // Kept apart by the space they were written with, so the group reads back as `auto 20pt`.
                    groups[^1].Add(new Token(TokenType.Whitespace, " ".AsMemory(), TextPosition.Empty));
                    groups[^1].Add(token);
                }
            }

            if (groups.Count is 0 or > 2 || groups.Any(g => !ContainGrammar.TryParseIntrinsic(g))) return null;

            return new ShorthandValue(groups[0], groups.Count == 2 ? groups[1] : groups[0], new TokenValue(value));
        }

        private static bool IsComplete(List<Token> group) =>
            !(group.Count == 1 && group[0].Type == TokenType.Ident && group[0].Data.Isi(Keywords.Auto));

        public IPropertyValue Construct(Property[] properties)
        {
            if (properties.Length != 2) return null;

            var width = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.ContainIntrinsicWidth))?.DeclaredValue;
            var height = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.ContainIntrinsicHeight))?.DeclaredValue;
            if (width is null || height is null) return null;

            IReadOnlyList<Token> widthTokens = width.Original;
            IReadOnlyList<Token> heightTokens = height.Original;
            var original = width.CssText == height.CssText
                ? new TokenValue(widthTokens)
                : new TokenValue(widthTokens.Concat(heightTokens));

            return new ShorthandValue([with(widthTokens)], [with(heightTokens)], original);
        }

        private sealed class ShorthandValue : IPropertyValue
        {
            private readonly List<Token> _width;
            private readonly List<Token> _height;

            public ShorthandValue(List<Token> width, List<Token> height, TokenValue original)
            {
                _width = width;
                _height = height;
                Original = original;
            }

            public string CssText => new TokenValue(_width).ToText() == new TokenValue(_height).ToText()
                ? new TokenValue(_width).ToText()
                : new TokenValue(_width).ToText() + " " + new TokenValue(_height).ToText();

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                if (name.Is(PropertyNames.ContainIntrinsicWidth)) return new TokenValue(_width);
                if (name.Is(PropertyNames.ContainIntrinsicHeight)) return new TokenValue(_height);
                return TokenValue.Empty;
            }
        }
    }
}
