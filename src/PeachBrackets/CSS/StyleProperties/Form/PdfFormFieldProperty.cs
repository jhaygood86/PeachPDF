namespace PeachBrackets.Css
{
    internal sealed class PdfFormFieldProperty : Property
    {
        internal PdfFormFieldProperty() : base(PropertyNames.PdfFormField)
        {
        }

        internal override IValueConverter Converter => Converters.PdfFormFieldConverter.OrDefault();
    }
}
