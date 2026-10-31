namespace PeachBrackets.Css
{
    internal sealed class StrokeDashoffsetProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthOrPercentOrNumberConverter.OrGlobalValue();

        public StrokeDashoffsetProperty()
            : base(PropertyNames.StrokeDashoffset, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}