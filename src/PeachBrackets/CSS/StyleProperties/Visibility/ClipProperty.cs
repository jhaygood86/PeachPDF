namespace PeachBrackets.Css
{
    internal sealed class ClipProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.ShapeConverter.OrDefault();

        internal ClipProperty()
            : base(PropertyNames.Clip, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}