namespace PeachPDF.CSS
{
    internal sealed class PdfFormFieldDoNotScrollProperty : Property
    {
        internal PdfFormFieldDoNotScrollProperty() : base(PropertyNames.PdfFormFieldDoNotScroll)
        {
        }

        internal override IValueConverter Converter => Converters.PdfFormFieldDoNotScrollConverter.OrDefault();
    }
}
