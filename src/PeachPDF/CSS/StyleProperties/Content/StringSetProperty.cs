namespace PeachPDF.CSS.StyleProperties.Content
{
    internal sealed class StringSetProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.StringSetConverter;

        internal StringSetProperty()
            : base(PropertyNames.StringSet)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
