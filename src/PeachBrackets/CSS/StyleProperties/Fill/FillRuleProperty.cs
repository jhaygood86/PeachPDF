namespace PeachPDF.CSS
{
    internal sealed class FillRuleProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FillRuleConverter.OrDefault(FillRule.Nonzero);

        public FillRuleProperty()
            : base(PropertyNames.FillRule, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}