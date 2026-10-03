namespace PeachPDF.CSS
{
    /// <summary>CSS Fonts 4 §3.5 <c>FontSynthesisWeight</c>: <c>auto | none</c>.</summary>
    internal sealed class FontSynthesisWeightProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontSynthesisModeConverter.OrDefault(FontSynthesisMode.Auto);

        internal FontSynthesisWeightProperty()
            : base(PropertyNames.FontSynthesisWeight, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
