#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    internal sealed class EndListValueConverter : IValueConverter
    {
        private readonly IValueConverter _endConverter;
        private readonly IValueConverter _listConverter;
        private readonly IReadOnlyDictionary<string, List<Token>> _layerDefaults;

        /// <summary>
        /// A list of layers, every one but the last converted by <paramref name="listConverter"/> and the last by <paramref name="endConverter"/>
        /// (the <c>background</c> and <c>cursor</c> shorthands: a final layer that alone may carry a color or a fallback keyword).
        /// </summary>
        /// <param name="listConverter">The converter for every layer but the last.</param>
        /// <param name="endConverter">The converter for the last layer.</param>
        /// <param name="layerDefaults">
        /// For the longhands that hold one value per layer (<c>background-image</c>, <c>background-repeat</c>, ...): the value a layer takes when its
        /// text leaves that longhand out (CSS Backgrounds 3 section 2.10: an omitted sub-property is its initial value). A longhand that is not listed
        /// (the colour, which only the last layer sets) is taken from the layers that do set it.
        /// </param>
        public EndListValueConverter(IValueConverter listConverter, IValueConverter endConverter, IReadOnlyDictionary<string, string> layerDefaults = null)
        {
            _listConverter = listConverter;
            _endConverter = endConverter;

            // Tokenized once: ExtractFor runs for every longhand of every declaration, and the defaults never change.
            _layerDefaults = layerDefaults?.ToDictionary(pair => pair.Key, pair => Tokenize(pair.Value));
        }

        /// <summary>Tokenizes an authored value string with the CSS-OM <see cref="Lexer"/> (whitespace and EOF stripped), as <c>GridTemplateValueConverter</c> does.</summary>
        private static List<Token> Tokenize(string text)
        {
            using var lexer = new Lexer(text) { IsInValue = false };
            var tokens = new List<Token>();
            Token token;

            do
            {
                token = lexer.Get();
                if (token.Type != TokenType.EndOfFile && token.Type != TokenType.Whitespace) tokens.Add(token);
            } while (token.Type != TokenType.EndOfFile);

            return tokens;
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var items = value.ToList();
            var n = items.Count - 1;

            // A comma with nothing on one side of it (url(a), or url(a), , url(b)) is not a layer: the declaration is invalid. The layer converters
            // accept an empty layer, and the hole this left in the longhands' comma lists used to hide that.
            if (n > 0 && items.Exists(item => item.Count == 0)) return null;
            var values = new IPropertyValue[n + 1];

            for (var i = 0; i < n; i++)
            {
                values[i] = _listConverter.Convert(items[i]);

                if (values[i] == null) return null;
            }

            values[n] = _endConverter.Convert(items[n]);
            return values[n] != null ? new ListValue(values, value, _layerDefaults) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            var valueList = new List<List<Token>>[properties.Length];
            var dummies = new Property[properties.Length];
            var max = 0;

            for (var i = 0; i < properties.Length; i++)
            {
                var value = properties[i].DeclaredValue;
                valueList[i] = value != null ? value.Original.ToList() : new List<List<Token>>();

                dummies[i] = PropertyFactory.Instance.CreateLonghand(properties[i].Name);
                max = Math.Max(max, valueList[i].Count);
            }

            var values = new IPropertyValue[max];

            for (var i = 0; i < max; i++)
            {
                for (var j = 0; j < dummies.Length; j++)
                {
                    var list = valueList[j];
                    var tokens = list.Count > i ? list[i] : Enumerable.Empty<Token>();
                    dummies[j].TrySetValue(new TokenValue(tokens));
                }

                var converter = i < max - 1 ? _listConverter : _endConverter;
                values[i] = converter.Construct(dummies);
            }

            return new ListValue(values, Enumerable.Empty<Token>(), _layerDefaults);
        }

        private sealed class ListValue : IPropertyValue
        {
            private readonly IPropertyValue[] _values;
            private readonly IReadOnlyDictionary<string, List<Token>> _layerDefaults;

            public ListValue(IPropertyValue[] values, IEnumerable<Token> tokens, IReadOnlyDictionary<string, List<Token>> layerDefaults)
            {
                _values = values;
                _layerDefaults = layerDefaults;
                Original = new TokenValue(tokens);
            }

            public string CssText
            {
                get { return string.Join(", ", _values.Select(m => m.CssText)); }
            }

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                var layers = new List<List<Token>>(_values.Length);

                foreach (var value in _values)
                {
                    var extracted = value.ExtractFor(name);
                    layers.Add(extracted is { Count: > 0 } ? new List<Token>(extracted) : null);
                }

                // No layer names this longhand: it is left to its initial value, which an empty value stands for.
                if (layers.TrueForAll(layer => layer is null)) return new TokenValue(Enumerable.Empty<Token>());

                List<Token> layerDefault = null;
                _layerDefaults?.TryGetValue(name, out layerDefault);

                var tokens = new List<Token>();

                foreach (var layer in layers)
                {
                    // A layer that leaves a per-layer longhand out takes its initial value, so the list keeps one entry per layer; any other
                    // longhand (the colour of the last layer) is only taken from the layers that set it. Without this the join wrote a trailing
                    // or leading comma, which no longhand accepts, and the whole longhand fell back to its initial value.
                    var entry = layer ?? layerDefault;
                    if (entry is null) continue;

                    if (tokens.Count > 0) tokens.Add(Token.Comma);
                    tokens.AddRange(entry);
                }

                return new TokenValue(tokens);
            }
        }
    }
}