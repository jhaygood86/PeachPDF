#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>container</c> shorthand (CSS Containment 3 SS2):
    /// <c>&lt;'container-name'&gt; [ / &lt;'container-type'&gt; ]?</c>. Mirrors
    /// <see cref="BorderRadiusConverter"/>'s slash-splitting shape, simplified for two non-periodic,
    /// heterogeneously-typed fields rather than four periodic same-typed corners - container-name uses
    /// <see cref="Converters.LiteralsConverter"/> (the same bare-identifier-list acceptance
    /// <see cref="ContainerNameProperty"/> already uses standalone) and container-type uses
    /// <see cref="Converters.ContainerTypeConverter"/>. An omitted type resets to its initial value
    /// (<c>normal</c>) via <see cref="ShorthandProperty.Export"/>'s existing empty-TokenValue handling.
    /// </summary>
    internal sealed class ContainerShorthandConverter : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var front = new List<Token>();
            var back = new List<Token>();
            var current = front;

            foreach (var token in value)
            {
                if (token.Type == TokenType.Delim && token.Data.Is("/"))
                {
                    if (current == back) return null;
                    current = back;
                }
                else
                {
                    current.Add(token);
                }
            }

            var name = Converters.LiteralsConverter.Convert(front);
            if (name == null) return null;

            var hasType = current == back;
            var type = hasType ? Converters.ContainerTypeConverter.Convert(back) : null;
            if (hasType && type == null) return null;

            return new ContainerShorthandValue(name, type, new TokenValue(value));
        }

        public IPropertyValue Construct(Property[] properties)
        {
            if (properties.Length != 2) return null;

            var nameProperty = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.ContainerName));
            var typeProperty = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.ContainerType));
            if (nameProperty?.DeclaredValue is not { } nameValue || typeProperty?.DeclaredValue is not { } typeValue)
                return null;

            var name = Converters.LiteralsConverter.Convert(nameValue.Original);
            var type = Converters.ContainerTypeConverter.Convert(typeValue.Original);
            if (name == null || type == null) return null;

            var original = new TokenValue(nameValue.Original
                .Concat([new Token(TokenType.Delim, "/", TextPosition.Empty)])
                .Concat(typeValue.Original));

            return new ContainerShorthandValue(name, type, original);
        }

        private sealed class ContainerShorthandValue : IPropertyValue
        {
            private readonly IPropertyValue _name;
            private readonly IPropertyValue _type;

            public ContainerShorthandValue(IPropertyValue name, IPropertyValue type, TokenValue original)
            {
                _name = name;
                _type = type;
                Original = original;
            }

            public string CssText =>
                _type is null ? _name.CssText : string.Concat(_name.CssText, " / ", _type.CssText);

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name)
            {
                if (name.Is(PropertyNames.ContainerName)) return _name.Original;
                if (name.Is(PropertyNames.ContainerType)) return _type?.Original ?? TokenValue.Empty;
                return TokenValue.Empty;
            }
        }
    }
}
