namespace PeachPDF.CSS
{
    internal sealed class InsetInlineEndProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.AutoLengthOrPercentConverter.OrDefault(Keywords.Auto);

        internal InsetInlineEndProperty()
            : base(PropertyNames.InsetInlineEnd, PropertyFlags.Unitless | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
