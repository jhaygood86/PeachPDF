namespace PeachPDF.CSS
{
    internal sealed class PdfFormFieldPlaceholderProperty : Property
    {
        internal PdfFormFieldPlaceholderProperty() : base(PropertyNames.PdfFormFieldPlaceholder)
        {
        }

        internal override IValueConverter Converter => Converters.PdfFormFieldPlaceholderConverter.OrDefault();
    }
}
