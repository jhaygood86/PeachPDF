namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class CounterSetProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Continuous(
            WithOrder(IdentifierConverter.Required(), IntegerConverter.Option(0))).OrDefault();

        internal CounterSetProperty()
            : base(PropertyNames.CounterSet)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
