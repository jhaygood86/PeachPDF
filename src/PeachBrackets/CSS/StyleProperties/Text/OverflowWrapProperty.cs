namespace PeachPDF.CSS
{
    internal sealed class OverflowWrapProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.OverflowWrapConverter.OrDefault();

        public OverflowWrapProperty()
            : base(PropertyNames.OverflowWrap, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
