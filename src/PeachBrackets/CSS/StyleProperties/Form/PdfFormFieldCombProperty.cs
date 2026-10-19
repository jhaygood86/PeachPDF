namespace PeachPDF.CSS
{
    internal sealed class PdfFormFieldCombProperty : Property
    {
        internal PdfFormFieldCombProperty() : base(PropertyNames.PdfFormFieldComb)
        {
        }

        internal override IValueConverter Converter => Converters.PdfFormFieldCombConverter.OrDefault();
    }
}
