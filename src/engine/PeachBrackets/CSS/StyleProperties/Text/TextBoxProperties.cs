#nullable disable

using System.Collections.Generic;

namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary><c>block-step</c> (CSS Rhythmic Sizing 1 §4): <c>&lt;'block-step-size'&gt; || &lt;'block-step-insert'&gt; || &lt;'block-step-align'&gt; || &lt;'block-step-round'&gt;</c>.</summary>
    internal sealed class BlockStepProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = WithAny(
            BlockStepSizeConverter.Option().For(PropertyNames.BlockStepSize),
            BlockStepInsertConverter.Option().For(PropertyNames.BlockStepInsert),
            BlockStepAlignConverter.Option().For(PropertyNames.BlockStepAlign),
            BlockStepRoundConverter.Option().For(PropertyNames.BlockStepRound)).OrDefault();

        internal BlockStepProperty()
            : base(PropertyNames.BlockStep, PropertyFlags.None)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }

    /// <summary><c>text-box</c> (CSS Inline 3 §5.3): <c>normal | [ &lt;'text-box-trim'&gt; || &lt;'text-box-edge'&gt; ]</c>; <c>normal</c> is <c>none auto</c>.</summary>
    internal sealed class TextBoxProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new TextBoxNormalConverter().Or(WithAny(
            TextBoxTrimConverter.Option().For(PropertyNames.TextBoxTrim),
            TextBoxEdgeConverter.Option().For(PropertyNames.TextBoxEdge)).OrDefault());

        internal TextBoxProperty()
            : base(PropertyNames.TextBox, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        /// <summary><c>normal</c>: both longhands at their initial values.</summary>
        private sealed class TextBoxNormalConverter : IValueConverter
        {
            public IPropertyValue Convert(IReadOnlyList<Token> value)
            {
                var toks = new List<Token>();
                foreach (var t in value) if (t.Type != TokenType.Whitespace) toks.Add(t);
                return toks.Count == 1 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.Normal) ? new NormalValue(value) : null;
            }

            public IPropertyValue Construct(Property[] properties) => null;

            private sealed class NormalValue : IPropertyValue
            {
                public NormalValue(IReadOnlyList<Token> tokens) => Original = new TokenValue(tokens);

                public string CssText => Original.Text;

                public TokenValue Original { get; }

                public TokenValue ExtractFor(string name) =>
                    name.Is(PropertyNames.TextBoxTrim) ? TokenValue.FromString("none")
                    : name.Is(PropertyNames.TextBoxEdge) ? TokenValue.FromString("auto")
                    : null;
            }
        }
    }
}
