#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Converter for <c>prince-script(&lt;name&gt; [, &lt;content-list-item&gt;]*)</c>, a content item whose text a script computes: the name is of a function
    /// the document's scripts registered, and each further argument is a content item (a string, <c>counter()</c>, <c>attr()</c>...) whose text is the
    /// function's argument. The function is called while the document is laid out; what it returns is the text. Without a script host the item is empty.
    /// </summary>
    internal sealed class ScriptFunctionConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var first = value.OnlyOrDefault();
            if (first is not { Type: TokenType.Function } function || !function.Data.Isi(FunctionNames.ScriptFunction))
                return null;

            var arguments = function.ArgumentTokens.Where(t => t.Type != TokenType.Whitespace).ToList();
            if (arguments.Count == 0 || arguments[0] is not { Type: TokenType.Hash or TokenType.AtKeyword or TokenType.Ident or TokenType.String })
                return null;

            // After the name come comma-separated items: name , item , item. An item is one token (a string, a counter() or an attr() function).
            for (var i = 1; i < arguments.Count; i++)
            {
                var isComma = arguments[i].Type == TokenType.Comma;
                if (isComma != (i % 2 == 1))
                    return null;
            }

            if (arguments.Count % 2 == 0)
                return null;

            return new ScriptFunctionValue(value);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<ScriptFunctionValue>();
        }

        private sealed class ScriptFunctionValue : IPropertyValue
        {
            public ScriptFunctionValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                return Original;
            }
        }
    }
}
