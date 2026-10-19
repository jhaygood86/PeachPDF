namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary><c>-webkit-text-stroke</c> (Compatibility Standard): the shorthand <c>&lt;line-width&gt; || &lt;color&gt;</c> for the width and the colour of the stroke around text.</summary>
    internal sealed class WebkitTextStrokeProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = WithAny(
            LineWidthConverter.Option().For(PropertyNames.WebkitTextStrokeWidth),
            ColorConverter.Option().For(PropertyNames.WebkitTextStrokeColor)).OrDefault();

        internal WebkitTextStrokeProperty()
            : base(PropertyNames.WebkitTextStroke, PropertyFlags.Inherited | PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
