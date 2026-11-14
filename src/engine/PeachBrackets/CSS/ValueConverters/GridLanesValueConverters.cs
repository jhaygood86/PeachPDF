#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Validates <c>grid-lanes-direction</c> (CSS Grid 3): <c>normal | [ row | column ] [ fill-reverse || track-reverse ]?</c>.
    /// The authored text is kept for the lanes layout engine, which reads which words are present.
    /// </summary>
    internal sealed class GridLanesDirectionValueConverter : IValueConverter
    {
        internal static readonly IValueConverter Instance = new GridLanesDirectionValueConverter();

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            var words = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (words.Length is < 1 or > 3 || words.Any(t => t.Type != TokenType.Ident))
                return null;

            if (words.Length == 1 && words[0].Data.Isi(Keywords.Normal))
                return new LanesTextValue(tokens);

            if (!(words[0].Data.Isi(Keywords.Row) || words[0].Data.Isi(Keywords.Column)))
                return null;

            var fill = false;
            var track = false;
            foreach (var word in words.Skip(1))
            {
                if (word.Data.Isi("fill-reverse"))
                {
                    if (fill) return null;
                    fill = true;
                }
                else if (word.Data.Isi("track-reverse"))
                {
                    if (track) return null;
                    track = true;
                }
                else
                {
                    return null;
                }
            }

            return new LanesTextValue(tokens);
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<LanesTextValue>();
    }

    /// <summary>Validates <c>grid-lanes-pack</c> (CSS Grid 3): <c>normal | dense</c>.</summary>
    internal sealed class GridLanesPackValueConverter : IValueConverter
    {
        internal static readonly IValueConverter Instance = new GridLanesPackValueConverter();

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            var words = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (words.Length != 1 || words[0].Type != TokenType.Ident
                || !(words[0].Data.Isi(Keywords.Normal) || words[0].Data.Isi(Keywords.Dense)))
                return null;

            return new LanesTextValue(tokens);
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<LanesTextValue>();
    }

    /// <summary>The authored text of a grid-lanes keyword property.</summary>
    internal sealed class LanesTextValue : IPropertyValue
    {
        public LanesTextValue(IEnumerable<Token> tokens)
        {
            Original = new TokenValue(tokens);
        }

        public string CssText => Original.Text;
        public TokenValue Original { get; }
        public TokenValue ExtractFor(string name) => Original;
    }
}
