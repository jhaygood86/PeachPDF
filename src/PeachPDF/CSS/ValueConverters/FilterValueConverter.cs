#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Validates a <c>filter</c> value through the shared <see cref="FilterGrammar"/> - accepting
    /// <c>none</c> or a space-separated list of <c>&lt;filter-function&gt;</c> values, and rejecting
    /// everything else so an invalid value (e.g. <c>filter: banana()</c>) is dropped at parse time. The
    /// authored value text is preserved verbatim so the paint-time resolver
    /// (<c>PeachPDF.Html.Core.Paint.FilterEffectResolver</c>, via <c>CssBox.ActualFilterFunctions</c>)
    /// re-tokenizes and resolves the same value against the box - a single grammar shared across both
    /// layers, mirroring <see cref="BoxShadowValueConverter"/>.
    /// </summary>
    internal sealed class FilterValueConverter : IValueConverter
    {
        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var tokens = value.ToArray();
            return FilterGrammar.TryParse(tokens) is null ? null : new FilterValue(tokens);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<FilterValue>();
        }

        private sealed class FilterValue : IPropertyValue
        {
            public FilterValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            // Preserve the authored text verbatim - Layer B re-parses and resolves this string.
            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
