namespace PeachPDF.CSS
{
    /// <summary>CSS Fonts 4 §3.5 <c>FontSynthesisPosition</c>: <c>auto | none</c>.</summary>
    internal sealed class FontSynthesisPositionProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontSynthesisModeConverter.OrDefault(FontSynthesisMode.Auto);

        internal FontSynthesisPositionProperty()
            : base(PropertyNames.FontSynthesisPosition, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
