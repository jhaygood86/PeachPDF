namespace PeachPDF.CSS
{
    internal sealed class UnicodeRangeProperty : Property
    {
        public UnicodeRangeProperty()
            : base(PropertyNames.UnicodeRange)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}