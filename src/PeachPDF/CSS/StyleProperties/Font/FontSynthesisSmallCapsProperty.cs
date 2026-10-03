namespace PeachPDF.CSS
{
    /// <summary>CSS Fonts 4 §3.5 <c>FontSynthesisSmallCaps</c>: <c>auto | none</c>.</summary>
    internal sealed class FontSynthesisSmallCapsProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontSynthesisModeConverter.OrDefault(FontSynthesisMode.Auto);

        internal FontSynthesisSmallCapsProperty()
            : base(PropertyNames.FontSynthesisSmallCaps, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
