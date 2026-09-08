#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.CSS
{
    internal sealed class StructValueConverter<T> : IValueConverter
        where T : struct, IFormattable
    {
        private readonly Func<IReadOnlyList<Token>, T?> _converter;

        public StructValueConverter(Func<IReadOnlyList<Token>, T?> converter)
        {
            _converter = converter;
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            var val = _converter(value);
            return val.HasValue ? new StructValue(val.Value, value) : null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            return properties.Guard<StructValue>();
        }

        private sealed class StructValue : IPropertyValue
        {
            private readonly T _value;

            public StructValue(T value, IReadOnlyList<Token> tokens)
            {
                _value = value;
                Original = new TokenValue(tokens);
            }

            public string CssText => _value.ToString(null, CultureInfo.InvariantCulture);

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                return Original;
            }
        }
    }
}
