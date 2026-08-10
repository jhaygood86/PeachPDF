#nullable disable

using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Converter for the <c>running()</c> function used by the <c>position</c> property (css-gcpm-3
    /// "Running Elements"): <c>position: running(&lt;custom-ident&gt;)</c> removes the element from
    /// normal flow entirely, making it available to a page margin box via
    /// <c>content: element(&lt;custom-ident&gt;)</c>.
    /// </summary>
    internal sealed class RunningFunctionConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            return PositionValueGrammar.TryParseRunningTokens(value, out var name)
                ? new RunningFunctionValue(name, value)
                : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<RunningFunctionValue>();
        }

        private sealed class RunningFunctionValue : IPropertyValue
        {
            private readonly string _name;

            public RunningFunctionValue(string name, IEnumerable<Token> tokens)
            {
                _name = name;
                Original = new TokenValue(tokens);
            }

            public string CssText => $"running({_name})";

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;

            public string Name => _name;
        }
    }
}
