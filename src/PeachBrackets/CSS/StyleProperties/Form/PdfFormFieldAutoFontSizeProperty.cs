namespace PeachPDF.CSS
{
    internal sealed class PdfFormFieldAutoFontSizeProperty : Property
    {
        internal PdfFormFieldAutoFontSizeProperty() : base(PropertyNames.PdfFormFieldAutoFontSize)
        {
        }

        internal override IValueConverter Converter => Converters.PdfFormFieldAutoFontSizeConverter.OrDefault();
    }
}
