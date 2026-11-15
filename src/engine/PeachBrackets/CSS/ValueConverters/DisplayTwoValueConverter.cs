#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The two-keyword <c>display</c> syntax (CSS Display 3 2.1): <c>&lt;display-outside&gt; &lt;display-inside&gt;</c> in either order, such as
    /// <c>inline grid-lanes</c> or <c>block flex</c>. It is rewritten to the single legacy keyword it is defined to equal
    /// (<c>inline flex</c> is <c>inline-flex</c>, <c>block flow-root</c> is <c>flow-root</c>), which the keyword table and everything after the
    /// cascade already know.
    /// </summary>
    internal sealed class DisplayTwoValueConverter(IValueConverter inner) : IValueConverter
    {
        private static readonly Dictionary<string, string> Block = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flow"] = Keywords.Block, ["flow-root"] = "flow-root", ["flex"] = Keywords.Flex, ["grid"] = Keywords.Grid,
            ["grid-lanes"] = Keywords.GridLanes, ["table"] = Keywords.Table,
        };

        private static readonly Dictionary<string, string> Inline = new(StringComparer.OrdinalIgnoreCase)
        {
            ["flow"] = Keywords.Inline, ["flow-root"] = Keywords.InlineBlock, ["flex"] = Keywords.InlineFlex, ["grid"] = Keywords.InlineGrid,
            ["grid-lanes"] = Keywords.InlineGridLanes, ["table"] = Keywords.InlineTable,
        };

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var words = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (words.Length != 2 || words.Any(t => t.Type != TokenType.Ident))
                return inner.Convert(value);

            foreach (var (outside, inside) in new[] { (words[0], words[1]), (words[1], words[0]) })
            {
                var table = outside.Data.Isi(Keywords.Block) ? Block : outside.Data.Isi(Keywords.Inline) ? Inline : null;
                if (table is not null && table.TryGetValue(inside.Data.ToString(), out var keyword))
                    return inner.Convert([new Token(TokenType.Ident, keyword.AsMemory(), TextPosition.Empty)]);
            }

            return null;
        }

        public IPropertyValue Construct(Property[] properties) => inner.Construct(properties);
    }
}
