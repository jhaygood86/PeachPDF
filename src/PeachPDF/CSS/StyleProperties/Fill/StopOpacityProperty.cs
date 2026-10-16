namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>stop-opacity</c> of a gradient <c>&lt;stop&gt;</c> (SVG 2 §13.4): a <c>&lt;number&gt;</c> or <c>&lt;percentage&gt;</c>. Not inherited; the SVG gradient builder reads it.
    /// </summary>
    internal sealed class StopOpacityProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.NumberConverter.Or(Converters.PercentConverter).OrDefault(1f);

        internal StopOpacityProperty()
            : base(PropertyNames.StopOpacity, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
