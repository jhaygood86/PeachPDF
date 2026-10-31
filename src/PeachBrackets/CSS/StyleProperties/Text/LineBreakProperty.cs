namespace PeachBrackets.Css
{
    internal sealed class LineBreakProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LineBreakConverter.OrDefault();

        public LineBreakProperty()
            : base(PropertyNames.LineBreak, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
