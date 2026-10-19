namespace PeachPDF.CSS
{
    /// <summary>CSS Fonts 4 §3.5 <c>FontSynthesisStyle</c>: <c>auto | none</c>.</summary>
    internal sealed class FontSynthesisStyleProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontSynthesisModeConverter.OrDefault(FontSynthesisMode.Auto);

        internal FontSynthesisStyleProperty()
            : base(PropertyNames.FontSynthesisStyle, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
