namespace PeachPDF.CSS
{
    /// <summary><c>-webkit-text-stroke-width</c> (Compatibility Standard): a <c>&lt;line-width&gt;</c>, <c>0</c> initially. Inherited.</summary>
    internal sealed class WebkitTextStrokeWidthProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LineWidthConverter.OrDefault(Length.Zero);

        internal WebkitTextStrokeWidthProperty()
            : base(PropertyNames.WebkitTextStrokeWidth, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
