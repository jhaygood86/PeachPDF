namespace PeachPDF.CSS
{
    internal sealed class PageBreakInsideProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.PageBreakInsideModeConverter.OrDefault(BreakMode.Auto);

        internal PageBreakInsideProperty()
            : base(PropertyNames.PageBreakInside)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}