namespace PeachPDF.CSS
{
    internal sealed class OrphansProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.NaturalIntegerConverter.OrDefault(2);

        internal OrphansProperty()
            : base(PropertyNames.Orphans, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}