namespace PeachPDF.CSS
{
    internal sealed class BackgroundClipProperty : Property
    {
        private static readonly IValueConverter ListConverter =
            Converters.BackgroundClipConverter.FromList().OrDefault(BackgroundClipKeyword.BorderBox);

        internal BackgroundClipProperty()
            : base(PropertyNames.BackgroundClip)
        {
        }

        internal override IValueConverter Converter => ListConverter;
    }
}