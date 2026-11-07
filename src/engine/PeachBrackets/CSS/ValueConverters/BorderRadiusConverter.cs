#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    using static Converters;

    internal sealed class BorderRadiusConverter : IValueConverter
    {
        private readonly IValueConverter _converter;
        private readonly string[] _labels;

        /// <summary>The <c>border-radius</c> shorthand: four corners, top-left first.</summary>
        public BorderRadiusConverter()
            : this(PropertyNames.BorderTopLeftRadius, PropertyNames.BorderTopRightRadius,
                PropertyNames.BorderBottomRightRadius, PropertyNames.BorderBottomLeftRadius)
        {
        }

        /// <summary>A radius shorthand over the given two or four longhands (<c>border-top-radius</c>, <c>corner</c>...).</summary>
        public BorderRadiusConverter(params string[] labels)
        {
            _labels = labels;
            _converter = LengthOrPercentConverter.Periodic(labels);
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var front = new List<Token>();
            var back = new List<Token>();
            var current = front;

            foreach (var token in value)
                if (token.Type == TokenType.Delim && token.Data.Is("/"))
                {
                    if (current == back) return null;

                    current = back;
                }
                else
                {
                    current.Add(token);
                }

            var horizontal = _converter.Convert(front);

            if (horizontal == null) return null;

            var vertical = current == back ? _converter.Convert(back) : horizontal;

            if (vertical == null) return null;

            return new BorderRadiusValue(horizontal, vertical, new TokenValue(value));
        }

        public IPropertyValue Construct(Property[] properties)
        {
            if (properties.Length == 4 && _labels.Length == 4)
            {
                var front = new List<Token>();
                var back = new List<Token>();
                var props = new List<Property>
                {
                    properties.First(m => m.Name.Is(PropertyNames.BorderTopLeftRadius)),
                    properties.First(m => m.Name.Is(PropertyNames.BorderTopRightRadius)),
                    properties.First(
                        m => m.Name.Is(PropertyNames.BorderBottomRightRadius)),
                    properties.First(
                        m => m.Name.Is(PropertyNames.BorderBottomLeftRadius))
                };

                for (var i = 0; i < props.Count; i++)
                {
                    var dpv = props[i].DeclaredValue as IEnumerable<IPropertyValue>;

                    if (dpv == null) return null;

                    var first = dpv.First().Original;
                    var second = dpv.Last().Original;

                    if (i != 0)
                    {
                        front.Add(Token.Whitespace);
                        back.Add(Token.Whitespace);
                    }

                    front.AddRange(first);
                    back.AddRange(second);
                }

                var h = _converter.Convert(front);
                var v = _converter.Convert(back);
                var o = front.Concat(new Token(TokenType.Delim, "/".AsMemory(), TextPosition.Empty)).Concat(back);

                return new BorderRadiusValue(h, v, new TokenValue(o));
            }

            return null;
        }

        private sealed class BorderRadiusValue : IPropertyValue
        {
            private readonly IPropertyValue _horizontal;
            private readonly IPropertyValue _vertical;

            public BorderRadiusValue(IPropertyValue horizontal, IPropertyValue vertical, TokenValue original)
            {
                _horizontal = horizontal;
                _vertical = vertical;
                Original = original;
            }

            public string CssText
            {
                get
                {
                    var horizontal = _horizontal.CssText;

                    if (_vertical == null) return horizontal;

                    var vertical = _vertical.CssText;

                    return horizontal != vertical
                        ? string.Concat(horizontal, " / ", vertical)
                        : horizontal;
                }
            }

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                var h = _horizontal.ExtractFor(name);
                var v = _vertical.ExtractFor(name);
                return new TokenValue(h.Concat(Token.Whitespace).Concat(v));
            }
        }
    }
}