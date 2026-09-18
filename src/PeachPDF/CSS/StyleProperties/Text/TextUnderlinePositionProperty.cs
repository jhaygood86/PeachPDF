namespace PeachPDF.CSS
{
    internal sealed class TextUnderlinePositionProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            new TextUnderlinePositionCompoundConverter().OrDefault(TextUnderlinePosition.Auto);

        internal TextUnderlinePositionProperty()
            : base(PropertyNames.TextUnderlinePosition, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
