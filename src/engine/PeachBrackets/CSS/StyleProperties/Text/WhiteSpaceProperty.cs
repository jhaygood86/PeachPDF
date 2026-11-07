#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// css-text-4 section 4.1's real shorthand over <c>white-space-collapse</c> and <c>text-wrap-mode</c>
    /// (it also covers <c>white-space-trim</c>, which is not supported). <c>normal</c>, <c>pre</c>,
    /// <c>pre-wrap</c>, <c>pre-line</c> and <c>nowrap</c> are fixed pairs; otherwise it is
    /// <c>&lt;'white-space-collapse'&gt; || &lt;'text-wrap-mode'&gt;</c>.
    /// </summary>
    internal sealed class WhiteSpaceProperty : ShorthandProperty
    {
        internal WhiteSpaceProperty()
            : base(PropertyNames.WhiteSpace, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.WhiteSpaceConverter;
    }

    /// <summary>css-text-4 section 5.1's <c>text-wrap</c> shorthand over <c>text-wrap-mode</c> and <c>text-wrap-style</c>.</summary>
    internal sealed class TextWrapProperty : ShorthandProperty
    {
        internal TextWrapProperty()
            : base(PropertyNames.TextWrap, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.TextWrapConverter;
    }

    /// <summary>The five legacy <c>white-space</c> keywords, each a fixed pair of longhand values.</summary>
    internal sealed class WhiteSpaceKeywordConverter : IValueConverter
    {
        private static readonly (string Keyword, string Collapse, string Mode)[] Pairs =
        [
            (Keywords.Normal, Keywords.Collapse, Keywords.Wrap),
            (Keywords.Pre, Keywords.Preserve, Keywords.Nowrap),
            (Keywords.PreWrap, Keywords.Preserve, Keywords.Wrap),
            (Keywords.PreLine, Keywords.PreserveBreaks, Keywords.Wrap),
            (Keywords.Nowrap, Keywords.Collapse, Keywords.Nowrap),
        ];

        public IPropertyValue Convert(IReadOnlyList<Token> value)
        {
            foreach (var (keyword, collapse, mode) in Pairs)
            {
                if (value.Is(keyword)) return new PairValue(keyword, collapse, mode);
            }

            return null;
        }

        public IPropertyValue Construct(Property[] properties)
        {
            var collapse = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.WhiteSpaceCollapse));
            var mode = properties.FirstOrDefault(p => p.Name.Is(PropertyNames.TextWrapMode));
            if (collapse is null || mode is null || !collapse.HasValue || !mode.HasValue) return null;

            foreach (var (keyword, c, m) in Pairs)
            {
                if (collapse.Value.Is(c) && mode.Value.Is(m)) return new PairValue(keyword, c, m);
            }

            return new PairValue(collapse.Value + " " + mode.Value, collapse.Value, mode.Value);
        }

        private sealed class PairValue : IPropertyValue
        {
            private readonly string _collapse;
            private readonly string _mode;

            public PairValue(string cssText, string collapse, string mode)
            {
                CssText = cssText;
                _collapse = collapse;
                _mode = mode;
            }

            public string CssText { get; }

            public TokenValue Original => TokenValue.FromString(CssText);

            public TokenValue ExtractFor(string name) =>
                name.Is(PropertyNames.WhiteSpaceCollapse) ? TokenValue.FromString(_collapse) :
                name.Is(PropertyNames.TextWrapMode) ? TokenValue.FromString(_mode) : null;
        }
    }
}
