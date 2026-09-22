namespace PeachPDF.CSS
{
    internal sealed class FootnoteDisplayProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FootnoteDisplayConverter.OrDefault(FootnoteDisplayMode.Block);

        internal FootnoteDisplayProperty()
            : base(PropertyNames.FootnoteDisplay)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
