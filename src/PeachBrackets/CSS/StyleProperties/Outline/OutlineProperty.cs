namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class OutlineProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = WithAny(
            LineWidthConverter.Option().For(PropertyNames.OutlineWidth),
            OutlineStyleConverter.Option().For(PropertyNames.OutlineStyle),
            InvertedColorConverter.Option().For(PropertyNames.OutlineColor)).OrDefault();

        internal OutlineProperty()
            : base(PropertyNames.Outline, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}