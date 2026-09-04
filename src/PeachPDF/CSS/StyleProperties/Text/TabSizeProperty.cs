namespace PeachPDF.CSS
{
    internal sealed class TabSizeProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.TabSizeConverter;

        internal TabSizeProperty()
            : base(PropertyNames.TabSize, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
