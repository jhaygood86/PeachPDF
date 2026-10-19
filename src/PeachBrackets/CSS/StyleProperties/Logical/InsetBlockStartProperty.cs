namespace PeachPDF.CSS
{
    internal sealed class InsetBlockStartProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.AutoLengthOrPercentConverter.OrDefault(Keywords.Auto);

        internal InsetBlockStartProperty()
            : base(PropertyNames.InsetBlockStart, PropertyFlags.Unitless | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
