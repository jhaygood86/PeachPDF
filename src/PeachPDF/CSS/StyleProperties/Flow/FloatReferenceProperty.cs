namespace PeachPDF.CSS
{
    internal sealed class FloatReferenceProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FloatReferenceConverter.OrDefault(FloatReference.Inline);

        internal FloatReferenceProperty()
            : base(PropertyNames.FloatReference)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
