namespace PeachPDF.CSS
{
    internal sealed class LineBreakProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LineBreakConverter.OrDefault();

        public LineBreakProperty()
            : base(PropertyNames.LineBreak)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
