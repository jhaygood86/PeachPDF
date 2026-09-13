namespace PeachPDF.CSS
{
    internal sealed class MixBlendModeProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.MixBlendModeConverter.OrDefault(BlendMode.Normal);

        internal MixBlendModeProperty()
            : base(PropertyNames.MixBlendMode)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
