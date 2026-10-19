namespace PeachPDF.CSS
{
    internal sealed class FlexWrapProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.FlexWrapConverter;

        internal FlexWrapProperty()
            : base(PropertyNames.FlexWrap)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }
}
