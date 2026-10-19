namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat shorthand for Prince's own -prince-pdf-form-field-settings spelling - see PrincePdfFormFieldSettingsShorthandConverter for the grammar this accepts.</summary>
    internal sealed class PrincePdfFormFieldSettingsProperty : ShorthandProperty
    {
        static readonly IValueConverter StyleConverter = Converters.PrincePdfFormFieldSettingsConverter.OrDefault();

        internal PrincePdfFormFieldSettingsProperty()
            : base(PropertyNames.PrincePdfFormFieldSettings)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
