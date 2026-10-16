namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>stop-color</c> of a gradient <c>&lt;stop&gt;</c> (SVG 2 §13.4): <c>&lt;color&gt;</c> or <c>currentcolor</c>. Not inherited; the SVG gradient builder reads it.
    /// </summary>
    internal sealed class StopColorProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.CurrentColorConverter.OrGlobalValue();

        internal StopColorProperty()
            : base(PropertyNames.StopColor, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
