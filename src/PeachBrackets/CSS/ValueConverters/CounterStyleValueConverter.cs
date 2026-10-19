#nullable enable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>&lt;counter-style&gt; = &lt;counter-style-name&gt; | &lt;symbols()&gt;</c> (CSS Counter Styles Level 3
    /// §3.9) for <c>list-style-type</c>, accepted alongside the predefined keywords and a literal
    /// <c>&lt;string&gt;</c>. A name that is not predefined is a valid author-defined counter style as far as
    /// parsing goes: whether an <c>@counter-style</c> by that name exists is only known at layout, where an
    /// unknown name falls back to <c>decimal</c> (§2).
    /// </summary>
    internal sealed class CounterStyleValueConverter : IValueConverter
    {
        // CSS-wide keywords can never be a custom ident, and inside/outside keep list-style's other
        // operand (list-style-position) from being claimed as a counter style name.
        private static readonly HashSet<string> ReservedIdents = new(StringComparer.OrdinalIgnoreCase)
        {
            "inherit", "initial", "unset", "revert", "revert-layer", "default", "inside", "outside",
        };

        public IPropertyValue? Convert(IReadOnlyList<Token> value)
        {
            var element = value.OnlyOrDefault();

            switch (element)
            {
                case { Type: TokenType.Ident } ident when !ReservedIdents.Contains(ident.Data.ToString()):
                case { Type: TokenType.Function } function when CounterStyleGrammar.TryParseSymbolsFunction(function, out _, out _):
                    return Converters.Any.Convert(value);
                default:
                    return null;
            }
        }

        public IPropertyValue? Construct(Property[] properties) => Converters.Any.Construct(properties);
    }
}
