namespace PeachPDF.CSS
{
    internal sealed class LineClampProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.LineClampConverter.OrDefault(NoneKeyword.None);

        internal LineClampProperty()
            : base(PropertyNames.LineClamp)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
