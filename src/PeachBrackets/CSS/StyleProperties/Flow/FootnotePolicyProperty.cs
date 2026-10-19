namespace PeachPDF.CSS
{
    internal sealed class FootnotePolicyProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FootnotePolicyConverter.OrDefault(FootnotePolicyMode.Auto);

        internal FootnotePolicyProperty()
            : base(PropertyNames.FootnotePolicy)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
