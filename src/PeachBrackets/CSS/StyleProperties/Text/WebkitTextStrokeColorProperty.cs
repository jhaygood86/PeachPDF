namespace PeachPDF.CSS
{
    /// <summary><c>-webkit-text-stroke-color</c> (Compatibility Standard): a <c>&lt;color&gt;</c>, <c>currentcolor</c> initially. Inherited.</summary>
    internal sealed class WebkitTextStrokeColorProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.ColorConverter.OrDefault(Color.Black);

        internal WebkitTextStrokeColorProperty()
            : base(PropertyNames.WebkitTextStrokeColor, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
