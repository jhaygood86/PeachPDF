#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>grid-lanes</c> shorthand (CSS Grid 3): a <c>&lt;track-list&gt;</c> (or <c>none</c>), optional <c>grid-template-areas</c> strings and
    /// the <c>grid-lanes-direction</c> words (<c>normal | row | column</c>, <c>fill-reverse</c>, <c>track-reverse</c>), in any order. The track list is the
    /// <c>grid-template-rows</c> when the direction is <c>row</c> and the <c>grid-template-columns</c> otherwise; the other template, the areas
    /// when no string is given, and the direction when no word is given, reset to their initial values.
    /// </summary>
    internal sealed class GridLanesShorthandValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = new List<Token>(value);
            var areas = new List<Token>();
            var tracks = new List<Token>();
            string axis = null;
            var fill = false;
            var track = false;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.String)
                {
                    if (areas.Count > 0) areas.Add(Token.Whitespace);
                    areas.Add(token);
                }
                else if (token.Type == TokenType.Ident && (token.Data.Isi(Keywords.Row) || token.Data.Isi(Keywords.Column) || token.Data.Isi(Keywords.Normal)))
                {
                    if (axis is not null) return null;
                    axis = token.Data.ToString().ToLowerInvariant();
                }
                else if (token.Type == TokenType.Ident && token.Data.Isi("fill-reverse"))
                {
                    if (fill) return null;
                    fill = true;
                }
                else if (token.Type == TokenType.Ident && token.Data.Isi("track-reverse"))
                {
                    if (track) return null;
                    track = true;
                }
                else
                {
                    tracks.Add(token);
                }
            }

            var trackSlice = GridTemplateShorthand.Trim(tracks);
            if (trackSlice.Count == 0) return null;

            var none = GridTemplateShorthand.Significant(trackSlice) is [{ Type: TokenType.Ident } only] && only.Data.Isi(Keywords.None);
            if (GridTemplateShorthand.TryTemplateAxis(trackSlice) is null) return null;
            if (areas.Count > 0 && GridTemplateAreasGrammar.TryParse(areas) is null) return null;
            if (axis == Keywords.Normal && (fill || track)) return null;

            // `none` alone is the column direction (css-grid-3), and so is a reverse word with no axis.
            axis ??= none || fill || track ? Keywords.Column : Keywords.Normal;

            var words = new List<string> { axis };
            if (fill) words.Add("fill-reverse");
            if (track) words.Add("track-reverse");
            var direction = new List<Token>();
            foreach (var word in words)
            {
                if (direction.Count > 0) direction.Add(Token.Whitespace);
                direction.Add(new Token(TokenType.Ident, word.AsMemory(), TextPosition.Empty));
            }

            var map = new Dictionary<string, IReadOnlyList<Token>>
            {
                [axis == Keywords.Row ? PropertyNames.GridTemplateRows : PropertyNames.GridTemplateColumns] = trackSlice,
                [PropertyNames.GridLanesDirection] = direction,
            };
            if (areas.Count > 0) map[PropertyNames.GridTemplateAreas] = areas;
            else if (none) map[PropertyNames.GridTemplateAreas] = trackSlice;
            if (none) map[axis == Keywords.Row ? PropertyNames.GridTemplateColumns : PropertyNames.GridTemplateRows] = trackSlice;

            return new MappedShorthandValue(map, tokens);
        }

        public IPropertyValue Construct(Property[] properties) => properties.Guard<MappedShorthandValue>();
    }
}
