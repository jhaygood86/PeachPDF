namespace PeachPDF.CSS
{
    internal sealed class AlignContentProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.AlignContentConverter;

        internal AlignContentProperty()
            : base(PropertyNames.AlignContent)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }
}